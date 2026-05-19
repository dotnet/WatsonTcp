# Performance Improvements

This document captures code-level opportunities to improve WatsonTcp in four areas:

- faster response times
- higher throughput
- faster connection setup
- higher connection counts

Scope reviewed:

- `src/WatsonTcp/WatsonTcpClient.cs`
- `src/WatsonTcp/WatsonTcpServer.cs`
- `src/WatsonTcp/WatsonMessageBuilder.cs`
- `src/WatsonTcp/WatsonCommon.cs`
- `src/WatsonTcp/ClientMetadataManager.cs`
- `src/WatsonTcp/DefaultSerializationHelper.cs`
- `src/WatsonTcp/WatsonStream.cs`
- `src/Test.Throughput/*`
- `src/Test.MaxConnections/*`

Most of the items below do not require a public API change. A few have an optional phase 2 that changes on-the-wire behavior but still preserves the C# API surface.

## How To Use This File

For each item:

- fill in `Owner`
- change `Status` as work progresses
- check off the implementation and validation boxes
- record benchmark deltas before closing the item

Suggested status values:

- `Not started`
- `In progress`
- `Blocked`
- `Done`

## Measurement Gate

The current perf test projects are useful for manual exploration, but they are not yet a reliable regression harness:

- `src/Test.Throughput/Test1.cs` and `src/Test.Throughput/Test2.cs` intentionally add `Task.Delay(1)` before every send, which caps throughput.
- the current test apps are interactive, which makes repeatable CI/perf regression checks difficult.

Before landing any P0 or P1 item:

- [ ] Add a non-interactive benchmark mode for throughput, connection setup time, and max stable connection count.
- [ ] Add microbenchmarks for header parse, header serialization, and `byte[]` send paths.
- [ ] Capture p50/p95/p99 latency, messages/sec, MB/sec, alloc/op, and connection setup time for both TCP and TLS.
- [ ] Test at payload sizes `0B`, `64B`, `1KB`, `64KB`, and `1MB`.
- [ ] Test at concurrency levels `1`, `16`, `256`, and `1024+` where hardware allows.

## Priority Summary

| ID | Priority | API impact | Faster responses | Throughput | Setup | Connection count |
| --- | --- | --- | --- | --- | --- | --- |
| PERF-01 | P0 | None | High | High | Low | Medium |
| PERF-02 | P0 | None | High | High | Low | Medium |
| PERF-03 | P0 | None | High | High | Low | Low |
| PERF-04 | P0 | None | Medium | High | Low | High |
| PERF-05 | P0 | None in phase 1 | Medium | Low | High | Medium |
| PERF-06 | P1 | None | Medium | Medium | Medium | Medium |
| PERF-07 | P1 | None | Medium | High | Low | High |
| PERF-08 | P1 | None | Low | Medium | Medium | High |
| PERF-09 | P2 | None | High | High | Medium | Very high |

## Recommended Execution Order

1. PERF-01
2. PERF-02
3. PERF-03
4. PERF-04
5. PERF-05
6. PERF-06
7. PERF-07
8. PERF-08
9. PERF-09

## PERF-01: Replace Byte-At-A-Time Header Parsing With A Buffered Framed Reader

Owner:
Status: Not started
API impact: None
Primary goals: faster response times, higher throughput, better connection scale

Current hot spots:

- `src/WatsonTcp/WatsonMessageBuilder.cs` - `BuildFromStream`
- Current reads are one byte at a time (`ReadAsync(..., 0, 1, ...)`) and every byte is appended with `MemoryStream.WriteByte`.
- The header is then copied again with `ToArray()` and decoded with `Encoding.UTF8.GetString(...)`.
- `src/WatsonTcp/WatsonTcpClient.cs` and `src/WatsonTcp/WatsonTcpServer.cs` treat `msg == null` as a retry case and sleep for `30ms`, even though `ReadAsync == 0` is usually EOF.

Why this matters:

- Small messages pay one async read per header byte.
- TLS streams amplify this cost because each read may unwrap a TLS frame only to return one byte.
- Connection close detection is delayed by the retry/sleep path.
- The current path cannot reuse bytes when header and payload arrive in the same socket read.

Implementation plan:

- [ ] Introduce a per-connection buffered receive path that reads chunks, not single bytes.
- [ ] Scan the buffered data for `\r\n\r\n` without per-byte async calls.
- [ ] Preserve any bytes already read past the header boundary and feed them into payload reads.
- [ ] Treat `ReadAsync == 0` as peer disconnect instead of `return null` plus `Task.Delay(30)`.
- [ ] Keep `MaxHeaderSize` enforcement in the new parser.
- [ ] Benchmark both plain TCP and TLS before and after.

Validation:

- [ ] Measure 0B, 64B, and 1KB message latency before and after.
- [ ] Measure messages/sec improvement for header-heavy workloads.
- [ ] Confirm disconnect detection is still correct for clean shutdown and mid-stream disconnects.

Expected payoff:

- Likely the single highest-value no-API-change latency and throughput improvement in the receive path.

## PERF-02: Remove Avoidable Copies And Memory Materialization In Send/Receive Hot Paths

Owner:
Status: Not started
API impact: None
Primary goals: faster response times, higher throughput, lower allocation rate

Current hot spots:

- `src/WatsonTcp/WatsonCommon.cs` - `BytesToStream`
- `src/WatsonTcp/WatsonCommon.cs` - `ReadFromStreamAsync`
- `src/WatsonTcp/WatsonCommon.cs` - `DataStreamToMemoryStream`
- `src/WatsonTcp/StreamReceivedEventArgs.cs` - `Data`
- `src/WatsonTcp/WatsonTcpClient.cs` and `src/WatsonTcp/WatsonTcpServer.cs` - all `SendAsync(byte[])`, sync response, status, and handshake payload paths

Why this matters:

- `SendAsync(byte[])` currently copies the supplied array into a new `MemoryStream`, then re-reads it into another buffer during write.
- `BytesToStream` creates avoidable copies for status messages, handshake messages, sync responses, and normal sends.
- Read helpers build empty `MemoryStream` instances, grow them, and call `ToArray()`, which copies again.
- The helpers also allocate a new smaller buffer for the tail read instead of reusing the same rented buffer.

Implementation plan:

- [ ] Change `BytesToStream` to wrap the existing `byte[]` without copying when possible.
- [ ] Add an internal fast path for `byte[]` or `ReadOnlyMemory<byte>` sends so small payloads bypass `MemoryStream` entirely.
- [ ] Pre-size `MemoryStream` with `contentLength` when buffering is still required.
- [ ] Reuse a single buffer for tail reads instead of allocating `new byte[bytesRemaining]`.
- [ ] Use pooled buffers for `StreamReceivedEventArgs.Data` and the buffered read helpers where possible.
- [ ] Audit status and handshake message paths to make sure control-plane messages use the direct fast path.

Validation:

- [ ] Compare allocations/op for `SendAsync(byte[])` before and after.
- [ ] Compare end-to-end throughput for 64B, 1KB, and 64KB payloads.
- [ ] Confirm large streaming behavior is unchanged when `MaxProxiedStreamSize` is exceeded.

Expected payoff:

- High. This removes repeated copies from the most common message paths without changing the public API.

## PERF-03: Coalesce Header And Payload Writes And Remove Per-Message Flushes

Owner:
Status: Not started
API impact: None
Primary goals: faster response times, higher throughput

Current hot spots:

- `src/WatsonTcp/WatsonTcpClient.cs` - `SendHeadersAsync`, `SendDataStreamAsync`
- `src/WatsonTcp/WatsonTcpServer.cs` - `SendHeadersAsync`, `SendDataStreamAsync`

Why this matters:

- The library currently writes header and payload separately, and it flushes after the header and again after the payload.
- With `NoDelay = true`, small messages are more likely to become multiple packets.
- With `SslStream`, separate writes commonly become separate TLS records, which hurts small-message latency and CPU efficiency.
- `NetworkStream.FlushAsync` usually adds no value, so the extra flush calls are mostly overhead.

Implementation plan:

- [ ] Remove the header flush from the hot path.
- [ ] Remove the payload flush from the hot path unless a specific stream type actually requires it.
- [ ] For small payloads, combine header and payload into one rented buffer and issue a single `WriteAsync`.
- [ ] Keep the streaming path for large payloads so large transfers remain chunked.
- [ ] Benchmark both TCP and TLS with small messages because the improvement should be most visible there.

Validation:

- [ ] Measure p50 and p99 latency for 0B to 1KB messages.
- [ ] Compare TLS throughput before and after.
- [ ] Confirm framing correctness under mixed small and large payloads.

Expected payoff:

- High for chatty, small-message workloads.

## PERF-04: Remove Proactive Socket Liveness Polling From The Server Receive Loop

Owner:
Status: Not started
API impact: None
Primary goals: higher throughput, higher connection counts, lower per-message overhead

Current hot spots:

- `src/WatsonTcp/WatsonTcpServer.cs` - `IsClientConnected`
- `src/WatsonTcp/WatsonTcpServer.cs` - `DataReceiver`

Why this matters:

- `IsClientConnected` walks `IPGlobalProperties.GetActiveTcpConnections()`, does a zero-byte send, then uses `Poll` and `Receive(..., Peek)` before every read loop iteration.
- That is expensive even at moderate connection counts and becomes a major scalability limiter when many clients are connected.
- The server already has enough signal from `ReadAsync == 0`, `IOException`, `ObjectDisposedException`, and cancellation to detect disconnects.

Implementation plan:

- [ ] Delete the `IsClientConnected` check from the hot receive loop.
- [ ] Let the next read decide whether the connection is alive.
- [ ] Treat EOF and socket exceptions as disconnect and exit the loop immediately.
- [ ] Keep TCP keepalives and the idle timeout monitor as the long-lived dead-peer detection mechanism.
- [ ] Re-test disconnect semantics for graceful close, reset, and idle timeout cases.

Validation:

- [ ] Measure CPU usage at `256+` idle and active connections before and after.
- [ ] Measure throughput with many concurrently connected clients.
- [ ] Confirm the disconnection event still fires correctly.

Expected payoff:

- Very high for connection count scaling. This is one of the clearest bottlenecks in the current server loop.

## PERF-05: Make The Client Connection Pipeline Fully Async Internally

Owner:
Status: Not started
API impact: None in phase 1
Primary goals: faster connection setup, better client-side scale during concurrent connects

Current hot spots:

- `src/WatsonTcp/WatsonTcpClient.cs` - `Connect`
- `src/WatsonTcp/WatsonTcpClient.cs` - `CompleteConnectionInitialization`

Why this matters:

- `Connect` uses `BeginConnect` plus `WaitHandle.WaitOne`, synchronous `AuthenticateAsClient`, `.Result`, `.Wait`, and `Task.WaitAny`.
- Those blocking waits increase setup latency and make it harder to scale many concurrent outgoing connections.
- `CompleteConnectionInitialization` includes a hard-coded `Task.Delay(50)` negotiation window, which adds avoidable startup latency when no auth or handshake is required.

Implementation plan:

- [ ] Build an internal `ConnectCoreAsync` that uses `ConnectAsync`, async TLS auth, and `await Task.WhenAny`.
- [ ] Keep the public `Connect()` method as a sync wrapper so the public API does not need to change.
- [ ] Remove `.Result`, `.Wait`, and `Task.WaitAny` from the connection path.
- [ ] Phase 2 optional: make the server send an explicit initialization status so the client can finish setup immediately without the `50ms` speculation window.
- [ ] Phase 2 optional: add a public `ConnectAsync` only if the project wants an additive API improvement. This is not required for the initial perf win.

Validation:

- [ ] Measure average and p99 connection setup time for TCP and TLS.
- [ ] Measure concurrent connect storms from one process to many servers and from many clients to one server.
- [ ] Confirm timeout and rejection behavior are unchanged.

Expected payoff:

- High for connection setup time, especially under TLS or connection bursts.

## PERF-06: Cache Serializer State And Emit Headers Directly As UTF-8

Owner:
Status: Not started
API impact: None
Primary goals: faster response times, higher throughput, faster connection setup for control-plane messages

Current hot spots:

- `src/WatsonTcp/DefaultSerializationHelper.cs` - `SerializeJson`
- `src/WatsonTcp/DefaultSerializationHelper.cs` - `DeserializeJson`
- `src/WatsonTcp/WatsonMessageBuilder.cs` - `GetHeaderBytes`

Why this matters:

- `SerializeJson` allocates a new `JsonSerializerOptions` and re-adds converters on every call.
- Header generation currently serializes to `string`, encodes to `byte[]`, then copies again into a second array to append `\r\n\r\n`.
- Control-plane messages such as handshake, auth, and status updates pay this cost frequently during setup.

Implementation plan:

- [ ] Cache the compact and pretty `JsonSerializerOptions` instances once per helper.
- [ ] Switch header generation to direct UTF-8 emission, not `string` then `Encoding.UTF8.GetBytes`.
- [ ] Append the header delimiter without allocating a second full-size array when possible.
- [ ] If serializer cost is still prominent after caching, add source-generated serialization for `WatsonMessage` and handshake/status DTOs on supported targets.

Validation:

- [ ] Benchmark `GetHeaderBytes` throughput and allocations.
- [ ] Benchmark connect/setup time for TLS plus handshake/auth scenarios.
- [ ] Confirm serialized wire format is unchanged unless intentionally versioned.

Expected payoff:

- Medium to high, with especially good payoff on small messages and connection setup flows.

## PERF-07: Replace Per-Message `Task.Run` Dispatch With Bounded Message Dispatchers

Owner:
Status: Not started
API impact: None
Primary goals: faster response times, higher throughput, higher connection counts

Current hot spots:

- `src/WatsonTcp/WatsonTcpClient.cs` - message event dispatch and sync request handling
- `src/WatsonTcp/WatsonTcpServer.cs` - message event dispatch, sync request handling, accepted-client processing, handshake task startup

Why this matters:

- The code schedules separate `Task.Run` work items for many message and control-plane events.
- At high message rates this adds thread-pool pressure, queueing delay, and extra allocations.
- It also makes backpressure hard because the number of queued work items can grow without a clear bound.

Implementation plan:

- [ ] Remove `Task.Run` around naturally async methods where it is only adding a thread-pool hop.
- [ ] For synchronous message/event handlers, introduce a bounded `Channel<T>` or similar dispatcher.
- [ ] Preserve per-connection message ordering if that behavior is expected.
- [ ] Add backpressure or drop/reject rules so a slow consumer cannot create unbounded memory growth.
- [ ] Revisit sync request handling so it shares the same dispatch strategy instead of creating a new task per request.

Validation:

- [ ] Measure throughput and GC activity at high message rates.
- [ ] Confirm ordering and reentrancy behavior expected by current consumers.
- [ ] Stress test slow handlers to verify backpressure behavior.

Expected payoff:

- Strong throughput and scale improvement once message rate is high enough for scheduler overhead to dominate.

## PERF-08: Reduce Connection Manager Lock Contention And Snapshot Copying

Owner:
Status: Not started
API impact: None
Primary goals: higher throughput, higher connection counts, faster connection admission

Current hot spots:

- `src/WatsonTcp/ClientMetadataManager.cs`
- `src/WatsonTcp/WatsonTcpServer.cs` - `ListClients`, idle-client monitoring, GUID replacement, last-seen updates
- `src/WatsonTcp/WatsonTcpServer.cs` - IP ACL checks against `PermittedIPs` and `BlockedIPs`

Why this matters:

- `ClientMetadataManager` uses a single `ReaderWriterLockSlim` over several hot dictionaries.
- `UpdateClientLastSeen` takes a write lock on every received message.
- `AllClients`, `AllPendingClients`, and `AllClientsLastSeen` clone whole dictionaries.
- Idle monitoring copies the entire last-seen dictionary every pass.
- `PermittedIPs` and `BlockedIPs` are `List<string>` and use linear `Contains(...)` for every new connection.

Implementation plan:

- [ ] Move hot-path connection maps to data structures that reduce global lock contention.
- [ ] Store last-seen ticks on `ClientMetadata` or in a dedicated concurrent map so per-message updates are cheaper.
- [ ] Make the idle monitor iterate directly over live client state instead of cloning a dictionary every cycle.
- [ ] Compile `PermittedIPs` and `BlockedIPs` into `HashSet<string>` snapshots at `Start()` time.
- [ ] Keep public behavior unchanged even if internal storage changes.

Validation:

- [ ] Measure accept rate with large ACLs.
- [ ] Measure throughput with many active connections and frequent last-seen updates.
- [ ] Measure memory and CPU during idle-client scans.

Expected payoff:

- Medium individually, but important once connection counts grow.

## PERF-09: Add A High-Scale Internal Transport Engine For Very Large Connection Counts

Owner:
Status: Not started
API impact: None
Primary goals: much higher connection counts, much higher throughput, lower CPU per byte

Current hot spots:

- per-connection tasks
- per-connection semaphores
- per-connection cancellation sources
- stream-based framing layered over `TcpClient` and `SslStream`

Why this matters:

- The current architecture is reasonable for normal server counts, but it will eventually hit scaling limits because each connection carries a meaningful amount of managed coordination state.
- If the library wants to push far beyond current connection-count expectations, the biggest gains will come from an internal transport rewrite, not from micro-optimizations alone.

Implementation plan:

- [ ] Introduce an internal transport abstraction so the public API is decoupled from the current `TcpClient` engine.
- [ ] Prototype a `SocketAsyncEventArgs` plus buffer-pool implementation or a `System.IO.Pipelines` implementation.
- [ ] Pool connection state, buffers, and message builders aggressively.
- [ ] Keep the existing public client/server/events/callbacks model on top of the new transport.
- [ ] Land this only after P0 and P1 items have been measured, because this is the highest-effort option.

Validation:

- [ ] Run max-connection tests until CPU, memory, or port exhaustion becomes the limiter.
- [ ] Compare per-connection memory footprint before and after.
- [ ] Compare throughput at `1024+` simultaneous connections.

Expected payoff:

- Highest upside for connection-count scaling, but also the largest engineering investment.
