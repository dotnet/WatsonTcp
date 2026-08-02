# WatsonTcp Telemetry

WatsonTcp emits **metrics** and **distributed-tracing spans** using only the .NET base
class library (`System.Diagnostics.Metrics` and `System.Diagnostics.ActivitySource`). Any
host that understands these APIs — [Radiant](https://github.com/jchristn), the OpenTelemetry
SDK, Prometheus, Grafana, Jaeger, or your own listener — can observe a WatsonTcp client or
server. **WatsonTcp takes no dependency on any telemetry backend.** Your code emits; the
host subscribes by name.

- **Meter name:** `WatsonTcp`
- **ActivitySource name:** `WatsonTcp`

Both names are stable across releases and are exposed as public constants on
`WatsonTcp.WatsonTcpMetrics` (`WatsonTcpMetrics.MeterName`, `WatsonTcpMetrics.ActivitySourceName`).

Telemetry is **on by default** and is a near-free no-op (roughly 1–5 ns, zero allocation)
when nobody is listening, so there is nothing to turn on to start emitting — you only wire up
a consumer.

---

## 1. Quick start

### Consume with Radiant

At your application's composition root, subscribe Radiant to the two source names:

```csharp
using Radiant;
using WatsonTcp;

RadiantSettings settings = new RadiantSettings("my-service");
settings.Otlp.Endpoint = "http://localhost:4317";
settings.Prometheus.Enable = true;
settings.Prometheus.Port = 9464;

settings.Sources.AddMeter(WatsonTcpMetrics.MeterName);            // "WatsonTcp"
settings.Sources.AddActivitySource(WatsonTcpMetrics.MeterName);   // "WatsonTcp"

using (RadiantHost host = RadiantHost.Start(settings))
{
    // ... run your WatsonTcpServer / WatsonTcpClient as usual ...
}
```

That is the entire integration. WatsonTcp itself needs no changes.

### Consume with the OpenTelemetry SDK

```csharp
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using MeterProvider metrics = Sdk.CreateMeterProviderBuilder()
    .AddMeter("WatsonTcp")
    .AddPrometheusHttpListener()      // or .AddOtlpExporter()
    .Build();

using TracerProvider traces = Sdk.CreateTracerProviderBuilder()
    .AddSource("WatsonTcp")
    .AddOtlpExporter()
    .Build();
```

Wildcard subscription (`AddMeter("WatsonTcp*")`) also works, should future versions add
sub-meters.

### Consume with only the base class library

No SDK is required to read the numbers. A `MeterListener` (metrics) or `ActivityListener`
(spans) subscribes directly:

```csharp
using System.Diagnostics.Metrics;

MeterListener listener = new MeterListener();
listener.InstrumentPublished = (instrument, l) =>
{
    if (instrument.Meter.Name == "WatsonTcp") l.EnableMeasurementEvents(instrument);
};
listener.SetMeasurementEventCallback<long>((inst, value, tags, state) =>
    Console.WriteLine($"{inst.Name} += {value}"));
listener.Start();
```

---

## 2. Turning telemetry off

Because unobserved recording is essentially free, telemetry is enabled by default. You can
disable it per instance:

```csharp
server.Settings.EnableMetrics = false;   // no Meter is created; zero overhead
server.Settings.EnableTracing = false;   // no spans are created

client.Settings.EnableMetrics = false;
client.Settings.EnableTracing = false;
```

When `EnableMetrics` is `false`, WatsonTcp does not create a `Meter` at all, so no
instruments are ever published. `EnableTracing` gates span creation independently.

---

## 3. Metric catalog

All metrics are recorded on the `Meter` named `WatsonTcp`. Names are dotted and lowercase;
units are UCUM. The right-most column shows the approximate series name the OpenTelemetry
Prometheus exporter produces (it lowercases, replaces `.` with `_`, and appends unit and
`_total` suffixes; `{…}` annotation units add no suffix).

| Metric name | Instrument | Unit | Tags | Meaning | Prometheus series |
|-------------|-----------|------|------|---------|-------------------|
| `watsontcp.messages.sent` | Counter | `{message}` | role, protocol, message.kind | Messages written to the wire | `watsontcp_messages_sent_total` |
| `watsontcp.messages.received` | Counter | `{message}` | role, protocol, message.kind | Messages read from the wire | `watsontcp_messages_received_total` |
| `watsontcp.bytes.sent` | Counter | `By` | role, protocol | Payload bytes sent | `watsontcp_bytes_sent_bytes_total` |
| `watsontcp.bytes.received` | Counter | `By` | role, protocol | Payload bytes received | `watsontcp_bytes_received_bytes_total` |
| `watsontcp.message.sent.size` | Histogram | `By` | role, protocol | Distribution of sent message sizes | `watsontcp_message_sent_size_bytes` |
| `watsontcp.message.received.size` | Histogram | `By` | role, protocol | Distribution of received message sizes | `watsontcp_message_received_size_bytes` |
| `watsontcp.message.send.duration` | Histogram | `s` | role, protocol | Time to write a message to the stream | `watsontcp_message_send_duration_seconds` |
| `watsontcp.connections.active` | ObservableGauge | `{connection}` | role, protocol | Current live connections | `watsontcp_connections_active` |
| `watsontcp.connections.pending` | ObservableGauge | `{connection}` | role, protocol | Accepted-but-not-yet-admitted clients (server) | `watsontcp_connections_pending` |
| `watsontcp.connections.total` | Counter | `{connection}` | role, protocol, outcome | Connection admission outcomes | `watsontcp_connections_total` |
| `watsontcp.disconnections.total` | Counter | `{connection}` | role, protocol, reason | Disconnections by reason | `watsontcp_disconnections_total` |
| `watsontcp.handshakes.total` | Counter | `{handshake}` | role, protocol, outcome | Custom-handshake completions | `watsontcp_handshakes_total` |
| `watsontcp.handshake.duration` | Histogram | `s` | role, protocol, outcome | Handshake duration | `watsontcp_handshake_duration_seconds` |
| `watsontcp.authentications.total` | Counter | `{authentication}` | role, protocol, outcome | Preshared-key auth results | `watsontcp_authentications_total` |
| `watsontcp.authorizations.total` | Counter | `{authorization}` | role, protocol, outcome | Connection-authorization results | `watsontcp_authorizations_total` |
| `watsontcp.sync.requests.sent` | Counter | `{request}` | role, protocol | `SendAndWaitAsync` requests issued | `watsontcp_sync_requests_sent_total` |
| `watsontcp.sync.responses.received` | Counter | `{response}` | role, protocol | Sync responses matched to a request | `watsontcp_sync_responses_received_total` |
| `watsontcp.sync.duration` | Histogram | `s` | role, protocol, outcome | Sync round-trip time | `watsontcp_sync_duration_seconds` |
| `watsontcp.sync.pending` | ObservableGauge | `{request}` | role, protocol | In-flight synchronous conversations | `watsontcp_sync_pending` |
| `watsontcp.sync.expired` | Counter | `{message}` | role, protocol, kind | Expired sync requests/responses discarded | `watsontcp_sync_expired_total` |
| `watsontcp.exceptions.total` | Counter | `{exception}` | role, protocol, exception.type | Exceptions surfaced via `ExceptionEncountered` | `watsontcp_exceptions_total` |
| `watsontcp.listener.transient_errors` | Counter | `{error}` | role, protocol, socket.error | Recovered transient accept-loop socket errors | `watsontcp_listener_transient_errors_total` |
| `watsontcp.stream.drained_bytes` | Counter | `By` | role, protocol | Unread stream bytes drained after a handler | `watsontcp_stream_drained_bytes_bytes_total` |
| `watsontcp.uptime` | ObservableGauge | `s` | role, protocol | Seconds since the client/server started | `watsontcp_uptime_seconds` |

### Tag values

Metric tags are deliberately **low cardinality** so every series stays enumerable.

| Tag key | Values |
|---------|--------|
| `role` | `server`, `client` |
| `protocol` | `tcp`, `ssl` |
| `outcome` (connections) | `accepted`, `connected`, `rejected_maxconnections`, `rejected_notpermitted`, `rejected_blocked`, `rejected_authorization`, `failed` |
| `outcome` (handshake / auth / authorization) | `success`, `failure`, `timeout`, `canceled` |
| `outcome` (sync) | `completed`, `timeout` |
| `reason` | `Normal`, `Removed`, `Timeout`, `Shutdown`, `AuthFailure`, `ConnectionRejected`, `HandshakeFailure` |
| `message.kind` | `data`, `control`, `sync_request`, `sync_response` |
| `kind` (sync.expired) | `request`, `response` |
| `exception.type` | short exception type name (e.g. `IOException`, `SocketException`) |
| `socket.error` | `SocketError` value name (e.g. `ConnectionReset`) |

All of these keys are available as public constants on `WatsonTcpMetrics` (`TagRole`,
`TagProtocol`, `TagOutcome`, `TagReason`, `TagMessageKind`, `TagKind`, `TagExceptionType`,
`TagSocketError`).

---

## 4. Tracing spans

Spans are started from the `ActivitySource` named `WatsonTcp`. They carry the
high-cardinality identifiers that metrics deliberately omit, so you can correlate an
individual connection or request without exploding your metric series.

| Span name | Kind | Started when | Notable tags |
|-----------|------|--------------|--------------|
| `watsontcp.connect` | Client | A client connects (through registration/handshake, ends at disconnect) | `server.address`, `server.port`, `protocol`, `outcome`, `reason` |
| `watsontcp.session` | Server | A client is admitted (ends at disconnect) | `client.address`, `client.guid`, `protocol`, `reason` |
| `watsontcp.handshake` | Internal | A custom handshake runs | `role` |
| `watsontcp.send` | Producer | A message is sent | `message.bytes`, `message.sync`, `client.guid` (server) |
| `watsontcp.receive` | Consumer | A data message is processed | `message.bytes`, `client.guid` (server) |
| `watsontcp.sync` | Client | A `SendAndWaitAsync` round trip runs | `conversation.guid`, `message.bytes`, `outcome` |

> **Note:** span tag values that are not strings (for example `message.bytes`, a 64-bit
> integer, or `message.sync`, a boolean) are stored on `Activity.TagObjects`, not on the
> string-only `Activity.Tags` collection. Enumerate `TagObjects` to see them all.

---

## 5. Cardinality: what goes where

WatsonTcp follows the OpenTelemetry guidance that metric labels must be low cardinality.

- **Metric tags** are limited to values you could list on a whiteboard: `role`, `protocol`,
  `outcome`, `reason`, `message.kind`, `kind`, `exception.type`, `socket.error`.
- **Client GUIDs, remote `ip:port`, and conversation GUIDs never appear on metrics.** They
  are placed on spans (and are therefore available in your tracing backend and logs for
  per-connection or per-request correlation).

This keeps your time-series database from growing one series per client forever, while still
letting you drill into a specific connection through traces.

---

## 6. Example queries and panels

Once a Prometheus exporter is scraping (`watsontcp_*` series), useful PromQL includes:

```promql
# Inbound message rate, server side
sum(rate(watsontcp_messages_received_total{role="server"}[1m]))

# Live server connections
watsontcp_connections_active{role="server"}

# 95th-percentile synchronous round-trip latency
histogram_quantile(0.95, sum(rate(watsontcp_sync_duration_seconds_bucket[5m])) by (le))

# Disconnections broken out by reason
sum(rate(watsontcp_disconnections_total[5m])) by (reason)

# Connection rejections
sum(rate(watsontcp_connections_total{outcome=~"rejected_.*"}[5m])) by (outcome)

# Handshake failure ratio
sum(rate(watsontcp_handshakes_total{outcome="failure"}[5m]))
  / sum(rate(watsontcp_handshakes_total[5m]))
```

---

## 7. Target frameworks

The diagnostics APIs WatsonTcp uses are in-box on **net8.0** and **net10.0**. For the
down-level target frameworks (**netstandard2.0**, **netstandard2.1**, **net462**,
**net48**), WatsonTcp references the `System.Diagnostics.DiagnosticSource` package (pinned to
`8.0.1`), which supplies the same `Meter`, `Counter`, `Histogram`, `ObservableGauge`,
`TagList`, and `ActivitySource` types. No action is required on your part; the reference is
part of the WatsonTcp package.

If your host uses Radiant's `Radiant.SemConv` package for shared naming constants, note that
it too pins `System.Diagnostics.DiagnosticSource 8.0.1`, so the two unify cleanly.

---

## 8. Public constants reference

Everything a consumer needs is available symbolically on `WatsonTcp.WatsonTcpMetrics` so you
never have to hard-code a string:

```csharp
WatsonTcpMetrics.MeterName            // "WatsonTcp"
WatsonTcpMetrics.ActivitySourceName   // "WatsonTcp"

// Metric names, e.g.:
WatsonTcpMetrics.MessagesSent         // "watsontcp.messages.sent"
WatsonTcpMetrics.ConnectionsActive    // "watsontcp.connections.active"
WatsonTcpMetrics.SyncDuration         // "watsontcp.sync.duration"

// Span names, e.g.:
WatsonTcpMetrics.SpanSend             // "watsontcp.send"

// Tag keys, e.g.:
WatsonTcpMetrics.TagRole              // "role"
WatsonTcpMetrics.TagOutcome           // "outcome"
```

---

## 9. How it works internally (for the curious)

Each `WatsonTcpClient` / `WatsonTcpServer` owns one instrumentation object created in
`ConnectAsync`/`Start`, holding a single `Meter` and `ActivitySource` and disposed with its
owner. Recording is threaded through the same points as the existing `WatsonTcpStatistics`
plus the connection, handshake, authentication, authorization, and synchronous-request
lifecycle. Every recording call is null-guarded and fire-and-forget, so telemetry can never
throw into the send, receive, or connection path. See `ARCHITECTURE.md` (§11) for details.
The original design/implementation plan is preserved at `archive/TELEMETRY_PLAN.md`.
