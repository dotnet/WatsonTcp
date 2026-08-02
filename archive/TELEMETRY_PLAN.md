# WatsonTcp Telemetry & Instrumentation Plan

> **Status:** Proposed — target release **v6.4.0** (minor).
> **Author/Owner:** _unassigned_
> **Tracking:** Check the boxes as work lands. Every table row and checklist item is independently actionable.

This document is the implementation plan for adding standardized, vendor-neutral
telemetry (metrics + traces) to WatsonTcp so that [Radiant](file:///c:/code/radiant),
Prometheus, OpenTelemetry Collector, or any other consumer can observe a WatsonTcp
client or server with **zero coupling to any specific telemetry backend**.

It is written to conform to Radiant's `c:\code\radiant\INTEGRATION.md` and to the
coding standards under `c:\code\agents\requirements`.

---

## 1. Design principles (non-negotiable)

These are lifted directly from `INTEGRATION.md` ("your code emits, the application hosts")
and are the contract this plan is built on.

1. **BCL only — no Radiant dependency.** WatsonTcp emits into a `System.Diagnostics.Metrics.Meter`
   and a `System.Diagnostics.ActivitySource`. Both ship in the platform. Radiant (or any
   OpenTelemetry host) *subscribes by name*. WatsonTcp never references the `Radiant`
   host SDK, `OpenTelemetry.*`, or `prometheus-net`.
2. **The name is the public API.** The `Meter` name and `ActivitySource` name — both
   `"WatsonTcp"` — are a **stable, namespaced contract**. Treat them like public API: do
   not rename across releases. They are exposed as public constants (see §4) so consumers
   can reference them symbolically.
3. **Free when unobserved.** An unsubscribed `Counter.Add` / `Histogram.Record` is ~1–5 ns
   and allocation-free when tags are passed through a stack-allocated `TagList`. Telemetry
   is therefore always-on by default; a subscriber pays, nobody else does.
4. **Instrumentation must never break the primary path.** Every recording site is
   fire-and-forget. Recording is a synchronous, non-throwing BCL call; where any
   computation is required to build a value it is guarded so a telemetry failure can never
   propagate into send/receive/connect logic.
5. **Low-cardinality metric tags only.** Metric dimensions are values you "could list on a
   whiteboard": `role` (`server`/`client`), `protocol` (`tcp`/`ssl`), `outcome`, `reason`.
   High-cardinality identifiers — client GUID, remote `ip:port`, conversation GUID — go on
   **span tags** and **logs**, never on metric tags.
6. **OpenTelemetry semantic-convention style names & UCUM units.** Dotted, lowercase names
   (`watsontcp.messages.sent`) and UCUM units (`s`, `By`, `{message}`, `{connection}`) so
   the exporter's automatic Prometheus suffixing produces conventional series.

---

## 2. Consumer quick-start (what a telemetry consumer needs to know)

A Radiant host observes WatsonTcp with exactly two subscriptions at the composition root:

```csharp
RadiantSettings settings = new RadiantSettings("my-service");
settings.Otlp.Endpoint = "http://localhost:4317";
settings.Prometheus.Enable = true;

settings.Sources.AddMeter("WatsonTcp");            // metrics
settings.Sources.AddActivitySource("WatsonTcp");   // traces

using (RadiantHost host = RadiantHost.Start(settings))
{
    // ... run WatsonTcp client/server as usual ...
}
```

A raw OpenTelemetry host is equivalent:

```csharp
Sdk.CreateMeterProviderBuilder().AddMeter("WatsonTcp").AddOtlpExporter().Build();
Sdk.CreateTracerProviderBuilder().AddSource("WatsonTcp").AddOtlpExporter().Build();
```

Wildcard subscription (`"WatsonTcp*"`) also works if we ever split into
`WatsonTcp.Server` / `WatsonTcp.Client` sub-meters (not planned for v6.4.0 — see §11).

**Strings a consumer needs** (all published as public constants in code, see §4):

| Concept | Value |
|---|---|
| Meter name | `WatsonTcp` |
| ActivitySource name | `WatsonTcp` |
| Metric name prefix | `watsontcp.` |
| Span name prefix | `watsontcp.` |
| Tag key: role | `role` (`server` \| `client`) |
| Tag key: protocol | `protocol` (`tcp` \| `ssl`) |
| Tag key: outcome | `outcome` |
| Tag key: disconnect reason | `reason` |

---

## 3. Instrumentation surface — where telemetry is emitted in the stack

The existing hand-rolled `WatsonTcpStatistics` (bytes/messages counters on both
`WatsonTcpClient` and `WatsonTcpServer`) stays as-is for backward compatibility. The new
`Meter`-based instrumentation is added **alongside** it, at the same proven insertion
points plus the connection/handshake/auth/sync lifecycle.

```
                      ┌───────────────────────────── WatsonTcp process ─────────────────────────────┐
   TCP accept ───▶ AcceptConnections ──▶ [connections.total{outcome}] [listener.transient_errors]
                          │
                          ▼
                  StartTls / Authorize / Handshake ──▶ [authorizations.total] [handshakes.total]
                          │                             [handshake.duration] [span watsontcp.handshake]
                          ▼
                  ActivateClient ─────────────────────▶ [connections.total{outcome=accepted}]
                          │                             [connections.active gauge] [span watsontcp.session]
                          ▼
   read  ◀── DataReceiver ──────────────────────────▶ [messages.received] [bytes.received]
                          │                             [message.received.size] [span watsontcp.receive]
                          │                             [sync.responses.received] [sync.expired]
                          ▼
                  (handler)                            [stream.drained_bytes] [exceptions.total]
                          │
   write ◀── SendInternal / SendAndWaitInternal ─────▶ [messages.sent] [bytes.sent]
                          │                             [message.sent.size] [message.send.duration]
                          │                             [sync.requests.sent] [sync.duration{outcome}]
                          ▼                             [span watsontcp.send] [span watsontcp.sync]
                  DataReceiver exit ──────────────────▶ [disconnections.total{reason}]
                                                        [connections.active gauge −1]
```

---

## 4. Metric catalog (the contract)

All instruments live on `Meter("WatsonTcp")`. Names are dotted/lowercase; units are UCUM.
The right-most column is the approximate series name the OpenTelemetry Prometheus exporter
produces (it lowercases, replaces `.`→`_`, appends unit and `_total` suffixes; `{…}`
annotation units contribute no suffix).

| # | Metric name | Kind | Unit | Base tags | Extra tags | Description | Prometheus series (approx.) | Done |
|---|-------------|------|------|-----------|-----------|-------------|------------------------------|:----:|
| M01 | `watsontcp.messages.sent` | Counter\<long> | `{message}` | role, protocol | message.kind | Messages written to the wire | `watsontcp_messages_sent_total` | ☐ |
| M02 | `watsontcp.messages.received` | Counter\<long> | `{message}` | role, protocol | message.kind | Messages read off the wire | `watsontcp_messages_received_total` | ☐ |
| M03 | `watsontcp.bytes.sent` | Counter\<long> | `By` | role, protocol | | Payload bytes sent (content length) | `watsontcp_bytes_sent_bytes_total` | ☐ |
| M04 | `watsontcp.bytes.received` | Counter\<long> | `By` | role, protocol | | Payload bytes received | `watsontcp_bytes_received_bytes_total` | ☐ |
| M05 | `watsontcp.message.sent.size` | Histogram\<long> | `By` | role, protocol | | Distribution of sent message sizes | `watsontcp_message_sent_size_bytes` | ☐ |
| M06 | `watsontcp.message.received.size` | Histogram\<long> | `By` | role, protocol | | Distribution of received message sizes | `watsontcp_message_received_size_bytes` | ☐ |
| M07 | `watsontcp.message.send.duration` | Histogram\<double> | `s` | role, protocol | | Time to write header+payload to the stream (buckets: `LatencyBuckets.Network`) | `watsontcp_message_send_duration_seconds` | ☐ |
| M08 | `watsontcp.connections.active` | ObservableGauge\<long> | `{connection}` | role, protocol | | Current live connections (server: `Connections`; client: `0/1`) | `watsontcp_connections_active` | ☐ |
| M09 | `watsontcp.connections.pending` | ObservableGauge\<long> | `{connection}` | role, protocol | | Accepted-but-not-yet-admitted clients (server only) | `watsontcp_connections_pending` | ☐ |
| M10 | `watsontcp.connections.total` | Counter\<long> | `{connection}` | role, protocol | outcome | Connection admission outcomes | `watsontcp_connections_total` | ☐ |
| M11 | `watsontcp.disconnections.total` | Counter\<long> | `{connection}` | role, protocol | reason | Disconnections by `DisconnectReason` | `watsontcp_disconnections_total` | ☐ |
| M12 | `watsontcp.handshakes.total` | Counter\<long> | `{handshake}` | role, protocol | outcome | Custom-handshake completions | `watsontcp_handshakes_total` | ☐ |
| M13 | `watsontcp.handshake.duration` | Histogram\<double> | `s` | role, protocol | outcome | Handshake begin→resolve time (buckets: `Network`) | `watsontcp_handshake_duration_seconds` | ☐ |
| M14 | `watsontcp.authentications.total` | Counter\<long> | `{authentication}` | role, protocol | outcome | Preshared-key auth results | `watsontcp_authentications_total` | ☐ |
| M15 | `watsontcp.authorizations.total` | Counter\<long> | `{authorization}` | role, protocol | outcome | Server `AuthorizeConnection` results | `watsontcp_authorizations_total` | ☐ |
| M16 | `watsontcp.sync.requests.sent` | Counter\<long> | `{request}` | role, protocol | | `SendAndWaitAsync` requests issued | `watsontcp_sync_requests_sent_total` | ☐ |
| M17 | `watsontcp.sync.responses.received` | Counter\<long> | `{response}` | role, protocol | | Sync responses matched to a request | `watsontcp_sync_responses_received_total` | ☐ |
| M18 | `watsontcp.sync.duration` | Histogram\<double> | `s` | role, protocol | outcome | Sync round-trip time, `outcome=completed\|timeout` (buckets: `Network`) | `watsontcp_sync_duration_seconds` | ☐ |
| M19 | `watsontcp.sync.pending` | ObservableGauge\<long> | `{request}` | role, protocol | | In-flight sync conversations (`_SyncRequests.Count`) | `watsontcp_sync_pending` | ☐ |
| M20 | `watsontcp.sync.expired` | Counter\<long> | `{message}` | role, protocol | kind | Expired sync request/response discarded, `kind=request\|response` | `watsontcp_sync_expired_total` | ☐ |
| M21 | `watsontcp.exceptions.total` | Counter\<long> | `{exception}` | role, protocol | exception.type | Exceptions surfaced through `ExceptionEncountered` | `watsontcp_exceptions_total` | ☐ |
| M22 | `watsontcp.listener.transient_errors` | Counter\<long> | `{error}` | role, protocol | socket.error | Recovered transient accept-loop socket errors | `watsontcp_listener_transient_errors_total` | ☐ |
| M23 | `watsontcp.stream.drained_bytes` | Counter\<long> | `By` | role, protocol | | Unread stream-payload bytes drained after a handler | `watsontcp_stream_drained_bytes_bytes_total` | ☐ |
| M24 | `watsontcp.uptime` | ObservableGauge\<double> | `s` | role, protocol | | Seconds since the client/server started | `watsontcp_uptime_seconds` | ☐ |

### Tag value dictionaries (low-cardinality — the whiteboard test)

| Tag key | Allowed values | Notes |
|---|---|---|
| `role` | `server`, `client` | Which side emitted. |
| `protocol` | `tcp`, `ssl` | Derived from `Mode`. Value `ssl` used for the TLS transport. |
| `outcome` (connections) | `accepted`, `connected`, `rejected_maxconnections`, `rejected_notpermitted`, `rejected_blocked`, `rejected_authorization`, `failed` | `connected` used on client side. |
| `outcome` (handshake/auth/authorization) | `success`, `failure`, `timeout`, `canceled` | |
| `outcome` (sync) | `completed`, `timeout` | |
| `reason` | `Normal`, `Removed`, `Timeout`, `Shutdown`, `AuthFailure`, `ConnectionRejected`, `HandshakeFailure` | Mirrors the public `DisconnectReason` enum member values. |
| `message.kind` | `data`, `control`, `sync_request`, `sync_response` | Optional dimension on M01/M02; `control` = register/status/auth/handshake frames. |
| `kind` (sync.expired) | `request`, `response` | |
| `exception.type` | short type name (`IOException`, `SocketException`, `TaskCanceledException`, `ObjectDisposedException`, `TimeoutException`, …) | Bounded set — the framework only ever raises a handful. |
| `socket.error` | `SocketError` enum name (`ConnectionReset`, `ConnectionAborted`, …) | Bounded. |

> **Public API note:** These constants are published so a consumer can build dashboards
> without hard-coding strings. They also *are* the deliverable "namespaces/strings a
> consumer needs."

---

## 5. Distributed tracing catalog (ActivitySource "WatsonTcp")

Spans carry the high-cardinality identifiers that metrics deliberately omit. All spans are
created via `ActivitySource("WatsonTcp")`; when no tracer subscribes, `StartActivity`
returns `null` and the `using` wrapper is a no-op.

| # | Span name | Kind | Started at | High-cardinality tags | Done |
|---|-----------|------|-----------|------------------------|:----:|
| T01 | `watsontcp.connect` | Client | `WatsonTcpClient.ConnectCoreAsync` (whole connect+register+handshake) | `server.address`, `server.port`, `protocol`, `outcome` | ☐ |
| T02 | `watsontcp.session` | Server | `WatsonTcpServer.ActivateClientAsync`→ends in `DataReceiver` exit | `client.address`, `client.port`, `client.guid`, `protocol`, `reason` | ☐ |
| T03 | `watsontcp.handshake` | Internal | `StartHandshakePhaseAsync` / `StartClientHandshake` | `role`, `outcome` | ☐ |
| T04 | `watsontcp.send` | Producer | `SendInternalAsync` | `message.bytes`, `message.sync`, `client.guid` (server) | ☐ |
| T05 | `watsontcp.receive` | Consumer | `DataReceiver` per data message | `message.bytes`, `client.guid` (server) | ☐ |
| T06 | `watsontcp.sync` | Client | `SendAndWaitInternalAsync` | `conversation.guid`, `message.bytes`, `outcome` | ☐ |

Span tag keys follow OTel conventions (`server.address`, `server.port`, `client.address`).
`client.guid` / `conversation.guid` are exactly the values banned from metric tags.

> **Scope decision for v6.4.0:** Metrics (M01–M24) are the committed deliverable. Tracing
> (T01–T06) is included in this plan and *may* be delivered in the same release, but if
> schedule pressure appears, tracing may be split to v6.5.0 without changing the metric
> contract. Mark the decision here: ☐ tracing in 6.4.0  ☐ tracing deferred.

---

## 6. Code design

### 6.1 New files (one type per file, per requirements)

| File | Type | Visibility | Purpose | Done |
|---|---|---|---|:----:|
| `src/WatsonTcp/WatsonTcpMetrics.cs` | `static class WatsonTcpMetrics` | **public** | Public constants: `MeterName`, `ActivitySourceName`, every metric name, every tag key. The consumer-facing string contract. | ☐ |
| `src/WatsonTcp/WatsonTcpInstrumentation.cs` | `sealed class WatsonTcpInstrumentation : IDisposable` | internal | Owns one `Meter` + one `ActivitySource` and every instrument; exposes typed recording helpers and gauge callbacks; disposed with its owner. | ☐ |

`WatsonTcpInstrumentation` is **instance-scoped** (one per `WatsonTcpClient` /
`WatsonTcpServer`), not static. Rationale:

- ObservableGauges (M08/M09/M19/M24) read live instance state via callbacks captured in the
  constructor — the correct BCL pattern for gauges and impossible to get right from a shared
  static Meter across multiple instances.
- Deterministic lifecycle: the `Meter`/`ActivitySource` are disposed in the owner's
  `Dispose(bool)`, so no leaked subscriptions.
- Multiple instances may create a `Meter` with the same name `"WatsonTcp"`; OpenTelemetry
  aggregates them, so the *contract* (the name) is still singular and stable.

Sketch (illustrative — not final code; must be fleshed out to full compliance):

```csharp
namespace WatsonTcp
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    internal sealed class WatsonTcpInstrumentation : IDisposable
    {
        #region Private-Members

        private readonly Meter _Meter;
        private readonly ActivitySource _ActivitySource;
        private readonly string _Role;      // "server" | "client"
        private readonly string _Protocol;  // "tcp" | "ssl"

        private readonly Counter<long> _MessagesSent;
        private readonly Counter<long> _BytesSent;
        // ... remaining instruments ...

        #endregion

        #region Constructors-and-Factories

        internal WatsonTcpInstrumentation(
            string role,
            string protocol,
            Func<long> activeConnections,
            Func<long> pendingConnections,
            Func<long> pendingSyncRequests,
            Func<double> uptimeSeconds)
        {
            _Role = role ?? throw new ArgumentNullException(nameof(role));
            _Protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));

            _Meter = new Meter(WatsonTcpMetrics.MeterName);
            _ActivitySource = new ActivitySource(WatsonTcpMetrics.ActivitySourceName);

            _MessagesSent = _Meter.CreateCounter<long>(
                WatsonTcpMetrics.MessagesSent, WatsonTcpMetrics.UnitMessage, "Messages written to the wire.");
            // ... create the rest ...

            _Meter.CreateObservableGauge(
                WatsonTcpMetrics.ConnectionsActive,
                () => new Measurement<long>(activeConnections(), BaseTags()),
                WatsonTcpMetrics.UnitConnection);
            // ... pending / sync.pending / uptime gauges ...
        }

        #endregion

        #region Internal-Methods

        internal void MessageSent(long bytes)
        {
            TagList tags = BaseTags();
            _MessagesSent.Add(1, tags);
            _BytesSent.Add(bytes, tags);
            _MessageSentSize.Record(bytes, tags);
        }

        // MessageReceived, ConnectionOutcome(string), Disconnection(DisconnectReason),
        // Handshake(string outcome, double seconds), Authentication(string outcome),
        // Authorization(string outcome), SyncRequestSent(), SyncCompleted(double, string outcome),
        // SyncExpired(string kind), Exception(Exception), TransientAcceptError(SocketError),
        // StreamDrained(long) ...

        internal Activity StartSend(long bytes) =>
            _ActivitySource.StartActivity(WatsonTcpMetrics.SpanSend, ActivityKind.Producer);

        #endregion

        #region Private-Methods

        private TagList BaseTags()
        {
            TagList tags = new TagList();
            tags.Add(WatsonTcpMetrics.TagRole, _Role);
            tags.Add(WatsonTcpMetrics.TagProtocol, _Protocol);
            return tags;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _ActivitySource.Dispose();
            _Meter.Dispose();
        }

        #endregion
    }
}
```

### 6.2 Wiring into `WatsonTcpServer`

| Insertion point (existing code) | Call to add | Metric/Span | Done |
|---|---|---|:----:|
| `Start()` after `_Statistics = new WatsonTcpStatistics();` | construct `_Instrumentation` (role `server`, protocol from `_Mode`, gauge callbacks → `Connections`, `PendingConnections`, `_SyncRequests.Count`, `Statistics.UpTime`) | M08/M09/M19/M24 | ☐ |
| `AcceptConnections` maxconn reject (`~L784`) | `ConnectionOutcome("rejected_maxconnections")` | M10 | ☐ |
| `AcceptConnections` not-permitted (`~L794`) | `ConnectionOutcome("rejected_notpermitted")` | M10 | ☐ |
| `AcceptConnections` blocked (`~L801`) | `ConnectionOutcome("rejected_blocked")` | M10 | ☐ |
| `AcceptConnections` transient catch (`~L773`) | `TransientAcceptError(e.SocketErrorCode)` | M22 | ☐ |
| `AuthorizePendingClientAsync` allow/reject/timeout | `Authorization(outcome)`; reject → also `ConnectionOutcome("rejected_authorization")` | M15/M10 | ☐ |
| `RunHandshakePhaseAsync` success (`~L1060`) / fail (`~L1068`) | `Handshake("success"/"failure"/"timeout", seconds)` | M12/M13, T03 | ☐ |
| `ActivateClientAsync` (`~L1088`) | `ConnectionOutcome("accepted")`; start `watsontcp.session` span stored on `ClientMetadata` | M10, T02 | ☐ |
| `DataReceiver` auth accepted (`~L1278`) / declined (`~L1287`,`~L1299`) | `Authentication("success"/"failure")` | M14 | ☐ |
| `DataReceiver` sync response matched (`~L1428`) | `SyncResponseReceived()` | M17 | ☐ |
| `DataReceiver` expired sync req (`~L1412`) / resp (`~L1437`) | `SyncExpired("request"/"response")` | M20 | ☐ |
| `DataReceiver` received accounting (`~L1463-1464`) | `MessageReceived(msg.ContentLength, kind)`; wrap handler in `watsontcp.receive` span | M02/M04/M06, T05 | ☐ |
| `DataReceiver` exit (`~L1513-1517`) | `Disconnection(reason)`; end session span | M11, T02 | ☐ |
| `SendInternalAsync` success (`~L1558-1559`) | `MessageSent(contentLength)`; time write for M07; `watsontcp.send` span | M01/M03/M05/M07, T04 | ☐ |
| `SendAndWaitInternalAsync` send (`~L1609-1610`) | `MessageSent(...)` + `SyncRequestSent()`; time round-trip → `SyncCompleted(seconds, outcome)`; `watsontcp.sync` span | M01/M03/M16/M18, T06 | ☐ |
| `HandleStreamPayloadAsync` drain finally | `StreamDrained(watsonStream.RemainingBytes)` | M23 | ☐ |
| every `_Events.HandleExceptionEncountered(...)` site | route through a new `private void HandleException(Exception e)` that calls `_Instrumentation?.Exception(e)` then raises the event | M21 | ☐ |
| `Dispose(bool disposing)` | `_Instrumentation?.Dispose(); _Instrumentation = null;` | lifecycle | ☐ |

### 6.3 Wiring into `WatsonTcpClient`

| Insertion point (existing code) | Call to add | Metric/Span | Done |
|---|---|---|:----:|
| `ConnectCoreAsync` after `_Statistics = new WatsonTcpStatistics();` (`~L552`) | construct `_Instrumentation` (role `client`, protocol from `_Mode`, gauges → `Connected?1:0`, `0`, `_SyncRequests.Count`, `Statistics.UpTime`); start `watsontcp.connect` span | M08/M19/M24, T01 | ☐ |
| `MarkConnected` (`~L739`) | `ConnectionOutcome("connected")` | M10 | ☐ |
| `RunClientHandshakeAsync` / `HandshakeSuccess` (`~L1196`) / failure | `Handshake(outcome, seconds)` | M12/M13, T03 | ☐ |
| `DataReceiver` `AuthSuccess` (`~L1110`) / `AuthFailure` (`~L1125`) | `Authentication("success"/"failure")` | M14 | ☐ |
| `DataReceiver` sync response matched (`~L1283`) | `SyncResponseReceived()` | M17 | ☐ |
| `DataReceiver` expired sync req (`~L1267`) / resp (`~L1292`) | `SyncExpired("request"/"response")` | M20 | ☐ |
| `DataReceiver` received accounting (`~L1320-1321`) | `MessageReceived(msg.ContentLength, kind)`; `watsontcp.receive` span | M02/M04/M06, T05 | ☐ |
| `DataReceiver` exit `HandleServerDisconnected` (`~L1392`) | `Disconnection(reason)`; end connect span | M11, T01 | ☐ |
| `SendInternalAsync` success (`~L1435-1436`) | `MessageSent(contentLength)`; time write; `watsontcp.send` span | M01/M03/M05/M07, T04 | ☐ |
| `SendAndWaitInternalAsync` send (`~L1501-1502`) | `MessageSent(...)` + `SyncRequestSent()`; time round-trip → `SyncCompleted(...)`; `watsontcp.sync` span | M01/M03/M16/M18, T06 | ☐ |
| `HandleStreamPayloadAsync` drain finally | `StreamDrained(...)` | M23 | ☐ |
| every `_Events.HandleExceptionEncountered(...)` site | route through `private void HandleException(Exception e)` | M21 | ☐ |
| `Dispose(bool disposing)` | `_Instrumentation?.Dispose(); _Instrumentation = null;` | lifecycle | ☐ |

### 6.4 Histogram buckets

Match Radiant presets (all in seconds, to line up with unit `s`):

| Metric | Preset | Boundaries |
|---|---|---|
| M07 `message.send.duration` | `Network` | 0.01 … 120.0 |
| M13 `handshake.duration` | `Network` | 0.01 … 120.0 |
| M18 `sync.duration` | `Network` | 0.01 … 120.0 |

WatsonTcp does not reference Radiant, so bucket boundaries are declared as an internal
`static readonly double[]` in `WatsonTcpInstrumentation` (documented as mirroring
`LatencyBuckets.Network`). The host chooses whether to apply them via an OTel `View`; the
producer only records raw values. Size histograms (M05/M06, unit `By`) use the default
OTel bucketing unless the host overrides.

---

## 7. Configuration & opt-out

Per requirements ("avoid constants a developer may want to change; use a public member with
a backing field and a sensible default"), expose an explicit switch on each settings class.
Because unobserved recording is already near-free, the default is **on**.

| Setting | Type | Default | File | Done |
|---|---|---|---|:----:|
| `WatsonTcpClientSettings.EnableMetrics` | `bool` | `true` | `WatsonTcpClientSettings.cs` | ☐ |
| `WatsonTcpClientSettings.EnableTracing` | `bool` | `true` | `WatsonTcpClientSettings.cs` | ☐ |
| `WatsonTcpServerSettings.EnableMetrics` | `bool` | `true` | `WatsonTcpServerSettings.cs` | ☐ |
| `WatsonTcpServerSettings.EnableTracing` | `bool` | `true` | `WatsonTcpServerSettings.cs` | ☐ |

When `EnableMetrics == false`, the owner passes a `null` instrumentation object (all call
sites use `_Instrumentation?.…`), so there is literally no `Meter` and zero cost. `EnableTracing`
gates only span creation. Both carry full XML docs stating default/meaning.

---

## 8. Project / dependency changes

`System.Diagnostics.Metrics` (`Meter`, `Counter`, `Histogram`, `UpDownCounter`,
`ObservableGauge`, `TagList`) and `System.Diagnostics.ActivitySource` are **in-box** on
`net8.0`/`net10.0` but ship via the `System.Diagnostics.DiagnosticSource` package for
`netstandard2.0`, `netstandard2.1`, `net462`, and `net48`.

`src/WatsonTcp/WatsonTcp.csproj` changes:

```xml
<!-- Down-level TFMs need the DiagnosticSource package for the Meter/ActivitySource APIs.
     net8.0/net10.0 already include them in the shared framework. -->
<ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'
                   Or '$(TargetFramework)' == 'netstandard2.1'
                   Or '$(TargetFramework)' == 'net462'
                   Or '$(TargetFramework)' == 'net48'">
  <PackageReference Include="System.Diagnostics.DiagnosticSource" Version="8.0.1" />
</ItemGroup>
```

- [ ] Add the conditional `PackageReference` above (version pinned to `8.0.1`, matching
      Radiant.SemConv's pin, so a Radiant host unifies cleanly).
- [ ] Confirm `TagList` and `Meter.CreateObservableGauge(...)` compile on **all six** TFMs
      (they are available in DiagnosticSource ≥ 6.0; 8.0.1 covers net462).
- [ ] **Do NOT** add `OpenTelemetry`, `OpenTelemetry.Exporter.*`, or `Radiant`.
- [ ] *(Optional, defer)* Evaluate referencing `Radiant.SemConv` (`System.Diagnostics.DiagnosticSource`-only,
      netstandard2.0-safe) for shared `SemConv.Attributes.Protocol` / `Convention` constants.
      Default recommendation: **do not** take even this dependency for v6.4.0 — keep WatsonTcp
      self-contained and publish our own constants in `WatsonTcpMetrics`.

> **Compliance divergence (explicit & justified, per requirements):** The requirements'
> `BACKEND_TEST_ARCHITECTURE.md` prescribes `net8.0;net10.0` + `<Nullable>enable</Nullable>`.
> WatsonTcp is a pre-existing, widely-consumed multi-target library
> (`netstandard2.0;netstandard2.1;net462;net48;net8.0;net10.0`) without project-wide
> nullable enablement. Telemetry code follows every applicable **style** rule from
> `CODE_STYLE.md` / `BACKEND_ARCHITECTURE.md` (usings inside namespace, `_PascalCase`
> privates, XML docs on public members, `using(){}` blocks, one type per file, no `var`,
> no tuples, `Math.Clamp` on numeric settings, guard clauses, `Interlocked`/thread-safety
> notes) but **retains the library's existing TFM matrix and nullable posture**. This is a
> deliberate divergence recorded here rather than a regression of the shipping package's
> compatibility surface.

---

## 9. Code-compliance checklist (`c:\code\agents\requirements`)

Apply to `WatsonTcpMetrics.cs`, `WatsonTcpInstrumentation.cs`, and all edits.

- [ ] `namespace WatsonTcp` first; `using` directives **inside** the namespace block.
- [ ] System/Microsoft usings alphabetized first, then others alphabetized.
- [ ] All public types/members/consts have XML `///` docs; **no** docs on private members.
- [ ] Public metric-name/tag constants documented with their meaning and stable-contract note.
- [ ] Private fields `_PascalCase` (`_Meter`, `_MessagesSent`, `_Instrumentation`).
- [ ] No `var`; no tuples.
- [ ] One class/enum per file.
- [ ] `IDisposable` implemented on `WatsonTcpInstrumentation`; owners call `.Dispose()` inside
      their existing `protected virtual void Dispose(bool disposing)`.
- [ ] `using (…) { }` block form for any local disposables (e.g. `Activity`).
- [ ] `Interlocked` / thread-safety documented; counters are already thread-safe via `Meter`.
- [ ] `Math.Clamp` + documented default/min/max for any numeric setting introduced.
- [ ] Guard clauses (`ArgumentNullException`) on constructor/method inputs; nullable
      call-site guards (`_Instrumentation?.`) everywhere.
- [ ] No `Console.*` anywhere in library code.
- [ ] Recording sites cannot throw into send/receive/connect paths (fire-and-forget).
- [ ] Builds warning-free on all six TFMs (`EnableNETAnalyzers` + `latest-recommended` already on).

---

## 10. Testing plan

Follow the Touchstone descriptor pattern already in `src/Test.Shared` (net8.0;net10.0),
executed via the console runner, xUnit, and NUnit projects. No console output in shared
test code; assert by throwing.

- [ ] **In-memory metrics test** — attach an OTel `MeterProvider` with
      `.AddMeter("WatsonTcp").AddInMemoryExporter(items)` (test-only dependency, not in the
      library), run a client⇄server exchange, assert M01–M04 increment with the expected
      `role`/`protocol` tags and that byte sums match payload sizes.
- [ ] **Connection lifecycle** — assert `connections.total{outcome=accepted}`,
      `connections.active` gauge rises then returns to 0, and `disconnections.total{reason=…}`
      records the correct `DisconnectReason` for normal, removed, timeout, and shutdown.
- [ ] **Sync round-trip** — assert `sync.requests.sent`, `sync.responses.received`,
      `sync.duration{outcome=completed}`, and a forced timeout yields `outcome=timeout`.
- [ ] **Handshake / auth / authorization** — success and failure paths hit M12–M15 with
      correct `outcome`.
- [ ] **Rejections** — permitted/blocked/max-connections/authorization-reject each hit
      `connections.total` with the right `outcome`.
- [ ] **Cardinality guard** — a test asserting no metric carries a GUID/endpoint tag key
      (only `role`, `protocol`, `outcome`, `reason`, `message.kind`, `kind`,
      `exception.type`, `socket.error`).
- [ ] **Opt-out** — with `EnableMetrics = false`, no `WatsonTcp` meter is created (in-memory
      exporter sees nothing).
- [ ] **Tracing** *(if delivered in 6.4.0)* — `.AddSource("WatsonTcp").AddInMemoryExporter(...)`,
      assert T04/T05 spans exist with `message.bytes` and, server-side, `client.guid`.
- [ ] All test projects compile and pass on `net8.0` and `net10.0` (exit code 0).

---

## 11. Documentation deliverables

- [ ] **README.md** — new `## New in v6.4.0` section (above the current
      `## New in v6.3.2`) describing telemetry, the `Meter`/`ActivitySource` name
      `WatsonTcp`, and the two-line Radiant/OTel subscription snippet from §2.
- [ ] **README.md** — extend the existing `## Version History` / observability area with a
      short "Metrics & Tracing" subsection linking to this file and the metric-name table.
- [ ] **TELEMETRY.md** (this file) — keep the metric catalog (§4) authoritative; consumers
      cite it for dashboard building.
- [ ] **ARCHITECTURE.md** — add a "Telemetry" subsection describing `WatsonTcpInstrumentation`
      and its insertion points; verify no stale version/feature references.
- [ ] **CLAUDE.md** — update the NuGet-version line (currently reads "currently 6.0.11",
      which is already stale) to `6.4.0`, and note the new telemetry files under
      "Key Components".
- [ ] **FRAMING.md** — no change expected (framing bytes are unaffected); confirm.

---

## 12. Version bump — v6.3.2 → v6.4.0 (minor)

A minor bump is correct: purely additive public surface (`WatsonTcpMetrics`, four new
settings flags), no breaking changes, new dependency only on down-level TFMs.

Run `grep -rn "6\.3\.2"` before tagging to catch anything this list misses.

| Artifact | Change | Done |
|---|---|:----:|
| `src/WatsonTcp/WatsonTcp.csproj` → `<Version>` | `6.3.2` → `6.4.0` | ☐ |
| `src/WatsonTcp/WatsonTcp.csproj` → `<PackageReleaseNotes>` | Replace with telemetry summary (Meter/ActivitySource `WatsonTcp`, OTel/Prometheus/Radiant-ready) | ☐ |
| `src/WatsonTcp/WatsonTcp.csproj` → `<Copyright>` | Confirm year (`(c)2025`) still correct at release | ☐ |
| `CHANGELOG.md` | Move `v6.3.2` block to "Previous Version"; add `## Current Version` `v6.4.0` with a "Telemetry & Observability" section | ☐ |
| `README.md` | Add `## New in v6.4.0` (see §11); NuGet badges auto-update | ☐ |
| `ARCHITECTURE.md` | Telemetry subsection + version/feature sync | ☐ |
| `CLAUDE.md` | Correct the stale `6.0.11` version line → `6.4.0`; list new files | ☐ |
| `benchmarks/README.md` | Verify no hard-coded version drift | ☐ |
| Any `Test.*` referencing WatsonTcp by version | None expected (ProjectReference); confirm | ☐ |

---

## 13. Delivery sequence (suggested order)

1. ☐ Add `WatsonTcpMetrics.cs` (public constants) — unblocks everything and is the consumer contract.
2. ☐ Add `WatsonTcpInstrumentation.cs` (Meter/instruments/gauges/helpers, `IDisposable`).
3. ☐ csproj: conditional `System.Diagnostics.DiagnosticSource` reference; verify 6-TFM build.
4. ☐ Add the four `Enable*` settings flags.
5. ☐ Wire `WatsonTcpServer` (§6.2), including the `HandleException` funnel refactor.
6. ☐ Wire `WatsonTcpClient` (§6.3).
7. ☐ Add tracing spans (T01–T06) *(or defer per §5)*.
8. ☐ Add Touchstone tests (§10).
9. ☐ Documentation (§11) + version bump (§12).
10. ☐ Full multi-TFM build, warning-free; run all test runners (exit 0).

---

## 14. Sign-off

| Gate | Owner | Date | Done |
|---|---|---|:----:|
| Metric contract (§4) reviewed & frozen | | | ☐ |
| Code compliance (§9) verified | | | ☐ |
| Tests green on net8.0 + net10.0 (§10) | | | ☐ |
| Docs + version artifacts updated (§11–§12) | | | ☐ |
| Validated end-to-end against a Radiant host (`AddMeter("WatsonTcp")`) | | | ☐ |
```
