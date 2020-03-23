# Change Log

## Current Version

v3.1.1

- APIs to support sending async or sync (send-and-wait) messages with a metadata dictionary and no data
- Better handling of null input when sending data

## Previous Versions

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
