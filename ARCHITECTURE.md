# WatsonTcp Internal Architecture

## 1. Overview

WatsonTcp is a C# TCP client/server library that provides reliable message-level delivery over TCP by implementing a custom framing protocol. TCP is a bidirectional byte stream with no inherent message boundaries; WatsonTcp solves this by prepending each message with a JSON header that declares the payload length, followed by a `\r\n\r\n` delimiter, followed by the raw data bytes. This allows receivers to know exactly how many bytes constitute each application-level message.

The library targets .NET Standard 2.0/2.1, .NET Framework 4.62/4.8, and .NET 6.0/8.0. Both endpoints of a connection must either use WatsonTcp or implement compatible framing (see [FRAMING.md](FRAMING.md) for the wire protocol specification).

Beyond framing, WatsonTcp provides:
- Automatic connection lifecycle management with event-driven notifications
- Synchronous request/response messaging (with timeout and expiration)
- Optional SSL/TLS encryption
- Preshared key authentication
- Optional server-side connection authorization
- Optional framed pre-registration handshakes
- Idle connection detection and timeout
- TCP keepalive configuration

## 1.1 Admission Pipeline

As of `v6.2.0`, server-side connection handling distinguishes pending connections from active clients.

Pending connection flow:

1. TCP accept
2. SSL/TLS establishment when enabled
3. `AuthorizeConnectionAsync`
4. preshared-key flow when configured
5. `HandshakeAsync` when configured
6. `RegisterClient`
7. active client registration and `ClientConnected`

During steps 1-6, the connection is tracked as pending and does not appear in `ListClients()`.

## 2. Message Flow

### Sending a Message

When a caller invokes `SendAsync()`, the following sequence occurs:

```
SendAsync(data, metadata)
  |
  v
BytesToStream(data) --> convert byte[] to MemoryStream + contentLength
  |
  v
MessageBuilder.ConstructNew(contentLength, stream, metadata, ...)
  |                          --> creates WatsonMessage with fields populated
  v
SendInternalAsync(msg, contentLength, stream, token)
  |
  v
WriteLock.WaitAsync()       --> acquire per-connection SemaphoreSlim(1,1)
  |
  v
SendHeadersAsync(msg)
  |   MessageBuilder.GetHeaderBytes(msg)
  |     --> SerializeJson(msg) --> UTF-8 encode --> append \r\n\r\n
  |   DataStream.WriteAsync(headerBytes)
  |   DataStream.FlushAsync()
  v
SendDataStreamAsync(contentLength, stream)
  |   Rent buffer from ArrayPool<byte>.Shared
  |   Loop: ReadAsync from source stream, WriteAsync to DataStream
  |   FlushAsync()
  |   Return buffer to ArrayPool
  v
WriteLock.Release()
```

### Wire Format

Each message on the wire has this structure:

```
+--------------------------------------------------+
| JSON header (UTF-8 encoded, no pretty printing)   |
| {"len":N,"status":"Normal","syncreq":false,...}   |
+--------------------------------------------------+
| \r\n\r\n  (bytes: 13, 10, 13, 10)                |
+--------------------------------------------------+
| Raw data bytes (exactly N bytes)                  |
+--------------------------------------------------+
```

See [FRAMING.md](FRAMING.md) for the complete wire protocol specification and guidance on integrating non-WatsonTcp endpoints.

### Receiving a Message

The receiving side runs a `DataReceiver` background task that loops continuously:

```
DataReceiver loop:
  |
  v
MessageBuilder.BuildFromStream(dataStream)
  |   Read one byte at a time into MemoryStream accumulator
  |   Track last 4 bytes in a sliding window
  |   When \r\n\r\n detected: deserialize header JSON --> WatsonMessage
  |   Set msg.DataStream = the underlying TCP/SSL stream
  v
Process by MessageStatus:
  |
  +-- AuthRequired/AuthSuccess/AuthFailure --> authentication flow
  +-- ConnectionRejected --> explicit admission failure
  +-- HandshakeBegin/HandshakeData/HandshakeSuccess/HandshakeFailure --> framed pre-registration handshake
  +-- Shutdown/Removed/Timeout --> disconnect
  +-- RegisterClient --> GUID registration
  +-- Normal + SyncRequest --> invoke SyncRequestReceived callback, send response
  +-- Normal + SyncResponse --> resolve matching TaskCompletionSource
  +-- Normal --> read data, fire MessageReceived or StreamReceived event
```

### Stream vs. Byte Array Delivery

When `Events.MessageReceived` is set, the data receiver reads the full payload into a `byte[]` via `WatsonCommon.ReadMessageDataAsync()` before firing the event. When `Events.StreamReceived` is set instead, behavior depends on message size:

- **Large messages** (ContentLength >= `Settings.MaxProxiedStreamSize`): A `WatsonStream` wrapping the raw TCP/SSL stream is delivered synchronously. The event handler must consume the stream before returning, as the underlying connection stream advances.
- **Small messages**: Data is first copied into a `MemoryStream`, then wrapped in a `WatsonStream` and delivered asynchronously via `Task.Run`.

## 3. Component Diagram

```
+----------------------------------------------------------+
|                    WatsonTcpClient                        |
|  Settings, Events, Callbacks, Keepalive, SslConfiguration |
|  _WriteLock (SemaphoreSlim)                               |
|  _ReadLock (SemaphoreSlim)                                |
|  _DataReceiver (Task)                                     |
|  _IdleServerMonitor (Task)                                |
|  _SyncRequests (ConcurrentDictionary<Guid, TCS>)          |
|  _DataStream (NetworkStream or SslStream)                 |
+---------------------------+------------------------------+
                            |
                            | uses
                            v
+----------------------------------------------------------+
|                  WatsonMessageBuilder                     |
|  ConstructNew() --> WatsonMessage                         |
|  BuildFromStream() --> WatsonMessage (from wire)          |
|  GetHeaderBytes() --> byte[] (JSON + \r\n\r\n)            |
|  SerializationHelper, ReadStreamBuffer, MaxHeaderSize     |
+---------------------------+------------------------------+
                            |
                            | creates / parses
                            v
+----------------------------------------------------------+
|                     WatsonMessage                         |
|  ContentLength, Status, Metadata, SyncRequest,            |
|  SyncResponse, ConversationGuid, ExpirationUtc,           |
|  TimestampUtc, PresharedKey, SenderGuid, DataStream       |
+----------------------------------------------------------+

+----------------------------------------------------------+
|                    WatsonTcpServer                        |
|  Settings, Events, Callbacks, Keepalive, SslConfiguration |
|  _AcceptConnections (Task)                                |
|  _MonitorClients (Task)                                   |
|  _SyncRequests (ConcurrentDictionary<Guid, TCS>)          |
|  _Listener (TcpListener)                                  |
+---------------------------+------------------------------+
                            |
                            | manages clients via
                            v
+----------------------------------------------------------+
|                 ClientMetadataManager                     |
|  _Lock (ReaderWriterLockSlim)                             |
|  _Clients (Dictionary<Guid, ClientMetadata>)              |
|  _UnauthenticatedClients (Dictionary<Guid, DateTime>)     |
|  _ClientsLastSeen (Dictionary<Guid, DateTime>)            |
|  _ClientsKicked (Dictionary<Guid, DateTime>)              |
|  _ClientsTimedout (Dictionary<Guid, DateTime>)            |
+---------------------------+------------------------------+
                            |
                            | stores
                            v
+----------------------------------------------------------+
|                    ClientMetadata                         |
|  Guid, IpPort, Name, Metadata (user-defined)              |
|  TcpClient, NetworkStream, SslStream, DataStream          |
|  WriteLock (SemaphoreSlim), ReadLock (SemaphoreSlim)       |
|  TokenSource / Token (CancellationToken)                  |
|  DataReceiver (Task)                                      |
|  SendBuffer (byte[])                                      |
+----------------------------------------------------------+

+----------------------------------------------------------+
|                      WatsonStream                         |
|  Read-only Stream wrapper with length tracking            |
|  Wraps the underlying TCP/SSL stream or a MemoryStream    |
|  Tracks _Position and _BytesRemaining                     |
|  CanRead=true, CanSeek=false, CanWrite=false              |
+----------------------------------------------------------+

+----------------------------------------------------------+
|                     WatsonCommon                           |
|  DataStreamToMemoryStream() - buffered stream copy        |
|  ReadFromStreamAsync() - read N bytes from stream         |
|  ReadMessageDataAsync() - read message payload            |
|  BytesToStream() - byte[] to Stream + contentLength       |
|  GetExpirationTimestamp() - clock-skew adjusted expiration|
|  ByteArrayToHex(), AppendBytes() - utilities              |
+----------------------------------------------------------+
```

## 4. Client Architecture

### Connection Lifecycle

```
new WatsonTcpClient(ip, port)   // or SSL constructor variant
  |
  v
Connect() / ConnectAsync()
  |
  +-- Create TcpClient, set NoDelay
  +-- BeginConnect with timeout (ConnectTimeoutSeconds)
  +-- TCP mode: get NetworkStream as _DataStream
  +-- SSL mode: wrap in SslStream, AuthenticateAsClient, set _DataStream = _SslStream
  +-- Enable TCP keepalives if configured
  +-- Send RegisterClient message (MessageStatus.RegisterClient)
  +-- Start _DataReceiver task (DataReceiver loop)
  +-- Start _IdleServerMonitor task
  +-- Fire ServerConnected event
  |
  v
[Connected state - DataReceiver loop running]
  |
  v
Disconnect() / Dispose()
  +-- Send Shutdown message (optional)
  +-- Cancel TokenSource
  +-- Close SslStream, NetworkStream, TcpClient
  +-- Wait for DataReceiver and IdleServerMonitor tasks (up to 5s)
  +-- Set Connected = false
```

### Send Pipeline

Every send operation follows this pattern:

1. Acquire `_WriteLock` (SemaphoreSlim(1,1)) -- serializes all writes on the connection
2. Serialize the `WatsonMessage` to JSON, append `\r\n\r\n`, write header bytes to `_DataStream`
3. Flush the stream
4. Read from the source data stream in chunks using a buffer rented from `ArrayPool<byte>.Shared`, write each chunk to `_DataStream`
5. Flush the stream
6. Release `_WriteLock`

If a write fails with an exception, the client sets `Connected = false` and calls `Dispose()`.

### Synchronous Request/Response

The sync request/response mechanism uses `ConcurrentDictionary<Guid, TaskCompletionSource<SyncResponse>>`:

1. **Sender** creates a `TaskCompletionSource<SyncResponse>` and stores it in `_SyncRequests` keyed by the message's `ConversationGuid`
2. **Sender** sends the message with `SyncRequest = true` and an `ExpirationUtc`
3. **Sender** awaits `tcs.Task` with a timeout via linked `CancellationTokenSource`
4. **Receiver's DataReceiver** detects `msg.SyncRequest == true`, invokes the `SyncRequestReceived` callback, and sends back a response with `SyncResponse = true` and the same `ConversationGuid`
5. **Original sender's DataReceiver** detects `msg.SyncResponse == true`, looks up the `ConversationGuid` in `_SyncRequests`, and calls `tcs.TrySetResult()` to unblock the awaiting caller
6. If the timeout fires before a response arrives, the `CancellationTokenSource` cancels the TCS and a `TimeoutException` is thrown

Clock skew between sender and receiver is handled by `WatsonCommon.GetExpirationTimestamp()`, which adjusts the expiration time based on the difference between `DateTime.UtcNow` and `msg.TimestampUtc`.

## 5. Server Architecture

### Listener Lifecycle

```
new WatsonTcpServer(ip, port)   // or SSL constructor variant
  |
  v
Start()
  +-- Create TcpListener, start listening
  +-- Start _AcceptConnections task
  +-- Start _MonitorClients task (idle client monitoring)
  +-- Fire ServerStarted event
  |
  v
AcceptConnections loop:
  +-- Check MaxConnections (pause listener if at capacity with enforcement)
  +-- AcceptTcpClientAsync()
  +-- Validate against PermittedIPs / BlockedIPs
  +-- Create ClientMetadata from TcpClient
  +-- Add to ClientMetadataManager
  +-- Create linked CancellationToken (server token + client token)
  +-- TCP mode: Task.Run(FinalizeConnection)
  +-- SSL mode: Task.Run(StartTls then FinalizeConnection)
  |
  v
FinalizeConnection(client):
  +-- If PresharedKey configured: send AuthRequired, add to unauthenticated list
  +-- Start client.DataReceiver = Task.Run(DataReceiver)
  |
  v
DataReceiver(client) loop:
  +-- Check client connectivity (IsClientConnected)
  +-- BuildFromStream() to read next message
  +-- Handle authentication if client is unauthenticated
  +-- Handle RegisterClient (GUID replacement)
  +-- Handle sync request/response, normal messages, shutdown
  +-- Update ClientsLastSeen timestamp
  |
  v (on exit from DataReceiver)
  +-- Determine DisconnectReason (Removed, Timeout, Normal)
  +-- Fire ClientDisconnected event
  +-- Remove from ClientMetadataManager
  +-- Decrement _Connections counter
  +-- Dispose ClientMetadata
```

### Client Management

`ClientMetadataManager` maintains five parallel dictionaries, all protected by a single `ReaderWriterLockSlim`:

| Dictionary | Key | Value | Purpose |
|---|---|---|---|
| `_Clients` | `Guid` | `ClientMetadata` | Active client connections |
| `_UnauthenticatedClients` | `Guid` | `DateTime` | Clients awaiting PSK authentication |
| `_ClientsLastSeen` | `Guid` | `DateTime` | Timestamp of last message from each client |
| `_ClientsKicked` | `Guid` | `DateTime` | Clients disconnected by server (kicked) |
| `_ClientsTimedout` | `Guid` | `DateTime` | Clients disconnected due to idle timeout |

The kicked and timed-out dictionaries are used to determine the `DisconnectReason` when the `DataReceiver` loop exits. They are periodically purged by `PurgeStaleRecords()` (every ~60 seconds, records older than 5 minutes).

### Idle Monitoring

The `MonitorForIdleClients` task runs every 5 seconds:

1. If `IdleClientTimeoutSeconds > 0`, iterates all clients' last-seen timestamps
2. Clients whose last activity exceeds the threshold are marked as timed out and disconnected
3. Every 12 iterations (~60 seconds), calls `PurgeStaleRecords()` to clean up stale kicked/timed-out records

### Connection Count Management

The server tracks active connections with `Interlocked.Increment/Decrement` on `_Connections`. When `MaxConnections` is reached:
- If `EnforceMaxConnections` is true: the listener is stopped and restarted when connections drop below the limit
- If enforcement is disabled: connections are accepted with a warning log

## 6. Threading Model

### Client Threads

| Thread/Task | Purpose | Lifetime |
|---|---|---|
| Caller thread | `Connect()`, `SendAsync()`, `Disconnect()` | User-controlled |
| `_DataReceiver` | Background task reading from the TCP stream | Connect to Disconnect |
| `_IdleServerMonitor` | Polls for server idle timeout | Connect to Disconnect |
| Event handler tasks | `Task.Run()` for async event delivery | Per-message |

### Server Threads

| Thread/Task | Purpose | Lifetime |
|---|---|---|
| Caller thread | `Start()`, `Stop()`, `SendAsync()` | User-controlled |
| `_AcceptConnections` | Accepts incoming TCP connections | Start to Stop |
| `_MonitorClients` | Checks for idle clients every 5 seconds | Start to Stop |
| Per-client `DataReceiver` | One background task per connected client | Client connect to disconnect |
| Event handler tasks | `Task.Run()` for async event delivery | Per-message |
| SSL handshake tasks | `Task.Run()` for TLS negotiation per client | Brief, during connection setup |

### Locks

**`WriteLock` (SemaphoreSlim(1,1))** -- One per connection. On the client, `_WriteLock` is a field of `WatsonTcpClient`. On the server, each `ClientMetadata` has its own `WriteLock`. This serializes writes on each connection to prevent interleaving of header and data bytes from concurrent senders. The lock is held for the duration of both header and data transmission.

**`ReadLock` (SemaphoreSlim(1,1))** -- Present on both client and `ClientMetadata`. On the client side, the `DataReceiver` acquires `_ReadLock` before calling `BuildFromStream()`. This exists to prevent concurrent reads, though in practice only the single `DataReceiver` task reads from each connection.

**`ReaderWriterLockSlim` in `ClientMetadataManager`** -- A single lock protecting all five client dictionaries. Read operations (exists, get, count, list) acquire a read lock. Write operations (add, remove, update, replace, purge) acquire a write lock. This allows concurrent read access while serializing mutations. A single lock is used rather than per-dictionary locks to ensure atomicity of operations that touch multiple dictionaries (e.g., `Remove()` removes from all five, `ReplaceGuid()` updates all five).

### Cancellation Token Flow

Each `WatsonTcpClient` and `WatsonTcpServer` has a `CancellationTokenSource` (`_TokenSource`) created at startup. The token is passed to all background tasks. On the server, each `ClientMetadata` also has its own `CancellationTokenSource`, and a linked token source (`CancellationTokenSource.CreateLinkedTokenSource`) is created combining the server's token with the client's token. This means cancelling either the server or the individual client will stop the client's `DataReceiver`.

Disposal triggers cancellation: `Disconnect()` cancels `_TokenSource`, and `ClientMetadata.Dispose()` cancels its own `TokenSource`.

## 7. SSL/TLS

SSL/TLS is implemented by layering `SslStream` on top of `NetworkStream`:

### Client SSL

```
TcpClient.GetStream() --> NetworkStream
  |
  v
new SslStream(NetworkStream, leaveInnerStreamOpen: false, validationCallback)
  |
  v
SslStream.AuthenticateAsClient(serverHostname, clientCerts, sslProtocols, checkRevocation)
  |
  v
_DataStream = _SslStream   // all reads/writes go through SslStream
```

When `Settings.AcceptInvalidCertificates` is true, a custom `ServerCertificateValidationCallback` from `SslConfiguration` is used (which accepts all certificates). When false, the default validation applies.

### Server SSL

```
ClientMetadata.NetworkStream (from TcpClient.GetStream())
  |
  v
new SslStream(NetworkStream, leaveInnerStreamOpen: false, validationCallback)
  |
  v
SslStream.AuthenticateAsServerAsync(serverCert, clientCertRequired, sslProtocols, checkRevocation)
  |
  v
client.DataStream = client.SslStream   // automatically set by the SslStream property setter
```

The server performs TLS negotiation in a separate `Task.Run()` via `StartTls()`. If negotiation fails (stream not encrypted, not authenticated, or mutual auth fails), the client is disposed and the connection count decremented.

Both client and server support mutual authentication via `Settings.MutuallyAuthenticate`. The TLS version is configurable (defaults to TLS 1.2).

## 8. Authentication

WatsonTcp supports optional preshared key (PSK) authentication. The flow:

```
Server                                  Client
  |                                       |
  |  [client connects]                    |
  |                                       |
  |-- AuthRequired ---------------------->|
  |   (MessageStatus.AuthRequired)        |
  |                                       |-- check Settings.PresharedKey
  |                                       |   or invoke Callbacks.AuthenticationRequested
  |<-- AuthRequested --------------------|
  |   (MessageStatus.AuthRequested,       |
  |    PresharedKey = 16-byte key)        |
  |                                       |
  |-- [compare PSK]                       |
  |                                       |
  |   [match]                             |
  |-- AuthSuccess ----------------------->|
  |   (MessageStatus.AuthSuccess)         |-- fire AuthenticationSucceeded event
  |   fire AuthenticationSucceeded event  |-- send RegisterClient message
  |                                       |
  |   [no match]                          |
  |-- AuthFailure ----------------------->|
  |   (MessageStatus.AuthFailure)         |-- fire AuthenticationFailed event
  |   fire AuthenticationFailed event     |-- disconnect
  |   disconnect client                   |
```

Key details:
- The preshared key must be exactly 16 bytes
- While unauthenticated, the server adds the client to `_UnauthenticatedClients` and ignores any non-auth messages
- On authentication success, the server removes the client from `_UnauthenticatedClients`
- The client re-sends the `RegisterClient` message after successful authentication, since the initial one sent during `Connect()` was ignored while unauthenticated

## 9. Configuration

### Settings Classes

**`WatsonTcpClientSettings`**: `Guid` (client identifier), `ConnectTimeoutSeconds`, `IdleServerTimeoutMs`, `IdleServerEvaluationIntervalMs`, `StreamBufferSize`, `MaxProxiedStreamSize`, `MaxHeaderSize`, `NoDelay`, `DebugMessages`, `Logger`, `LocalPort`, `PresharedKey`, `AcceptInvalidCertificates`, `MutuallyAuthenticate`

**`WatsonTcpServerSettings`**: `MaxConnections`, `EnforceMaxConnections`, `IdleClientTimeoutSeconds`, `StreamBufferSize`, `MaxProxiedStreamSize`, `MaxHeaderSize`, `NoDelay`, `DebugMessages`, `Logger`, `PermittedIPs`, `BlockedIPs`, `PresharedKey`, `AcceptInvalidCertificates`, `MutuallyAuthenticate`

### Events Classes

**`WatsonTcpClientEvents`**: `ServerConnected`, `ServerDisconnected`, `MessageReceived`, `StreamReceived`, `ExceptionEncountered`, `AuthenticationSucceeded`, `AuthenticationFailure`

**`WatsonTcpServerEvents`**: `ClientConnected`, `ClientDisconnected`, `MessageReceived`, `StreamReceived`, `ExceptionEncountered`, `ServerStarted`, `ServerStopped`, `AuthenticationSucceeded`, `AuthenticationFailed`

Only one of `MessageReceived` or `StreamReceived` should be set. `MessageReceived` takes precedence if both are set.

### Callbacks Classes

**`WatsonTcpClientCallbacks`**: `AuthenticationRequested` (returns PSK string), `SyncRequestReceived` / `SyncRequestReceivedAsync` (handles sync requests from the server)

**`WatsonTcpServerCallbacks`**: `SyncRequestReceived` / `SyncRequestReceivedAsync` (handles sync requests from clients)

### Keepalive Settings

**`WatsonTcpKeepaliveSettings`**: `EnableTcpKeepAlives`, `TcpKeepAliveTime`, `TcpKeepAliveInterval`, `TcpKeepAliveRetryCount`

TCP keepalives are configured at the socket level. On .NET 6.0+, socket options are set directly. On .NET Framework, `IOControl` with `KeepAliveValues` is used. Keepalives are not available on .NET Standard.

### SSL Configuration

**`WatsonTcpClientSslConfiguration`**: `ServerCertificateValidationCallback`, `ClientCertificateSelectionCallback`

**`WatsonTcpServerSslConfiguration`**: `ClientCertificateValidationCallback`, `ClientCertificateRequired`

## 10. Key Design Decisions

### Byte-by-Byte Header Reading

`WatsonMessageBuilder.BuildFromStream()` reads the header one byte at a time, tracking the last four bytes in a sliding window to detect the `\r\n\r\n` delimiter. This is deliberate: `NetworkStream` (and `SslStream`) do not support seeking. If a buffered read were used, the read could consume bytes beyond the header delimiter and into the message body. Since the header terminates at `\r\n\r\n` and the body length is only known after parsing the header, byte-by-byte reading is the safest approach to avoid over-reading. The implementation uses a `MemoryStream` accumulator rather than array concatenation to avoid O(n^2) allocation overhead.

### ArrayPool for Send Buffers

`SendDataStreamAsync()` rents buffers from `ArrayPool<byte>.Shared` rather than allocating new arrays for each send. This reduces GC pressure during high-throughput scenarios, as send buffers are frequently allocated and released. The buffer is rented at `StreamBufferSize` (default 65536 bytes) and returned in a `finally` block to ensure no leaks.

### Single Lock in ClientMetadataManager

All five dictionaries in `ClientMetadataManager` are protected by a single `ReaderWriterLockSlim` rather than individual locks. This is a deliberate trade-off:

- **Atomicity**: Operations like `Remove(guid)` must remove entries from all five dictionaries atomically. With separate locks, partial removal could leave inconsistent state.
- **Simplicity**: `ReplaceGuid()` (used during client GUID registration) must update entries across all dictionaries in a single atomic operation.
- **Acceptable contention**: The lock is held for short durations (dictionary lookups/mutations), and `ReaderWriterLockSlim` allows concurrent reads. The server's message processing is per-client in separate tasks, so the manager lock is only contended during client lifecycle events (connect, disconnect, timeout checks), not during per-message processing.

### WriteLock Per Connection

Each connection has its own `SemaphoreSlim(1,1)` for write serialization. On the client, this is `_WriteLock` on the `WatsonTcpClient` instance. On the server, each `ClientMetadata` has its own `WriteLock`. This design ensures:

- Header and data bytes for a single message are never interleaved with another message's bytes
- Multiple threads can send to different clients concurrently (server-side) without contention
- The semaphore is async-compatible (`WaitAsync`), avoiding thread pool starvation

### Message Status as Control Plane

`WatsonMessage.Status` serves as an in-band control plane. The `MessageStatus` enum includes values like `Normal`, `Shutdown`, `Removed`, `Timeout`, `AuthRequired`, `AuthRequested`, `AuthSuccess`, `AuthFailure`, and `RegisterClient`. This allows connection lifecycle management (authentication, registration, graceful shutdown) to use the same framing protocol as data messages, avoiding the need for a separate control channel.

### WatsonStream as Bounded Stream Wrapper

`WatsonStream` wraps the raw TCP/SSL stream but tracks `_Position` and `_BytesRemaining` based on the declared `ContentLength`. This prevents the consumer from reading beyond the current message's data into the next message's header. The stream is read-only, non-seekable, and non-writable, reflecting its role as a view over a specific segment of the underlying transport stream.
