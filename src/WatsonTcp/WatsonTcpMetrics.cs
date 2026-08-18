namespace WatsonTcp
{
    /// <summary>
    /// Well-known names, units, and tag keys that WatsonTcp emits telemetry under.
    /// <para>
    /// WatsonTcp publishes metrics to a <see cref="System.Diagnostics.Metrics.Meter"/> and
    /// distributed-tracing spans to a <see cref="System.Diagnostics.ActivitySource"/>, both named
    /// <see cref="MeterName"/> (<c>"WatsonTcp"</c>).  These names are the public contract between
    /// WatsonTcp and any telemetry host (Radiant, the OpenTelemetry SDK, Prometheus, and others).
    /// They are stable across releases; treat them like public API.
    /// </para>
    /// <para>
    /// A host observes WatsonTcp by subscribing to the meter and activity source by name, for
    /// example <c>MeterProviderBuilder.AddMeter(WatsonTcpMetrics.MeterName)</c> or, for a Radiant
    /// host, <c>settings.Sources.AddMeter(WatsonTcpMetrics.MeterName)</c>.  Metric names are dotted
    /// and lowercase and units are UCUM strings, so the OpenTelemetry Prometheus exporter produces
    /// conventional series names automatically.
    /// </para>
    /// </summary>
    public static class WatsonTcpMetrics
    {
        #region Sources

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.Metrics.Meter"/> WatsonTcp records all metrics
        /// into, and of the <see cref="System.Diagnostics.ActivitySource"/> WatsonTcp starts all
        /// spans from.  Value is <c>"WatsonTcp"</c>.  Stable across releases.
        /// </summary>
        public const string MeterName = "WatsonTcp";

        /// <summary>
        /// Name of the <see cref="System.Diagnostics.ActivitySource"/> WatsonTcp starts spans from.
        /// Identical to <see cref="MeterName"/> (<c>"WatsonTcp"</c>) so a single subscription string
        /// covers both metrics and traces.
        /// </summary>
        public const string ActivitySourceName = "WatsonTcp";

        #endregion

        #region Units

        /// <summary>
        /// UCUM unit for a count of messages (<c>"{message}"</c>).  Annotation units contribute no
        /// Prometheus suffix.
        /// </summary>
        public const string UnitMessage = "{message}";

        /// <summary>
        /// UCUM unit for a count of connections (<c>"{connection}"</c>).
        /// </summary>
        public const string UnitConnection = "{connection}";

        /// <summary>
        /// UCUM unit for a count of handshakes (<c>"{handshake}"</c>).
        /// </summary>
        public const string UnitHandshake = "{handshake}";

        /// <summary>
        /// UCUM unit for a count of authentications (<c>"{authentication}"</c>).
        /// </summary>
        public const string UnitAuthentication = "{authentication}";

        /// <summary>
        /// UCUM unit for a count of authorizations (<c>"{authorization}"</c>).
        /// </summary>
        public const string UnitAuthorization = "{authorization}";

        /// <summary>
        /// UCUM unit for a count of synchronous requests (<c>"{request}"</c>).
        /// </summary>
        public const string UnitRequest = "{request}";

        /// <summary>
        /// UCUM unit for a count of synchronous responses (<c>"{response}"</c>).
        /// </summary>
        public const string UnitResponse = "{response}";

        /// <summary>
        /// UCUM unit for a count of exceptions (<c>"{exception}"</c>).
        /// </summary>
        public const string UnitException = "{exception}";

        /// <summary>
        /// UCUM unit for a count of errors (<c>"{error}"</c>).
        /// </summary>
        public const string UnitError = "{error}";

        /// <summary>
        /// UCUM unit for bytes (<c>"By"</c>).  The Prometheus exporter appends a <c>_bytes</c> suffix.
        /// </summary>
        public const string UnitBytes = "By";

        /// <summary>
        /// UCUM unit for seconds (<c>"s"</c>).  The Prometheus exporter appends a <c>_seconds</c> suffix.
        /// </summary>
        public const string UnitSeconds = "s";

        #endregion

        #region Metric-Names

        /// <summary>
        /// Counter of messages written to the wire.  Unit <see cref="UnitMessage"/>.
        /// </summary>
        public const string MessagesSent = "watsontcp.messages.sent";

        /// <summary>
        /// Counter of messages read from the wire.  Unit <see cref="UnitMessage"/>.
        /// </summary>
        public const string MessagesReceived = "watsontcp.messages.received";

        /// <summary>
        /// Counter of payload bytes sent.  Unit <see cref="UnitBytes"/>.
        /// </summary>
        public const string BytesSent = "watsontcp.bytes.sent";

        /// <summary>
        /// Counter of payload bytes received.  Unit <see cref="UnitBytes"/>.
        /// </summary>
        public const string BytesReceived = "watsontcp.bytes.received";

        /// <summary>
        /// Histogram of sent message sizes.  Unit <see cref="UnitBytes"/>.
        /// </summary>
        public const string MessageSentSize = "watsontcp.message.sent.size";

        /// <summary>
        /// Histogram of received message sizes.  Unit <see cref="UnitBytes"/>.
        /// </summary>
        public const string MessageReceivedSize = "watsontcp.message.received.size";

        /// <summary>
        /// Histogram of the time taken to write a message header and payload to the transport stream.
        /// Unit <see cref="UnitSeconds"/>.
        /// </summary>
        public const string MessageSendDuration = "watsontcp.message.send.duration";

        /// <summary>
        /// Observable gauge of currently live connections.  Unit <see cref="UnitConnection"/>.
        /// </summary>
        public const string ConnectionsActive = "watsontcp.connections.active";

        /// <summary>
        /// Observable gauge of accepted-but-not-yet-admitted connections (server only).
        /// Unit <see cref="UnitConnection"/>.
        /// </summary>
        public const string ConnectionsPending = "watsontcp.connections.pending";

        /// <summary>
        /// Counter of connection admission outcomes, dimensioned by <see cref="TagOutcome"/>.
        /// Unit <see cref="UnitConnection"/>.
        /// </summary>
        public const string ConnectionsTotal = "watsontcp.connections.total";

        /// <summary>
        /// Counter of disconnections, dimensioned by <see cref="TagReason"/>.
        /// Unit <see cref="UnitConnection"/>.
        /// </summary>
        public const string DisconnectionsTotal = "watsontcp.disconnections.total";

        /// <summary>
        /// Counter of custom-handshake completions, dimensioned by <see cref="TagOutcome"/>.
        /// Unit <see cref="UnitHandshake"/>.
        /// </summary>
        public const string HandshakesTotal = "watsontcp.handshakes.total";

        /// <summary>
        /// Histogram of custom-handshake duration, dimensioned by <see cref="TagOutcome"/>.
        /// Unit <see cref="UnitSeconds"/>.
        /// </summary>
        public const string HandshakeDuration = "watsontcp.handshake.duration";

        /// <summary>
        /// Counter of preshared-key authentication results, dimensioned by <see cref="TagOutcome"/>.
        /// Unit <see cref="UnitAuthentication"/>.
        /// </summary>
        public const string AuthenticationsTotal = "watsontcp.authentications.total";

        /// <summary>
        /// Counter of connection-authorization results, dimensioned by <see cref="TagOutcome"/>.
        /// Unit <see cref="UnitAuthorization"/>.
        /// </summary>
        public const string AuthorizationsTotal = "watsontcp.authorizations.total";

        /// <summary>
        /// Counter of synchronous requests issued through SendAndWaitAsync.  Unit <see cref="UnitRequest"/>.
        /// </summary>
        public const string SyncRequestsSent = "watsontcp.sync.requests.sent";

        /// <summary>
        /// Counter of synchronous responses matched to an outstanding request.  Unit <see cref="UnitResponse"/>.
        /// </summary>
        public const string SyncResponsesReceived = "watsontcp.sync.responses.received";

        /// <summary>
        /// Histogram of synchronous round-trip duration, dimensioned by <see cref="TagOutcome"/>
        /// (<c>completed</c> or <c>timeout</c>).  Unit <see cref="UnitSeconds"/>.
        /// </summary>
        public const string SyncDuration = "watsontcp.sync.duration";

        /// <summary>
        /// Observable gauge of in-flight synchronous conversations.  Unit <see cref="UnitRequest"/>.
        /// </summary>
        public const string SyncPending = "watsontcp.sync.pending";

        /// <summary>
        /// Counter of expired synchronous requests or responses that were discarded, dimensioned by
        /// <see cref="TagKind"/> (<c>request</c> or <c>response</c>).  Unit <see cref="UnitMessage"/>.
        /// </summary>
        public const string SyncExpired = "watsontcp.sync.expired";

        /// <summary>
        /// Counter of exceptions surfaced through the ExceptionEncountered event, dimensioned by
        /// <see cref="TagExceptionType"/>.  Unit <see cref="UnitException"/>.
        /// </summary>
        public const string ExceptionsTotal = "watsontcp.exceptions.total";

        /// <summary>
        /// Counter of recovered transient accept-loop socket errors, dimensioned by
        /// <see cref="TagSocketError"/>.  Unit <see cref="UnitError"/>.
        /// </summary>
        public const string ListenerTransientErrors = "watsontcp.listener.transient_errors";

        /// <summary>
        /// Counter of unread stream-payload bytes drained after a receive handler returned.
        /// Unit <see cref="UnitBytes"/>.
        /// </summary>
        public const string StreamDrainedBytes = "watsontcp.stream.drained_bytes";

        /// <summary>
        /// Observable gauge of seconds elapsed since the client or server started.  Unit <see cref="UnitSeconds"/>.
        /// </summary>
        public const string Uptime = "watsontcp.uptime";

        #endregion

        #region Span-Names

        /// <summary>
        /// Client span covering a connection attempt through registration and handshake.
        /// </summary>
        public const string SpanConnect = "watsontcp.connect";

        /// <summary>
        /// Server span covering the lifetime of an admitted client session.
        /// </summary>
        public const string SpanSession = "watsontcp.session";

        /// <summary>
        /// Span covering a custom handshake exchange.
        /// </summary>
        public const string SpanHandshake = "watsontcp.handshake";

        /// <summary>
        /// Span covering a single message send.
        /// </summary>
        public const string SpanSend = "watsontcp.send";

        /// <summary>
        /// Span covering the processing of a single received data message.
        /// </summary>
        public const string SpanReceive = "watsontcp.receive";

        /// <summary>
        /// Span covering a synchronous request/response round trip.
        /// </summary>
        public const string SpanSync = "watsontcp.sync";

        #endregion

        #region Metric-Tag-Keys

        /// <summary>
        /// Metric tag key naming which side emitted the measurement.  Values: <c>server</c>, <c>client</c>.
        /// </summary>
        public const string TagRole = "role";

        /// <summary>
        /// Metric tag key naming the transport.  Values: <c>tcp</c>, <c>ssl</c>.
        /// </summary>
        public const string TagProtocol = "protocol";

        /// <summary>
        /// Metric tag key naming the outcome of a connection, handshake, authentication, authorization,
        /// or synchronous operation.
        /// </summary>
        public const string TagOutcome = "outcome";

        /// <summary>
        /// Metric tag key naming the reason for a disconnection.  Values mirror the
        /// <see cref="DisconnectReason"/> enum member names.
        /// </summary>
        public const string TagReason = "reason";

        /// <summary>
        /// Metric tag key naming a message classification.  Values: <c>data</c>, <c>control</c>,
        /// <c>sync_request</c>, <c>sync_response</c>.
        /// </summary>
        public const string TagMessageKind = "message.kind";

        /// <summary>
        /// Metric tag key naming an expired-synchronous discard classification.  Values: <c>request</c>,
        /// <c>response</c>.
        /// </summary>
        public const string TagKind = "kind";

        /// <summary>
        /// Metric tag key naming the short type name of an exception.
        /// </summary>
        public const string TagExceptionType = "exception.type";

        /// <summary>
        /// Metric tag key naming a <see cref="System.Net.Sockets.SocketError"/> value.
        /// </summary>
        public const string TagSocketError = "socket.error";

        #endregion

        #region Span-Tag-Keys

        /// <summary>
        /// Span tag key naming the remote server address (client spans).
        /// </summary>
        public const string TagServerAddress = "server.address";

        /// <summary>
        /// Span tag key naming the remote server port (client spans).
        /// </summary>
        public const string TagServerPort = "server.port";

        /// <summary>
        /// Span tag key naming the remote client address (server spans).
        /// </summary>
        public const string TagClientAddress = "client.address";

        /// <summary>
        /// Span tag key naming the WatsonTcp client GUID (server spans).  High cardinality; spans only.
        /// </summary>
        public const string TagClientGuid = "client.guid";

        /// <summary>
        /// Span tag key naming a synchronous conversation GUID.  High cardinality; spans only.
        /// </summary>
        public const string TagConversationGuid = "conversation.guid";

        /// <summary>
        /// Span tag key naming a message payload size in bytes.
        /// </summary>
        public const string TagMessageBytes = "message.bytes";

        /// <summary>
        /// Span tag key indicating whether a message is a synchronous request.
        /// </summary>
        public const string TagMessageSync = "message.sync";

        #endregion
    }
}
