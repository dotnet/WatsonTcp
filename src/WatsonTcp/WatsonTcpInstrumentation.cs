namespace WatsonTcp
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Net.Sockets;

    /// <summary>
    /// Owns the <see cref="Meter"/> and <see cref="ActivitySource"/> a single WatsonTcp client or
    /// server records telemetry into, along with every instrument and span factory.  One instance
    /// lives per client/server and is disposed with its owner.  All recording is fire-and-forget and
    /// never throws into the caller's send, receive, or connection path.  When metrics are disabled the
    /// meter is never created (so no instruments are published); when tracing is disabled span factories
    /// return <c>null</c>.
    /// </summary>
    internal sealed class WatsonTcpInstrumentation : IDisposable
    {
        #region Internal-Constants

        internal const string RoleServer = "server";
        internal const string RoleClient = "client";

        internal const string ProtocolTcp = "tcp";
        internal const string ProtocolSsl = "ssl";

        internal const string OutcomeAccepted = "accepted";
        internal const string OutcomeConnected = "connected";
        internal const string OutcomeRejectedMaxConnections = "rejected_maxconnections";
        internal const string OutcomeRejectedNotPermitted = "rejected_notpermitted";
        internal const string OutcomeRejectedBlocked = "rejected_blocked";
        internal const string OutcomeRejectedAuthorization = "rejected_authorization";
        internal const string OutcomeFailed = "failed";

        internal const string OutcomeSuccess = "success";
        internal const string OutcomeFailure = "failure";
        internal const string OutcomeTimeout = "timeout";
        internal const string OutcomeCanceled = "canceled";

        internal const string OutcomeCompleted = "completed";

        internal const string MessageKindData = "data";
        internal const string MessageKindControl = "control";
        internal const string MessageKindSyncRequest = "sync_request";
        internal const string MessageKindSyncResponse = "sync_response";

        internal const string KindRequest = "request";
        internal const string KindResponse = "response";

        #endregion

        #region Private-Members

        // Latency histogram buckets, in seconds, mirroring Radiant's LatencyBuckets.Network preset.
        private static readonly double[] _NetworkLatencyBuckets =
            new double[] { 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0, 30.0, 60.0, 120.0 };

        private readonly string _Role;
        private readonly string _Protocol;
        private readonly bool _MetricsEnabled;
        private readonly bool _TracingEnabled;

        private readonly Meter _Meter;
        private readonly ActivitySource _ActivitySource;
        private readonly KeyValuePair<string, object>[] _BaseTagArray;

        private readonly Counter<long> _MessagesSent;
        private readonly Counter<long> _MessagesReceived;
        private readonly Counter<long> _BytesSent;
        private readonly Counter<long> _BytesReceived;
        private readonly Histogram<long> _MessageSentSize;
        private readonly Histogram<long> _MessageReceivedSize;
        private readonly Histogram<double> _MessageSendDuration;
        private readonly Counter<long> _ConnectionsTotal;
        private readonly Counter<long> _DisconnectionsTotal;
        private readonly Counter<long> _HandshakesTotal;
        private readonly Histogram<double> _HandshakeDuration;
        private readonly Counter<long> _AuthenticationsTotal;
        private readonly Counter<long> _AuthorizationsTotal;
        private readonly Counter<long> _SyncRequestsSent;
        private readonly Counter<long> _SyncResponsesReceived;
        private readonly Histogram<double> _SyncDuration;
        private readonly Counter<long> _SyncExpired;
        private readonly Counter<long> _ExceptionsTotal;
        private readonly Counter<long> _ListenerTransientErrors;
        private readonly Counter<long> _StreamDrainedBytes;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="role">Emitting side; one of <see cref="RoleServer"/> or <see cref="RoleClient"/>.</param>
        /// <param name="protocol">Transport; one of <see cref="ProtocolTcp"/> or <see cref="ProtocolSsl"/>.</param>
        /// <param name="metricsEnabled">When false, no meter or instruments are created.</param>
        /// <param name="tracingEnabled">When false, span factories return null.</param>
        /// <param name="activeConnectionsCallback">Callback returning the current live connection count, or null to omit that gauge.</param>
        /// <param name="pendingConnectionsCallback">Callback returning the current pending connection count, or null to omit that gauge.</param>
        /// <param name="pendingSyncRequestsCallback">Callback returning the current in-flight synchronous request count, or null to omit that gauge.</param>
        /// <param name="uptimeSecondsCallback">Callback returning seconds since start, or null to omit that gauge.</param>
        internal WatsonTcpInstrumentation(
            string role,
            string protocol,
            bool metricsEnabled,
            bool tracingEnabled,
            Func<long> activeConnectionsCallback,
            Func<long> pendingConnectionsCallback,
            Func<long> pendingSyncRequestsCallback,
            Func<double> uptimeSecondsCallback)
        {
            if (String.IsNullOrEmpty(role)) throw new ArgumentNullException(nameof(role));
            if (String.IsNullOrEmpty(protocol)) throw new ArgumentNullException(nameof(protocol));

            _Role = role;
            _Protocol = protocol;
            _MetricsEnabled = metricsEnabled;
            _TracingEnabled = tracingEnabled;

            _BaseTagArray = new KeyValuePair<string, object>[]
            {
                new KeyValuePair<string, object>(WatsonTcpMetrics.TagRole, _Role),
                new KeyValuePair<string, object>(WatsonTcpMetrics.TagProtocol, _Protocol)
            };

            if (_TracingEnabled)
            {
                _ActivitySource = new ActivitySource(WatsonTcpMetrics.ActivitySourceName);
            }

            if (!_MetricsEnabled) return;

            _Meter = new Meter(WatsonTcpMetrics.MeterName);

            _MessagesSent = _Meter.CreateCounter<long>(WatsonTcpMetrics.MessagesSent, WatsonTcpMetrics.UnitMessage, "Messages written to the wire.");
            _MessagesReceived = _Meter.CreateCounter<long>(WatsonTcpMetrics.MessagesReceived, WatsonTcpMetrics.UnitMessage, "Messages read from the wire.");
            _BytesSent = _Meter.CreateCounter<long>(WatsonTcpMetrics.BytesSent, WatsonTcpMetrics.UnitBytes, "Payload bytes sent.");
            _BytesReceived = _Meter.CreateCounter<long>(WatsonTcpMetrics.BytesReceived, WatsonTcpMetrics.UnitBytes, "Payload bytes received.");
            _MessageSentSize = _Meter.CreateHistogram<long>(WatsonTcpMetrics.MessageSentSize, WatsonTcpMetrics.UnitBytes, "Distribution of sent message sizes.");
            _MessageReceivedSize = _Meter.CreateHistogram<long>(WatsonTcpMetrics.MessageReceivedSize, WatsonTcpMetrics.UnitBytes, "Distribution of received message sizes.");
            _MessageSendDuration = _Meter.CreateHistogram<double>(WatsonTcpMetrics.MessageSendDuration, WatsonTcpMetrics.UnitSeconds, "Time to write a message to the transport stream.");
            _ConnectionsTotal = _Meter.CreateCounter<long>(WatsonTcpMetrics.ConnectionsTotal, WatsonTcpMetrics.UnitConnection, "Connection admission outcomes.");
            _DisconnectionsTotal = _Meter.CreateCounter<long>(WatsonTcpMetrics.DisconnectionsTotal, WatsonTcpMetrics.UnitConnection, "Disconnections by reason.");
            _HandshakesTotal = _Meter.CreateCounter<long>(WatsonTcpMetrics.HandshakesTotal, WatsonTcpMetrics.UnitHandshake, "Custom-handshake completions.");
            _HandshakeDuration = _Meter.CreateHistogram<double>(WatsonTcpMetrics.HandshakeDuration, WatsonTcpMetrics.UnitSeconds, "Custom-handshake duration.");
            _AuthenticationsTotal = _Meter.CreateCounter<long>(WatsonTcpMetrics.AuthenticationsTotal, WatsonTcpMetrics.UnitAuthentication, "Preshared-key authentication results.");
            _AuthorizationsTotal = _Meter.CreateCounter<long>(WatsonTcpMetrics.AuthorizationsTotal, WatsonTcpMetrics.UnitAuthorization, "Connection-authorization results.");
            _SyncRequestsSent = _Meter.CreateCounter<long>(WatsonTcpMetrics.SyncRequestsSent, WatsonTcpMetrics.UnitRequest, "Synchronous requests issued.");
            _SyncResponsesReceived = _Meter.CreateCounter<long>(WatsonTcpMetrics.SyncResponsesReceived, WatsonTcpMetrics.UnitResponse, "Synchronous responses matched to a request.");
            _SyncDuration = _Meter.CreateHistogram<double>(WatsonTcpMetrics.SyncDuration, WatsonTcpMetrics.UnitSeconds, "Synchronous round-trip duration.");
            _SyncExpired = _Meter.CreateCounter<long>(WatsonTcpMetrics.SyncExpired, WatsonTcpMetrics.UnitMessage, "Expired synchronous requests or responses discarded.");
            _ExceptionsTotal = _Meter.CreateCounter<long>(WatsonTcpMetrics.ExceptionsTotal, WatsonTcpMetrics.UnitException, "Exceptions surfaced through ExceptionEncountered.");
            _ListenerTransientErrors = _Meter.CreateCounter<long>(WatsonTcpMetrics.ListenerTransientErrors, WatsonTcpMetrics.UnitError, "Recovered transient accept-loop socket errors.");
            _StreamDrainedBytes = _Meter.CreateCounter<long>(WatsonTcpMetrics.StreamDrainedBytes, WatsonTcpMetrics.UnitBytes, "Unread stream-payload bytes drained after a handler.");

            if (activeConnectionsCallback != null)
            {
                _Meter.CreateObservableGauge(
                    WatsonTcpMetrics.ConnectionsActive,
                    () => new Measurement<long>(SafeSampleLong(activeConnectionsCallback), _BaseTagArray),
                    WatsonTcpMetrics.UnitConnection,
                    "Current live connections.");
            }

            if (pendingConnectionsCallback != null)
            {
                _Meter.CreateObservableGauge(
                    WatsonTcpMetrics.ConnectionsPending,
                    () => new Measurement<long>(SafeSampleLong(pendingConnectionsCallback), _BaseTagArray),
                    WatsonTcpMetrics.UnitConnection,
                    "Accepted-but-not-yet-admitted connections.");
            }

            if (pendingSyncRequestsCallback != null)
            {
                _Meter.CreateObservableGauge(
                    WatsonTcpMetrics.SyncPending,
                    () => new Measurement<long>(SafeSampleLong(pendingSyncRequestsCallback), _BaseTagArray),
                    WatsonTcpMetrics.UnitRequest,
                    "In-flight synchronous conversations.");
            }

            if (uptimeSecondsCallback != null)
            {
                _Meter.CreateObservableGauge(
                    WatsonTcpMetrics.Uptime,
                    () => new Measurement<double>(SafeSampleDouble(uptimeSecondsCallback), _BaseTagArray),
                    WatsonTcpMetrics.UnitSeconds,
                    "Seconds since the client or server started.");
            }
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Returns fractional seconds elapsed since a <see cref="Stopwatch.GetTimestamp"/> reading.
        /// </summary>
        internal static double SecondsSince(long startTimestamp)
        {
            long now = Stopwatch.GetTimestamp();
            if (now <= startTimestamp) return 0.0;
            return (now - startTimestamp) / (double)Stopwatch.Frequency;
        }

        internal void MessageSent(WatsonMessage msg, long bytes, long sendStartTimestamp)
        {
            if (!_MetricsEnabled) return;

            TagList tags = KindTags(msg);
            _MessagesSent.Add(1, tags);
            if (bytes > 0)
            {
                _BytesSent.Add(bytes, BaseTags());
                _MessageSentSize.Record(bytes, BaseTags());
            }

            _MessageSendDuration.Record(SecondsSince(sendStartTimestamp), BaseTags());
        }

        internal void MessageReceived(WatsonMessage msg, long bytes)
        {
            if (!_MetricsEnabled) return;

            TagList tags = KindTags(msg);
            _MessagesReceived.Add(1, tags);
            if (bytes > 0)
            {
                _BytesReceived.Add(bytes, BaseTags());
                _MessageReceivedSize.Record(bytes, BaseTags());
            }
        }

        internal void ConnectionOutcome(string outcome)
        {
            if (!_MetricsEnabled) return;

            TagList tags = BaseTags();
            tags.Add(WatsonTcpMetrics.TagOutcome, outcome);
            _ConnectionsTotal.Add(1, tags);
        }

        internal void Disconnection(DisconnectReason reason)
        {
            if (!_MetricsEnabled) return;

            TagList tags = BaseTags();
            tags.Add(WatsonTcpMetrics.TagReason, reason.ToString());
            _DisconnectionsTotal.Add(1, tags);
        }

        internal void HandshakeCompleted(string outcome, long handshakeStartTimestamp)
        {
            if (!_MetricsEnabled) return;

            TagList tags = BaseTags();
            tags.Add(WatsonTcpMetrics.TagOutcome, outcome);
            _HandshakesTotal.Add(1, tags);
            _HandshakeDuration.Record(SecondsSince(handshakeStartTimestamp), tags);
        }

        internal void Authentication(string outcome)
        {
            if (!_MetricsEnabled) return;

            TagList tags = BaseTags();
            tags.Add(WatsonTcpMetrics.TagOutcome, outcome);
            _AuthenticationsTotal.Add(1, tags);
        }

        internal void Authorization(string outcome)
        {
            if (!_MetricsEnabled) return;

            TagList tags = BaseTags();
            tags.Add(WatsonTcpMetrics.TagOutcome, outcome);
            _AuthorizationsTotal.Add(1, tags);
        }

        internal void SyncRequestSent()
        {
            if (!_MetricsEnabled) return;
            _SyncRequestsSent.Add(1, BaseTags());
        }

        internal void SyncResponseReceived()
        {
            if (!_MetricsEnabled) return;
            _SyncResponsesReceived.Add(1, BaseTags());
        }

        internal void SyncCompleted(string outcome, long syncStartTimestamp)
        {
            if (!_MetricsEnabled) return;

            TagList tags = BaseTags();
            tags.Add(WatsonTcpMetrics.TagOutcome, outcome);
            _SyncDuration.Record(SecondsSince(syncStartTimestamp), tags);
        }

        internal void SyncExpiredMessage(string kind)
        {
            if (!_MetricsEnabled) return;

            TagList tags = BaseTags();
            tags.Add(WatsonTcpMetrics.TagKind, kind);
            _SyncExpired.Add(1, tags);
        }

        internal void ExceptionRecorded(Exception e)
        {
            if (!_MetricsEnabled || e == null) return;

            TagList tags = BaseTags();
            tags.Add(WatsonTcpMetrics.TagExceptionType, e.GetType().Name);
            _ExceptionsTotal.Add(1, tags);
        }

        internal void TransientAcceptError(SocketError error)
        {
            if (!_MetricsEnabled) return;

            TagList tags = BaseTags();
            tags.Add(WatsonTcpMetrics.TagSocketError, error.ToString());
            _ListenerTransientErrors.Add(1, tags);
        }

        internal void StreamDrained(long bytes)
        {
            if (!_MetricsEnabled || bytes <= 0) return;
            _StreamDrainedBytes.Add(bytes, BaseTags());
        }

        internal Activity StartConnectSpan(string serverAddress, int serverPort)
        {
            if (!_TracingEnabled) return null;

            Activity activity = _ActivitySource.StartActivity(WatsonTcpMetrics.SpanConnect, ActivityKind.Client);
            if (activity != null)
            {
                activity.SetTag(WatsonTcpMetrics.TagServerAddress, serverAddress);
                activity.SetTag(WatsonTcpMetrics.TagServerPort, serverPort);
                activity.SetTag(WatsonTcpMetrics.TagProtocol, _Protocol);
            }

            return activity;
        }

        internal Activity StartSessionSpan(string clientAddress, Guid clientGuid)
        {
            if (!_TracingEnabled) return null;

            Activity activity = _ActivitySource.StartActivity(WatsonTcpMetrics.SpanSession, ActivityKind.Server);
            if (activity != null)
            {
                activity.SetTag(WatsonTcpMetrics.TagClientAddress, clientAddress);
                activity.SetTag(WatsonTcpMetrics.TagClientGuid, clientGuid.ToString());
                activity.SetTag(WatsonTcpMetrics.TagProtocol, _Protocol);
            }

            return activity;
        }

        internal Activity StartHandshakeSpan()
        {
            if (!_TracingEnabled) return null;

            Activity activity = _ActivitySource.StartActivity(WatsonTcpMetrics.SpanHandshake, ActivityKind.Internal);
            if (activity != null)
            {
                activity.SetTag(WatsonTcpMetrics.TagRole, _Role);
            }

            return activity;
        }

        internal Activity StartSendSpan(long bytes, bool sync, Guid? clientGuid)
        {
            if (!_TracingEnabled) return null;

            Activity activity = _ActivitySource.StartActivity(WatsonTcpMetrics.SpanSend, ActivityKind.Producer);
            if (activity != null)
            {
                activity.SetTag(WatsonTcpMetrics.TagMessageBytes, bytes);
                activity.SetTag(WatsonTcpMetrics.TagMessageSync, sync);
                if (clientGuid.HasValue) activity.SetTag(WatsonTcpMetrics.TagClientGuid, clientGuid.Value.ToString());
            }

            return activity;
        }

        internal Activity StartReceiveSpan(long bytes, Guid? clientGuid)
        {
            if (!_TracingEnabled) return null;

            Activity activity = _ActivitySource.StartActivity(WatsonTcpMetrics.SpanReceive, ActivityKind.Consumer);
            if (activity != null)
            {
                activity.SetTag(WatsonTcpMetrics.TagMessageBytes, bytes);
                if (clientGuid.HasValue) activity.SetTag(WatsonTcpMetrics.TagClientGuid, clientGuid.Value.ToString());
            }

            return activity;
        }

        internal Activity StartSyncSpan(Guid conversationGuid, long bytes)
        {
            if (!_TracingEnabled) return null;

            Activity activity = _ActivitySource.StartActivity(WatsonTcpMetrics.SpanSync, ActivityKind.Client);
            if (activity != null)
            {
                activity.SetTag(WatsonTcpMetrics.TagConversationGuid, conversationGuid.ToString());
                activity.SetTag(WatsonTcpMetrics.TagMessageBytes, bytes);
            }

            return activity;
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Dispose of the underlying meter and activity source.
        /// </summary>
        public void Dispose()
        {
            _ActivitySource?.Dispose();
            _Meter?.Dispose();
        }

        #endregion

        #region Private-Methods

        private TagList BaseTags()
        {
            TagList tags = new TagList();
            tags.Add(WatsonTcpMetrics.TagRole, _Role);
            tags.Add(WatsonTcpMetrics.TagProtocol, _Protocol);
            return tags;
        }

        private TagList KindTags(WatsonMessage msg)
        {
            TagList tags = BaseTags();
            tags.Add(WatsonTcpMetrics.TagMessageKind, ClassifyMessage(msg));
            return tags;
        }

        private static string ClassifyMessage(WatsonMessage msg)
        {
            if (msg == null) return MessageKindData;
            if (msg.SyncRequest) return MessageKindSyncRequest;
            if (msg.SyncResponse) return MessageKindSyncResponse;
            if (msg.Status != MessageStatus.Normal) return MessageKindControl;
            return MessageKindData;
        }

        private static long SafeSampleLong(Func<long> callback)
        {
            try
            {
                return callback();
            }
            catch (Exception)
            {
                return 0L;
            }
        }

        private static double SafeSampleDouble(Func<double> callback)
        {
            try
            {
                return callback();
            }
            catch (Exception)
            {
                return 0.0;
            }
        }

        #endregion
    }
}
