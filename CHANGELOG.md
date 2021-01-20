# Change Log

## Current Version

v4.8.0

- Breaking change; log messages now include a ```Severity``` parameter
- TCP keepalives moved to the socket instead of the listener

## Previous Versions

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
