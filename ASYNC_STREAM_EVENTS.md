# Async Stream Receive Plan

Implementation plan for issue `#186` and the broader async large-stream receive gap.

## Status Legend

- `[ ]` not started
- `[~]` in progress
- `[x]` completed
- `[!]` blocked / needs decision

## Goal

Add a true async receive path for stream payloads so large proxied streams can be consumed safely with `await`, without relying on `async void` event handlers over a live connection-backed stream.

This plan is intentionally concrete so a developer can mark progress directly in this file.

## Release Target

- Target release: `v6.3.0`
- Versioning rationale: this is a new opt-in public API and a backward-compatible behavior expansion, so it is a minor release, not a patch
- Wire protocol impact: none

## Current Behavior

Today:

- `Events.MessageReceived` receives a buffered payload
- `Events.StreamReceived` receives a `WatsonStream`
- small stream payloads are copied into a `MemoryStream` before delivery
- large stream payloads (`ContentLength >= MaxProxiedStreamSize`) are delivered synchronously over the live connection stream
- the live proxied stream must be fully consumed before the event handler returns
- `async void` handlers that `await` before draining the large proxied stream are unsafe because control returns to WatsonTcp while the same connection stream is still in use

Relevant files:

- `src/WatsonTcp/WatsonTcpServer.cs`
- `src/WatsonTcp/WatsonTcpClient.cs`
- `src/WatsonTcp/WatsonStream.cs`
- `src/WatsonTcp/WatsonTcpServerEvents.cs`
- `src/WatsonTcp/WatsonTcpClientEvents.cs`

## Desired Behavior

After this work:

- applications can register an awaited async stream callback on both server and client
- large proxied streams can be consumed safely with `await` and `CopyToAsync`
- the callback owns the stream until it returns
- unread bytes are drained before WatsonTcp attempts to parse the next message
- exceptions during async stream handling are treated as connection-fatal for that connection
- warnings and diagnostics for the feature flow through `Settings.Logger`, never `Console.WriteLine` or `Debug.WriteLine`
- existing sync event-based stream handling remains supported

## Scope

In scope:

- new async callback API for stream receive on client and server
- runtime dispatch changes on both client and server
- async read support in `WatsonStream`
- remainder-drain logic after stream handlers return
- logger-based warning and diagnostic consistency
- expanded shared automated coverage in `Test.Shared`
- focused documentation and changelog updates

Out of scope for this work:

- replacing `EventHandler` with async events
- changing the framing protocol
- introducing `System.IO.Pipelines`
- adding a per-message receive timeout for stream callbacks
- deprecating `Events.StreamReceived`
- turning message receive into an async callback API

## Proposed Public API

Preferred v1 API shape:

```csharp
public class WatsonTcpServerCallbacks
{
    public Func<StreamReceivedEventArgs, CancellationToken, Task> StreamReceivedAsync { get; set; }
}

public class WatsonTcpClientCallbacks
{
    public Func<StreamReceivedEventArgs, CancellationToken, Task> StreamReceivedAsync { get; set; }
}
```

Rationale:

- minimal public surface expansion
- reuses existing `StreamReceivedEventArgs`
- mirrors the existing callback-based direction already used for sync request handling, authorization, and handshake flows

## Callbacks Vs Events

The async API should live under `Callbacks`, not `Events`.

Rationale:

- `Events.StreamReceived` is a legacy synchronous `EventHandler<StreamReceivedEventArgs>`
- awaited application code fits the existing `Callbacks` model already used by other async extensibility points
- `Callbacks.StreamReceivedAsync` makes ownership and awaited execution explicit
- changing `Events.StreamReceived` into an async event would be a much riskier surface change
- the asymmetry is acceptable because the two APIs are intentionally different:
  - `Events.StreamReceived` is the legacy sync notification surface
  - `Callbacks.StreamReceivedAsync` is the new awaited ownership surface

This should be documented explicitly so users understand why both exist.

## Behavioral Contract

The implementation should follow these rules:

1. `MessageReceived` remains the highest-precedence receive mode for backward compatibility.
2. `Callbacks.StreamReceivedAsync` is the preferred stream path when `MessageReceived` is not configured.
3. `Events.StreamReceived` remains the legacy synchronous stream path.
4. Only one receive mode should be configured in user code; if multiple are configured, WatsonTcp should behave deterministically and log a warning.
5. Startup/connect validation must treat `StreamReceivedAsync` as a valid receive handler.
6. For large proxied streams, WatsonTcp must not continue reading the next message on the connection until the async callback returns.
7. When a stream callback or stream event returns early without consuming all `ContentLength` bytes, WatsonTcp must drain the unread remainder before continuing.
8. If draining the unread remainder fails, the connection must be closed.
9. If `StreamReceivedAsync` throws, WatsonTcp must log the exception, surface `ExceptionEncountered`, and close that connection.
10. The callback receives the connection cancellation token and should pass it through to `CopyToAsync`, `ReadAsync`, and any storage I/O.
11. Ownership of `DataStream` ends when the callback or event returns. Storing it for later use is unsupported.
12. No new timeout setting is required in v1; long-running callbacks intentionally hold message progression for that connection.
13. All warnings introduced by this feature must use `Settings.Logger?.Invoke(...)` with the appropriate severity level, never `Console.WriteLine`, `Debug.WriteLine`, or any direct console/debug output.

## File-Level Worklist

### API Surface

- [ ] Add `StreamReceivedAsync` to `src/WatsonTcp/WatsonTcpServerCallbacks.cs`
- [ ] Add `StreamReceivedAsync` to `src/WatsonTcp/WatsonTcpClientCallbacks.cs`
- [ ] Add XML doc comments describing ownership, cancellation, and remainder-drain behavior
- [ ] Update callback backing fields and any helper methods needed to query whether async stream callbacks are configured

### Client Runtime

- [ ] Update `src/WatsonTcp/WatsonTcpClient.cs` startup validation so a configured async stream callback is sufficient to connect
- [ ] Refactor client receive-path selection so it supports:
  - `MessageReceived`
  - `Callbacks.StreamReceivedAsync`
  - `Events.StreamReceived`
- [ ] For small payload streams, continue buffering into `MemoryStream`, then invoke the async callback with a `WatsonStream` over that buffered stream
- [ ] For large payload streams, invoke the async callback with a `WatsonStream` over the live connection stream and await completion
- [ ] After any stream callback or stream event returns, drain unread bytes before parsing the next message
- [ ] If async stream handling throws, log, raise `ExceptionEncountered`, and disconnect from the server
- [ ] Ensure client-side handshake/auth flows remain unaffected by the new stream dispatch order

### Server Runtime

- [ ] Update `src/WatsonTcp/WatsonTcpServer.cs` startup validation so a configured async stream callback is sufficient to start
- [ ] Refactor server receive-path selection so it supports:
  - `MessageReceived`
  - `Callbacks.StreamReceivedAsync`
  - `Events.StreamReceived`
- [ ] For small payload streams, continue buffering into `MemoryStream`, then invoke the async callback with a `WatsonStream` over that buffered stream
- [ ] For large payload streams, invoke the async callback with a `WatsonStream` over the live connection stream and await completion
- [ ] After any stream callback or stream event returns, drain unread bytes before parsing the next message
- [ ] If async stream handling throws, log, raise `ExceptionEncountered`, and disconnect only the affected client
- [ ] Confirm one client's long-running stream callback does not block unrelated clients on the server

### Stream Wrapper

- [ ] Extend `src/WatsonTcp/WatsonStream.cs` with true async read support
- [ ] Override `ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)`
- [ ] Conditionally override `ReadAsync(Memory<byte> buffer, CancellationToken token)` on supported TFMs
- [ ] Preserve correct `Length`, `Position`, EOF, and bounds behavior for both sync and async reads
- [ ] Add an internal way to inspect remaining unread bytes after handler return
- [ ] Add an internal async drain helper that consumes any unread remainder safely
- [ ] Ensure drain logic works for both buffered `MemoryStream`-backed instances and live connection-backed instances

### Diagnostics And Safety

- [ ] Log a warning when more than one receive mode is configured on a single client/server instance
- [ ] Route all new warnings and diagnostics through `Settings.Logger`, never `Console.WriteLine` or `Debug.WriteLine`
- [ ] Audit all new code paths for direct console or debug output before merge
- [ ] Update exception logging so async stream callback failures clearly identify the receive mode and remote endpoint
- [ ] Document and preserve the per-connection backpressure behavior for awaited stream callbacks
- [ ] Document why `StreamReceivedAsync` lives on `Callbacks` while `StreamReceived` remains on `Events`
- [ ] Decide whether sync `Events.StreamReceived` should also benefit from automatic unread-byte drain
- [ ] Apply unread-byte drain to sync `Events.StreamReceived` as part of this work unless a concrete regression risk is identified

## Receive Mode Decision Matrix

This is the intended runtime selection order:

1. `Events.MessageReceived`
2. `Callbacks.StreamReceivedAsync`
3. `Events.StreamReceived`
4. otherwise fail startup/connect with the existing invalid-configuration exception

Required behavior:

- if `MessageReceived` and either stream mode are both configured, `MessageReceived` wins and a warning is logged
- if both stream modes are configured, `StreamReceivedAsync` wins and a warning is logged
- tests must verify these rules on both client and server
- warning assertions must be made through captured `Settings.Logger` output, not console/debug output

## Test Strategy

All automated coverage should live in `src/Test.Shared/WatsonTcpScenarios.cs` and be surfaced through:

- `src/Test.Automated`
- `src/Test.XUnit`
- `src/Test.Nunit`

Also:

- [ ] Add a dedicated `streaming` suite in `src/Test.Shared/WatsonTcpSuites.cs` for focused runs
- [ ] Keep stream scenarios included in the general regression run

## Test Case Inventory

Each line below is a candidate shared scenario name and its required assertions.

### Configuration And Precedence

- [ ] `ServerStartsWithOnlyAsyncStreamCallback`
  - server starts successfully with only `Callbacks.StreamReceivedAsync`
- [ ] `ClientConnectsWithOnlyAsyncStreamCallback`
  - client connects successfully with only `Callbacks.StreamReceivedAsync`
- [ ] `ServerStartFailsWithoutAnyReceiveHandler`
  - same failure as today when no receive mode is configured
- [ ] `ClientConnectFailsWithoutAnyReceiveHandler`
  - same failure as today when no receive mode is configured
- [ ] `MessageReceivedTakesPrecedenceOverAsyncStreamCallbackOnServer`
  - `MessageReceived` fires
  - `StreamReceivedAsync` does not fire
  - warning is logged
- [ ] `MessageReceivedTakesPrecedenceOverAsyncStreamCallbackOnClient`
  - `MessageReceived` fires
  - `StreamReceivedAsync` does not fire
  - warning is logged
- [ ] `AsyncStreamCallbackTakesPrecedenceOverSyncStreamEventOnServer`
  - async callback fires
  - sync stream event does not fire
  - warning is logged
- [ ] `AsyncStreamCallbackTakesPrecedenceOverSyncStreamEventOnClient`
  - async callback fires
  - sync stream event does not fire
  - warning is logged
- [ ] `ServerReceiveModeWarningsRouteThroughLogger`
  - configure conflicting receive modes
  - capture `Settings.Logger`
  - assert warning text/severity is captured through the logger
  - assert no direct console/debug dependency in the scenario design
- [ ] `ClientReceiveModeWarningsRouteThroughLogger`
  - configure conflicting receive modes
  - capture `Settings.Logger`
  - assert warning text/severity is captured through the logger
  - assert no direct console/debug dependency in the scenario design

### Server Receive Positive Cases

- [ ] `ServerAsyncStreamReceiveSmallPayload`
  - callback fires
  - payload bytes match
  - metadata is preserved
- [ ] `ServerAsyncStreamReceiveLargePayload`
  - callback fires
  - `CopyToAsync` completes
  - payload bytes match
- [ ] `ServerAsyncStreamReceiveExactThresholdPayload`
  - payload size exactly equals `MaxProxiedStreamSize`
  - correct path is exercised and bytes match
- [ ] `ServerAsyncStreamReceiveLargePayloadMultipleChunks`
  - callback loops over `ReadAsync`
  - all bytes arrive in order
- [ ] `ServerAsyncStreamReceiveLargePayloadViaCopyToAsync`
  - callback uses `CopyToAsync`
  - all bytes arrive in order
- [ ] `ServerAsyncStreamReceiveLargePayloadWithMetadata`
  - metadata survives the receive path
- [ ] `ServerAsyncStreamReceiveZeroLengthPayload`
  - zero-length behavior is defined and does not hang
- [ ] `ServerAsyncStreamReceiveSequentialLargeMessages`
  - same connection receives multiple large stream messages in order
- [ ] `ServerAsyncStreamReceiveLargeThenMessagePayload`
  - stream message is handled
  - subsequent normal `MessageReceived` message arrives intact on the same connection when configured for message mode in a separate scenario or follow-up connection
- [ ] `ServerAsyncStreamReceivePartialReadThenReturnDrainsRemainder`
  - callback intentionally reads only part of the stream
  - WatsonTcp drains the remainder
  - a following message on the same connection is parsed correctly
- [ ] `ServerAsyncStreamReceiveNoReadThenReturnDrainsRemainder`
  - callback reads nothing
  - WatsonTcp drains the remainder
  - following message arrives intact
- [ ] `ServerAsyncStreamReceiveUsingDataProperty`
  - callback uses `args.Data`
  - payload is fully consumed
  - following message arrives intact
- [ ] `ServerAsyncStreamReceiveConcurrentClientsIsolation`
  - one client runs a slow async stream callback
  - another client can still send/receive normally
- [ ] `ServerAsyncStreamReceiveSslLargePayload`
  - same large payload scenario over SSL

### Server Receive Negative Cases

- [ ] `ServerAsyncStreamReceiveCallbackThrowsBeforeRead`
  - callback throws immediately
  - `ExceptionEncountered` fires
  - client disconnects
  - server remains healthy for new clients
- [ ] `ServerAsyncStreamReceiveCallbackThrowsAfterPartialRead`
  - callback reads part of the stream then throws
  - exception is surfaced
  - affected client disconnects
  - server remains healthy for other clients
- [ ] `ServerAsyncStreamReceiveClientDisconnectsMidStream`
  - sender disconnects during payload transfer
  - receiver does not hang
  - disconnect is observed
- [ ] `ServerAsyncStreamReceiveServerStopCancelsCallback`
  - callback sees cancellation or read failure caused by stop
  - server stops cleanly
- [ ] `ServerAsyncStreamReceiveDrainFailureDisconnectsClient`
  - simulate a failure while draining unread bytes
  - affected client disconnects
- [ ] `ServerAsyncStreamReceiveUnreadRemainderDoesNotCorruptNextClient`
  - one client misbehaves
  - another client is unaffected

### Client Receive Positive Cases

- [ ] `ClientAsyncStreamReceiveSmallPayload`
  - callback fires
  - payload bytes match
  - metadata is preserved
- [ ] `ClientAsyncStreamReceiveLargePayload`
  - callback fires
  - `CopyToAsync` completes
  - payload bytes match
- [ ] `ClientAsyncStreamReceiveExactThresholdPayload`
  - payload size exactly equals `MaxProxiedStreamSize`
  - correct path is exercised and bytes match
- [ ] `ClientAsyncStreamReceiveLargePayloadMultipleChunks`
  - callback loops over `ReadAsync`
  - all bytes arrive in order
- [ ] `ClientAsyncStreamReceiveLargePayloadViaCopyToAsync`
  - callback uses `CopyToAsync`
  - all bytes arrive in order
- [ ] `ClientAsyncStreamReceiveLargePayloadWithMetadata`
  - metadata survives the receive path
- [ ] `ClientAsyncStreamReceiveZeroLengthPayload`
  - zero-length behavior is defined and does not hang
- [ ] `ClientAsyncStreamReceiveSequentialLargeMessages`
  - same connection receives multiple large stream messages in order
- [ ] `ClientAsyncStreamReceivePartialReadThenReturnDrainsRemainder`
  - callback reads only part of the stream
  - WatsonTcp drains the remainder
  - following message arrives intact
- [ ] `ClientAsyncStreamReceiveNoReadThenReturnDrainsRemainder`
  - callback reads nothing
  - WatsonTcp drains the remainder
  - following message arrives intact
- [ ] `ClientAsyncStreamReceiveUsingDataProperty`
  - callback uses `args.Data`
  - payload is fully consumed
  - following message arrives intact
- [ ] `ClientAsyncStreamReceiveSslLargePayload`
  - same large payload scenario over SSL

### Client Receive Negative Cases

- [ ] `ClientAsyncStreamReceiveCallbackThrowsBeforeRead`
  - callback throws immediately
  - `ExceptionEncountered` fires
  - client disconnects cleanly
- [ ] `ClientAsyncStreamReceiveCallbackThrowsAfterPartialRead`
  - callback reads part of the stream then throws
  - exception is surfaced
  - client disconnects cleanly
- [ ] `ClientAsyncStreamReceiveServerDisconnectsMidStream`
  - sender disconnects during payload transfer
  - receiver does not hang
  - disconnect is observed
- [ ] `ClientAsyncStreamReceiveDisconnectCancelsCallback`
  - local disconnect or remote disconnect interrupts active async stream consumption
- [ ] `ClientAsyncStreamReceiveDrainFailureDisconnectsConnection`
  - drain failure after callback return closes the connection

### Existing Sync Stream Path Regression Expansion

These are not the new feature, but this work is a good chance to harden the existing stream path.

- [ ] `ServerSyncStreamReceiveLargePayloadStillWorks`
- [ ] `ClientSyncStreamReceiveLargePayloadStillWorks`
- [ ] `ServerSyncStreamPartialReadThenReturnDrainsRemainder`
- [ ] `ClientSyncStreamPartialReadThenReturnDrainsRemainder`
- [ ] `ServerSyncStreamNoReadThenReturnDrainsRemainder`
- [ ] `ClientSyncStreamNoReadThenReturnDrainsRemainder`
- [ ] `ServerSyncStreamExactThresholdPayload`
- [ ] `ClientSyncStreamExactThresholdPayload`

### WatsonStream Behavior Coverage

These can be integration tests rather than pure unit tests if that is simpler for this repository.

- [ ] `WatsonStreamAsyncReadAdvancesPositionCorrectly`
- [ ] `WatsonStreamAsyncReadReturnsZeroAtEnd`
- [ ] `WatsonStreamSyncAndAsyncReadLengthsMatch`
- [ ] `WatsonStreamCopyToAsyncReadsEntirePayload`
- [ ] `WatsonStreamCannotSeekOrWrite`
- [ ] `WatsonStreamLengthAndPositionRemainAccurateUnderChunkedReads`

### Broader Stream Regression Opportunities

These are worthwhile expansions even if not all are required for the first pass.

- [ ] `LargeStreamThenSyncRequestResponse`
- [ ] `LargeStreamAfterAuthorizationHandshake`
- [ ] `LargeStreamAfterPresharedKeyAuthentication`
- [ ] `BidirectionalLargeStreamsSequentially`
- [ ] `LargeStreamWithHighMetadataCount`
- [ ] `LargeStreamWithSmallBufferSize`
- [ ] `LargeStreamWithNoDelayFalse`
- [ ] `LargeStreamAcrossMultipleTargetFrameworksInCI`

### Long-Running Or Non-CI Candidates

These should be clearly marked if added so they can be excluded from the fast default suite when needed.

- [ ] `LargeStreamSoakTestSingleConnection`
- [ ] `LargeStreamSoakTestConcurrentClients`
- [ ] `LargeStreamMemoryPressureTest`
- [ ] `LargeStreamVeryLargePayloadBoundaryTest`

## Documentation Worklist

- [ ] Update `README.md`
  - explain the old limitation clearly
  - add server example using `Callbacks.StreamReceivedAsync`
  - add client example using `Callbacks.StreamReceivedAsync`
  - explain why `StreamReceivedAsync` is a callback while `StreamReceived` remains an event
  - document receive-mode precedence
  - document that stream ownership ends when the callback returns
  - document unread-byte drain behavior
  - document exception/disconnect semantics
  - document that warnings flow through `Settings.Logger`
  - document that `MessageReceived` is still buffered and remains separate
- [ ] Update `ARCHITECTURE.md`
  - describe receive-mode dispatch order
  - describe small vs large stream delivery under the new callback path
  - describe why awaited callbacks are safe and `async void` events are not
  - describe why the awaited API lives in `Callbacks`
  - describe per-connection backpressure behavior
  - describe drain-on-return behavior
- [ ] Update `CHANGELOG.md`
  - mark as `v6.3.0`
  - summarize async stream callback support
  - summarize unread-byte drain hardening
  - summarize new automated coverage
- [ ] Update XML documentation via source comments
- [ ] Review whether `CONTRIBUTING.md` needs a short note about new streaming tests and suite names

## Suggested Implementation Order

- [ ] Step 1: add callback API surface and startup/connect validation
- [ ] Step 2: implement async `WatsonStream` reads
- [ ] Step 3: refactor server receive path to use async stream callback
- [ ] Step 4: refactor client receive path to use async stream callback
- [ ] Step 5: implement unread-byte drain behavior for async and sync stream paths
- [ ] Step 6: add configuration, precedence, and smoke tests
- [ ] Step 7: add full positive/negative client and server stream scenarios
- [ ] Step 8: update documentation and changelog
- [ ] Step 9: run all three Touchstone-backed test hosts
- [ ] Step 10: after tests pass and documentation is updated, run a release build
- [ ] Step 11: prepare release notes and issue close-out text

## Verification Commands

Use these after implementation:

```bash
dotnet build src/WatsonTcp.sln -c Debug
dotnet run --project src/Test.Automated/Test.Automated.csproj --framework net8.0 -- --results test-results/cli-results.json
dotnet test src/Test.XUnit/Test.XUnit.csproj -c Debug --framework net8.0
dotnet test src/Test.Nunit/Test.Nunit.csproj -c Debug --framework net8.0
dotnet build src/WatsonTcp.sln -c Release
```

## Definition Of Done

- [ ] Client and server both support `Callbacks.StreamReceivedAsync`
- [ ] Large proxied streams can be consumed safely with `await`
- [ ] Unread stream remainders are drained before next-message parsing
- [ ] Async callback failures are logged and close only the affected connection
- [ ] Existing sync stream mode still works
- [ ] Shared tests cover both positive and negative cases on both client and server
- [ ] Touchstone CLI, xUnit, and NUnit hosts all pass
- [ ] README, architecture notes, and changelog are updated
- [ ] Release build passes after documentation and automated tests are complete
- [ ] Version is updated to `6.3.0`
- [ ] Issue `#186` can be closed with an accurate explanation of what changed
