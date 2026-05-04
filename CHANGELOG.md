# Change Log

## Current Version

v6.3.0

### Async Stream Receive

- Added `WatsonTcpServerCallbacks.StreamReceivedAsync` and `WatsonTcpClientCallbacks.StreamReceivedAsync`
- Added awaited large-stream consumption support for payloads at or above `MaxProxiedStreamSize`
- Added unread-byte drain handling after stream callbacks and stream events return so the next frame stays aligned
- Added receive-mode precedence warnings through `Settings.Logger`

### Stream Lifecycle

- `MessageReceived` now takes precedence over both stream receive modes
- `StreamReceivedAsync` now takes precedence over legacy `StreamReceived`
- Large proxied streams can now be consumed safely with `await` without relying on `async void` event handlers

### Testing

- Expanded `Test.Shared` with shared client/server stream coverage for:
  - precedence and configuration validation
  - small, large, and exact-threshold payloads
  - partial-read remainder drain behavior
  - callback exception handling
  - sync stream regression coverage
- Added a dedicated `streaming` Touchstone suite while keeping the same cases in regression

### Compatibility Notes

- This is a minor release because the new async stream path is opt-in
- Legacy `Events.StreamReceived` remains supported

## Previous Version

v6.2.0

### Connection Admission

- Added `WatsonTcpServerCallbacks.AuthorizeConnectionAsync` for server-side admission decisions before a client is treated as connected
- Added `Connections` vs `PendingConnections` visibility and moved `ListClients()` semantics to fully admitted, registered clients only
- Added `ConnectionRejected` events and `ConnectionRejectedException` for explicit rejection handling

### Handshake State Machines

- Added framed handshake callbacks via `WatsonTcpServerCallbacks.HandshakeAsync` and `WatsonTcpClientCallbacks.HandshakeAsync`
- Added `ServerHandshakeSession`, `ClientHandshakeSession`, `HandshakeMessage`, and `HandshakeResult`
- Added control-plane statuses for handshake begin/data/success/failure
- Added handshake success/failure events and `HandshakeFailedException`

### Settings And Lifecycle

- Added `WatsonTcpServerSettings.AuthorizationTimeoutMs`
- Added `WatsonTcpServerSettings.HandshakeTimeoutMs`
- Added `WatsonTcpClientSettings.HandshakeTimeoutMs`
- Added pending-connection tracking and moved active registration to the end of admission

### Testing

- Migrated automated coverage into `Test.Shared` using Touchstone descriptors
- Rewrote `Test.Automated` as a Touchstone CLI host
- Rewrote `Test.XUnit` as a thin Touchstone xUnit host
- Added `Test.Nunit` as a thin Touchstone NUnit host
- Added shared authorization and API-key handshake success/failure coverage exercised through all three hosts

### Compatibility Notes

- This is a minor release because all new admission and handshake behavior is opt-in
- Handshake-enabled servers require compatible clients that understand the new control-plane statuses
- `Test.Automated` now targets `net8.0;net10.0`

## Earlier Version

v6.1.0

### Performance

- **Header parsing rewrite** - The old ```BuildFromStream``` read one byte at a time, allocated a new array on every byte via ```AppendBytes```, and ran LINQ ```.Skip().Take().ToArray()``` on every iteration to check for the ```\r\n\r\n``` delimiter.  For a header of length H, that produced H-24 array allocations plus H-24 LINQ allocations.  The new version writes into a ```MemoryStream``` and tracks the last 4 bytes with simple variables.  Same byte-by-byte read (necessary to avoid over-reading past the delimiter into message body data on non-seekable ```NetworkStream```s), but zero per-byte allocations.
- **Buffer pooling** - Both client and server ```SendDataStreamAsync``` methods were allocating ```new byte[bufferSize]``` on every loop iteration of every send.  Now they rent from ```ArrayPool<byte>.Shared``` once per send and return when done.  ```GetHeaderBytes``` also replaced ```AppendBytes``` with ```Buffer.BlockCopy```.

### Thread Safety

- **ClientMetadataManager consolidated** - Had 5 separate ```ReaderWriterLockSlim``` instances protecting 5 dictionaries that represent one logical unit of client state.  Operations like ```ReplaceGuid``` and ```Remove``` acquired/released each lock individually, creating windows where a client existed in one dictionary but not another.  Now uses a single lock; all mutations are atomic.  Also fixed ```GetClient``` which did ```ContainsKey``` then ```[guid]``` across two separate lock acquisitions (TOCTOU race leading to ```KeyNotFoundException```).  Now uses ```TryGetValue```.
- **Sync response matching** - Both client and server used ```AutoResetEvent``` + multicast event handler subscription (```_SyncResponseReceived += handler```) with a ```lock``` around invocation.  Race conditions existed between handler registration and message send, and between concurrent sync requests.  Replaced with ```ConcurrentDictionary<Guid, TaskCompletionSource<SyncResponse>>```: register before sending, ```DataReceiver``` does ```TryRemove``` + ```TrySetResult``` on response arrival.  Cleaner, lock-free, no signal loss.

### Bug Fixes

- **WaitHandle leak** - ```WatsonTcpClient.Connect()``` called ```BeginConnect```, got a ```WaitHandle```, but never closed it (commented out with a link to an MSDN forum post).  Now closed in ```finally```.
- **Busy-wait spin loops** - ```ClientMetadata.Dispose()``` had ```while (DataReceiver?.Status == Running) Task.Delay(30).Wait()``` which blocks a thread pool thread spinning.  Same pattern in ```WatsonTcpClient.Disconnect()``` for both ```_DataReceiver``` and ```_IdleServerMonitor```.  Replaced with ```Task.Wait(TimeSpan.FromSeconds(5))```.
- **Stale client records** - ```_ClientsKicked``` and ```_ClientsTimedout``` dictionaries accumulated entries forever.  Added ```PurgeStaleRecords(TimeSpan)``` to ```ClientMetadataManager```, called every ~60 seconds from ```MonitorForIdleClients```, purging records older than 5 minutes that don't correspond to active clients.

### New Features

- **```Settings.MaxHeaderSize```** (client and server, default 262144/256KB) - The old header parser had no upper bound.  A malformed or malicious peer could send megabytes without a ```\r\n\r\n``` delimiter and the server would allocate until OOM.  Now throws ```IOException``` when exceeded.
- **```Settings.EnforceMaxConnections```** (server, default ```true```) - Previously, ```MaxConnections``` only paused the listener (stopped accepting) but the check happened after the connection was already accepted, so it was really just a soft warning.  Now, when enforcement is on, connections are actively rejected with ```tcpClient.Close()``` before any client state is created.  When off, the old behavior is preserved (accept anyway, log a warning, don't stop the listener).

### Observability

- Added ```Severity.Debug``` logging to all previously silent ```catch (TaskCanceledException) { }``` and ```catch (OperationCanceledException) { }``` blocks in ```IdleServerMonitor```, ```MonitorForIdleClients```, and ```SendInternalAsync``` on both client and server.

### Testing

- 10 new automated tests (46 total): MaxConnections enforcement (happy + sad), MaxHeaderSize validation, rapid connect/disconnect (10 cycles), concurrent sync requests (5 parallel ```SendAndWaitAsync``` with response verification), SSL connectivity + message exchange, server stop detection from client, duplicate client GUID handling, and send-with-byte-offset.

### Breaking Changes

- ```Settings.EnforceMaxConnections``` defaults to ```true```.  If you were relying on the server accepting unlimited connections despite ```MaxConnections``` being set, connections will now be rejected at capacity.  Set ```EnforceMaxConnections = false``` to restore the old behavior.
- All other changes are internal implementation details with identical public API signatures and wire protocol.

## Previous Versions

v6.0.x

- Remove unsupported frameworks
- Async version of ```SyncMessageReceived``` callback
- Moving usings inside namespace
- Remove obsolete methods
- Mark non-async APIs obsolete
- Modified test projects to use async
- Ensured background tasks honored cancellation tokens

v5.1.x

- Strong name signing
- Better exception logging
- Set ```Settings.NoDelay``` to ```true``` by default (disabling Nagle's algorithm)

v5.0.x

- Breaking changes
- Migrate from using ```IpPort``` as a client key to using ```Guid```
- Removal of ```Newtonsoft.Json``` as a dependency
- Separate ```WatsonMessageBuilder``` class to reduce code bloat
- ```ClientMetadata``` now includes ```Guid```
- ```ListClients``` now returns list of ```ClientMetadata``` instead of list of ```IpPort```
- Mark ```Send*``` methods that use ```ipPort``` as obsolete (pending removal in future release)
- Restrict message metadata dictionary to ```<string, object>```
- Targeting for .NET 7.0

v4.8.11

- TLS extensions, thank you @cee-sharp

v4.8.10

- Bugfix, authentication failure now disconnects clients and propagates the correct reason (thank you @Jyck)

v4.8.9

- Added optional parameter ```offset``` to ```Send``` and ```SendAsync``` methods that use ```byte[]``` data (thank you @pha3z)

v4.8.8

- Move listener start into ```Start()``` method (thank you @avoitenko)

v4.8.7

- Bugfix, timeout values for .NET Framework now properly handled as milliseconds (thank you @zsolt777)

v4.8.6

- Specify the client port by setting ```Settings.LocalPort``` (0, 1024-65535 are valid, where 0 is auto-assigned)

v4.8.0

- Breaking change; log messages now include a ```Severity``` parameter
- TCP keepalives moved to the socket instead of the listener

v4.7.1

- Breaking change; TCP keepalives now disabled by default due to incompatibility and problems on some platforms

v4.7.0

- Breaking changes
- Consolidated connection/disconnection event arguments
- Consolidated message/stream received event arguments
- Aligned disconnection reason with message status

v4.6.0.0

- More changes based on suggestions from @syntacs and @MartyIX
- Consolidated ```Send``` constructors with optional params to reduce complexity
- Optional ```CancellationToken``` parameters for async ```Send``` methods
- Use of ```ConfigureAwait``` for better reliability

v4.5.0.1

- Excellent changes and recommendations led by @syntacs for reliability
- Better coordination between Dispose and server Stop and client Disconnect
- Exception handling in server and client event handlers as well as callbacks

v4.4.0

- Breaking changes; header name fields have been reduced
- Performance improvements
- Elimination of sending unnecessary headers
- Thank you @broms95!

v4.3.0

- Breaking changes
- Retarget to include .NET Core 3.1 (previously .NET Framework 4.6.1 and .NET Standard 2.1 only)
- Added support for TCP keepalives for .NET Framework and .NET Core (.NET Standard does not have such facilities)
- Consolidated settings into separate classes

v4.2.0

- Breaking changes
- Introduced ```WatsonStream``` class to prevent stream consumers from reading into the next message's header
- ```MaxProxiedStreamSize``` property to dictate whether data is sent to ```StreamReceived``` in a new ```MemoryStream``` or the underlying data stream is sent
- Minor refactor and removal of compression

v4.1.12

- Fix for ClientMetadata.Dispose

v4.1.11

- Fix to order of ServerConnected and starting DataReceiver in WatsonTcpClient (thank you @ozrecsec)

v4.1.10

- Minor fixes to synchronous message expiration (thank you @karstennilsen)

v4.1.9

- Fix for being unable to disconnect a client from ClientConnected (thank you @motridox)

v4.1.8

- Fix for message expiration (thank you @karstennilsen)

v4.1.7

- AuthenticationRequested, AuthenticationSucceeded, and AuthenticationFailed events in WatsonTcpServer

v4.1.6

- Added SenderTimestamp to sync messages and derived expiration based on difference in sender vs receiver perception of time (thank you @karstennilsen)

v4.1.5

- Fix for synchronous request timeout leaving message data in the underlying stream (thank you @ozrecsec!)

v4.1.4

- Minor internal refactor

v4.1.3

- Fix for issue: compression with SSL enabled causes deserialization exceptions; not recommended for use
- Minor refactor

v4.1.2

- New constructor for SSL, taking certificate as parameter (thank you @NormenSchwettmann)
- **Known issue**: compression with SSL enabled causes deserialization exceptions; not recommended for use

v4.1.1

- Bugfix for disconnect scenarios causing the next message headers to be read as part of the prior message
- **Known issue**: compression with SSL enabled causes deserialization exceptions; not recommended for use

v4.1.0

- Compression of message data using either GZip or Deflate (thanks @developervariety!)
- Message data is now a property that fully reads the underlying stream
- Internal code refactoring to better follow DRY principles (SendHeaders, SendDataStream, etc)
- Reduce log verbosity on disconnect

v4.0.2

- Bugfix (thank you @ozrecsec!) for ClientDisconnected firing too early

v4.0.1

- Bugfixes (thank you @ozrecsec!) for DateTime serialization

v4.0.0

- Overhaul to internal framing, refer to ```FRAMING.md```
- Fixes to ```Test.Throughput``` projects (incorrectly reporting statistics)

v3.1.4

- Better handling for cases where no message/stream event handler is set

v3.1.3

- Fix synchronous messaging expiration bug

v3.1.2

- Fix DateTime string format

v3.1.1

- APIs to support sending async or sync (send-and-wait) messages with a metadata dictionary and no data
- Better handling of null input when sending data

v3.1.0

- Added support for synchronous messaging, i.e. send and wait for a response (see ```SendAndWait``` methods) with timeouts.  See the updated examples below or refer to the ```Test.Client``` and ```Test.Server``` project for examples
- Consolidated Logger for client, server, and messages
- ```Debug``` is now ```DebugMessages```
- Minor internal refactor

v3.0.3

- Now supports serialized metadata sizes (i.e. calculated after serialization of your dictionary) of up to 99,999,999 bytes

v3.0.2

- ```.Data``` property in both ```StreamReceivedFromClientEventArgs``` and ```StreamReceivedFromServerEventArgs```.

v3.0.1

- Bugfix in pre-shared key authentication

v3.0.0

- Breaking changes; move from Func-based callbacks to Event
- Added MaxConnections and Connection values in WatsonTcpServer

v2.2.2

- Added Statistics object.

v2.2.1

- Added Logger method to both WatsonTcpServer and WatsonTcpClient (thanks @crushedice)

v2.2.0

- Add support for sending and receiving messages with metadata ```Dictionary<object, object>```
- New callbacks for receiving messages with metadata: MessageReceivedWithMetadata and StreamReceivedWithMetadata  - 
- New callbacks for sending messages with metadata (overloads on existing methods added)
- Now dependent upon Newtonsoft.Json as metadata must be serialized; only serializable types are supported in metadata

v2.1.7

- Add support for Send(string) and SendAsync(string) 

v2.1.6

- ListClients now returns IEnumerable<string> (thanks @pha3z!)

v2.1.5

- Fix for larger message cases (thanks @mikkleini!)

v2.1.4

- Minor breaking change; ClientDisconnect now includes DisconnectReason to differentiate between normal, kicked, or timeout disconnections

v2.1.3

- Fix for ClientMetadata dispose (too many extranneous Dispose calls)
- TestThroughput project

v2.1.2

- Client timeout now only reset upon receiving a message from a client, and no longer reset when sending a message to a client

v2.1.1

- Automatically disconnect idle clients by setting ```WatsonTcpServer.IdleClientTimeoutSeconds``` to a positive integer (excellent suggestion, @pha3z!)

v2.1.0

- Breaking changes
- Better documentation on StreamReceived vs MessageReceived in the XML documentation and in the README
- Modified getters and setters on StreamReceived and MessageReceived to make them mutually exclusive
- Removal of (now unnecessary) ReadDataStream parameter
- ReadStreamBufferSize is now renamed to StreamBufferSize

v2.0.8

- StartAsync() method for client and server

v2.0.x

- Changed .NET Framework minimum requirement to 4.6.1 to support use of ```TcpClient.Dispose```
- Better disconnect handling and support (thank you to @mikkleini)
- Async Task-based callbacks
- Configurable connect timeout in WatsonTcpClient
- Clients can now connect via SSL without a certificate
- Big thanks to @MrMikeJJ for his extensive commits and pull requests
- Bugfix for graceful disconnect through dispose (thank you @mikkleini!)
 
v1.3.x
- Numerous fixes to authentication using preshared keys
- Authentication callbacks in the client to handle authentication events
  - ```AuthenticationRequested``` - authentication requested by the server, return the preshared key string (16 bytes)
  - ```AuthenticationSucceeded``` - authentication has succeeded, return true
  - ```AuthenticationFailure``` - authentication has failed, return true
- Support for sending and receiving larger messages by using streams instead of byte arrays
- Refer to ```TestServerStream``` and ```TestClientStream``` for a reference implementation.  You must set ```client.ReadDataStream = false``` and ```server.ReadDataStream = false``` and use the ```StreamReceived``` callback instead of ```MessageReceived```

v1.2.x
- Breaking changes for assigning callbacks, various server/client class variables, and starting them
- Consolidated SSL and non-SSL clients and servers into single classes for each
- Retargeted test projects to both .NET Core and .NET Framework
- Added more extensible framing support to later carry more metadata as needed
- Added authentication via pre-shared key (set Server.PresharedKey class variable, and use Client.Authenticate() method)

v1.1.x
- Re-targeted to both .NET Core 2.0 and .NET Framework 4.5.2
- Various bugfixes

v1.0.x
- Initial release
- Async support and IDisposable support
- IP filtering/permitted IP addresses support
- Improved disconnect detection
- SSL support
