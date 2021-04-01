![alt tag](https://github.com/jchristn/watsontcp/blob/master/assets/watson.ico)

# WatsonTcp

[![NuGet Version](https://img.shields.io/nuget/v/WatsonTcp.svg?style=flat)](https://www.nuget.org/packages/WatsonTcp/) [![NuGet](https://img.shields.io/nuget/dt/WatsonTcp.svg)](https://www.nuget.org/packages/WatsonTcp) 

WatsonTcp is the fastest, easiest, most efficient way to build TCP-based clients and servers in C# with integrated framing, reliable transmission, and fast disconnect detection.

**IMPORTANT** WatsonTcp provides framing to ensure message-level delivery which also dictates that you must either 1) use WatsonTcp for both the server and the client, or, 2) ensure that your client/server exchange messages with the WatsonTcp node using WatsonTcp's framing.  Refer to ```FRAMING.md``` for a reference on WatsonTcp message structure.

## New in v4.8.0

- Breaking change; log messages now include a ```Severity``` parameter
- TCP keepalives moved to the socket instead of the listener

## Test Applications

Test projects for both client and server are included which will help you understand and exercise the class library.

## SSL

WatsonTcp supports data exchange with or without SSL.  The server and client classes include constructors that allow you to include fields for the PFX certificate file and password.  An example certificate can be found in the test projects, which has a password of 'password'.

## To Stream or Not To Stream...

WatsonTcp allows you to receive messages using either byte arrays or streams.  Set ```Events.MessageReceived``` if you wish to consume a byte array, or, set ```Events.StreamReceived``` if you wish to consume a stream. 

It is important to note the following:

- When using ```Events.MessageReceived```
  - The message payload is read from the stream and sent to your application
  - The event is fired asynchronously and Watson can continue reading messages while your application processes
- When using ```Events.StreamReceived```
  - If the message payload is smaller than ```Settings.MaxProxiedStreamSize```, the data is read into a ```MemoryStream``` and sent to your application asynchronously
  - If the message payload is larger than ```Settings.MaxProxiedStreamSize```, the underlying data stream is sent to your application synchronously, and WatsonTcp will wait until your application responds before continuing to read
- Only one of ```Events.MessageReceived``` and ```Events.StreamReceived``` should be set; ```Events.MessageReceived``` will be used if both are set

## Including Metadata with a Message

Should you with to include metadata with any message, use the ```Send``` or ```SendAsync``` method that allows you to pass in metadata (```Dictionary<object, object>```).  Refer to the ```TestClient```, ```TestServer```, ```TestClientStream```, and ```TestServerStream``` projects for a full example.
 
Note: if you use a class instance as either the key or value, you'll need to deserialize on the receiving end from JSON.  
```
object myVal = args.Metadata["myKey"];
MyClass instance = myVal.ToObject<MyClass>();
```

This is not necessary if you are using simple types (int, string, etc).  Simply cast to the simple type.

### Local vs External Connections

**IMPORTANT**
* If you specify ```127.0.0.1``` as the listener IP address in WatsonTcpServer, it will only be able to accept connections from within the local host.  
* To accept connections from other machines:
  * Use a specific interface IP address, or
  * Use ```null```, ```*```, ```+```, or ```0.0.0.0``` for the listener IP address (requires admin privileges to listen on any IP address)
* Make sure you create a permit rule on your firewall to allow inbound connections on that port
* If you use a port number under 1024, admin privileges will be required

## Running under Mono

.NET Core should always be the preferred option for multi-platform deployments.  However, WatsonTcp works well in Mono environments with the .NET Framework to the extent that we have tested it. It is recommended that when running under Mono, you execute the containing EXE using --server and after using the Mono Ahead-of-Time Compiler (AOT).  Note that TLS 1.2 is hard-coded, which may need to be downgraded to TLS in Mono environments.

NOTE: Windows accepts '0.0.0.0' as an IP address representing any interface.  On Mac and Linux you must be specified ('127.0.0.1' is also acceptable, but '0.0.0.0' is NOT).
```
mono --aot=nrgctx-trampolines=8096,nimt-trampolines=8096,ntrampolines=4048 --server myapp.exe
mono --server myapp.exe
```

## Contributions

Special thanks to the following people for their support and contributions to this project!

@brudo @MrMikeJJ @mikkleini @pha3z @crushedice @marek-petak @ozrecsec @developervariety 
@NormenSchwettmann @karstennilsen @motridox @AdamFrisby @Job79 @Dijkstra-ru @playingoDEERUX
@DuAell @syntacs @zsolt777 @broms95 @Antwns @MartyIX @Jyck @Memphizzz

If you'd like to contribute, please jump right into the source code and create a pull request, or, file an issue with your enhancement request. 

## Examples

The following examples show a simple client and server example using WatsonTcp without SSL and consuming messages using byte arrays instead of streams.  For full examples, please refer to the ```Test.*``` projects.  

### Server
```csharp
using WatsonTcp;

static void Main(string[] args)
{
    WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", 9000);
    server.Events.ClientConnected += ClientConnected;
    server.Events.ClientDisconnected += ClientDisconnected;
    server.Events.MessageReceived += MessageReceived; 
    server.Callbacks.SyncRequestReceived = SyncRequestReceived;
    server.Start();

    // list clients
    IEnumerable<string> clients = server.ListClients();

    // send a message
    server.Send("[IP:port]", "Hello, client!");

    // send a message with metadata
    Dictionary<object, object> md = new Dictionary<object, object>();
    md.Add("foo", "bar");
    server.Send("[IP:port]", "Hello, client!  Here's some metadata!", md);

    // send async!
    await server.SendAsync("[IP:port", "Hello, client!  I'm async!");

    // send and wait for a response
    try
    {
        SyncResponse resp = server.SendAndWait("[IP:port", 5000, "Hey, say hello back within 5 seconds!");
        Console.WriteLine("My friend says: " + Encoding.UTF8.GetString(resp.Data));
    }
    catch (TimeoutException)
    {
        Console.WriteLine("Too slow...");
    } 
}

static void ClientConnected(object sender, ConnectionEventArgs args)
{
    Console.WriteLine("Client connected: " + args.IpPort);
}

static void ClientDisconnected(object sender, DisconnectionEventArgs args)
{
    Console.WriteLine("Client disconnected: " + args.IpPort + ": " + args.Reason.ToString());
}

static void MessageReceived(object sender, MessageReceivedEventArgs args)
{
    Console.WriteLine("Message from " + args.IpPort + ": " + Encoding.UTF8.GetString(args.Data));
}

static SyncResponse SyncRequestReceived(SyncRequest req)
{
    return new SyncResponse("Hello back at you!");
}
```
 
### Client 
```csharp
using WatsonTcp;

static void Main(string[] args)
{
    WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 9000);
    client.Events.ServerConnected += ServerConnected;
    client.Events.ServerDisconnected += ServerDisconnected;
    client.Events.MessageReceived += MessageReceived; 
    client.Callbacks.SyncRequestReceived = SyncRequestReceived;
    client.Connect();

    // check connectivity
    Console.WriteLine("Am I connected?  " + client.Connected);

    // send a message
    client.Send("Hello!");

    // send a message with metadata
    Dictionary<object, object> md = new Dictionary<object, object>();
    md.Add("foo", "bar");
    client.Send("Hello, client!  Here's some metadata!", md);

    // send async!
    await client.SendAsync("Hello, client!  I'm async!");

    // send and wait for a response
    try
    {
        SyncResponse resp = client.SendAndWait(5000, "Hey, say hello back within 5 seconds!");
        Console.WriteLine("My friend says: " + Encoding.UTF8.GetString(resp.Data));
    }
    catch (TimeoutException)
    {
        Console.WriteLine("Too slow...");
    }  
}

static void MessageReceived(object sender, MessageReceivedEventArgs args)
{
    Console.WriteLine("Message from " + args.IpPort + ": " + Encoding.UTF8.GetString(args.Data));
}

static void ServerConnected(object sender, EventArgs args)
{
    Console.WriteLine("Server " + args.IpPort + " connected");
}

static void ServerDisconnected(object sender, EventArgs args)
{
    Console.WriteLine("Server " + args.IpPort + " disconnected");
}

static SyncResponse SyncRequestReceived(SyncRequest req)
{
    return new SyncResponse("Hello back at you!");
}
```

## Example with SSL

The examples above can be modified to use SSL as follows.  No other changes are needed.  Ensure that the certificate is exported as a PFX file and is resident in the directory of execution.
```csharp
// server
WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", 9000, "test.pfx", "password"); 
server.Settings.AcceptInvalidCertificates = true;
server.Settings.MutuallyAuthenticate = true;
server.Start();

// client
WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 9000, "test.pfx", "password"); 
client.Settings.AcceptInvalidCertificates = true;
client.Settings.MutuallyAuthenticate = true;
client.Connect();
```

## Example with Streams

Refer to the ```Test.ClientStream``` and ```Test.ServerStream``` projects for a full example.  
```csharp
// server
WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", 9000);
server.Events.ClientConnected += ClientConnected;
server.Events.ClientDisconnected += ClientDisconnected;
server.Events.StreamReceived += StreamReceived; 
server.Start();

static void StreamReceived(object sender, StreamReceivedEventArgs args)
{
    long bytesRemaining = args.ContentLength;
    int bytesRead = 0;
    byte[] buffer = new byte[65536];

    using (MemoryStream ms = new MemoryStream())
    {
        while (bytesRemaining > 0)
        {
            bytesRead = args.DataStream.Read(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                ms.Write(buffer, 0, bytesRead);
                bytesRemaining -= bytesRead;
            }
        }
    }

    Console.WriteLine("Stream received from " + args.IpPort + ": " + Encoding.UTF8.GetString(ms.ToArray())); 
}

// client
WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 9000);
client.Events.ServerConnected += ServerConnected;
client.Events.ServerDisconnected += ServerDisconnected;
client.Events.StreamReceived += StreamReceived; 
client.Connect();

static void StreamReceived(object sender, StreamReceivedEventArgs args)
{
    long bytesRemaining = args.ContentLength;
    int bytesRead = 0;
    byte[] buffer = new byte[65536];

    using (MemoryStream ms = new MemoryStream())
    {
        while (bytesRemaining > 0)
        {
            bytesRead = args.DataStream.Read(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                ms.Write(buffer, 0, bytesRead);
                bytesRemaining -= bytesRead;
            }
        }
    }

    Console.WriteLine("Stream received from " + args.IpPort + ": " + Encoding.UTF8.GetString(ms.ToArray())); 
}
```

## Troubleshooting

The first step in troubleshooting is to implement a logging method and attach it to ```Settings.Logger```, and as a general best practice while debugging, set ```Settings.DebugMessages``` to ```true```.

```csharp
client.Settings.DebugMessages = true;
client.Settings.Logger = MyLoggerMethod;

private void MyLoggerMethod(Severity sev, string msg)
{
    Console.WriteLine(sev.ToString() + ": " + msg);
}
```

Additionally it is recommended that you implement the ```Events.ExceptionEncountered``` event.

```csharp
client.Events.ExceptionEncountered += MyExceptionEvent;

private void MyExceptionEvent(object sender, ExceptionEventArgs args)
{
    Console.WriteLine(args.Json);
}
```

## Disconnection Handling

The project TcpTest (https://github.com/jchristn/TcpTest) was built specifically to provide a reference for WatsonTcp to handle a variety of disconnection scenarios.  The disconnection tests for which WatsonTcp is evaluated include:

| Test case | Description | Pass/Fail |
|---|---|---|
| Server-side dispose | Graceful termination of all client connections | PASS |
| Server-side client removal | Graceful termination of a single client | PASS |
| Server-side termination | Abrupt termination due to process abort or CTRL-C | PASS |
| Client-side dispose | Graceful termination of a client connection | PASS |
| Client-side termination | Abrupt termination due to a process abort or CTRL-C | PASS |
| Network interface down | Network interface disabled or cable removed | Partial (see below) |

Additionally, as of v4.3.0, support for TCP keepalives has been added to WatsonTcp, primarily to address the issue of a network interface being shut down, the cable unplugged, or the media otherwise becoming unavailable.  It is important to note that keepalives are supported in .NET Core and .NET Framework, but NOT .NET Standard.  As of this release, .NET Standard provides no facilities for TCP keepalives.

TCP keepalives are enabled by default.
```csharp
server.Keepalive.EnableTcpKeepAlives = true;
server.Keepalive.TcpKeepAliveInterval = 5;      // seconds to wait before sending subsequent keepalive
server.Keepalive.TcpKeepAliveTime = 5;          // seconds to wait before sending a keepalive
server.Keepalive.TcpKeepAliveRetryCount = 5;    // number of failed keepalive probes before terminating connection
```

Some important notes about TCP keepalives:

- Keepalives only work in .NET Core and .NET Framework
- ```Keepalive.TcpKeepAliveRetryCount``` is only applicable to .NET Core; for .NET Framework, this value is forced to 10

## Disconnecting Idle Clients

If you wish to have WatsonTcpServer automatically disconnect clients that have been idle for a period of time, set ```WatsonTcpServer.IdleClientTimeoutSeconds``` to a positive integer.  Receiving a message from a client automatically resets their timeout.  Client timeouts are evaluated every 5 seconds by Watson, so the disconnection may not be precise (for instance, if you use 7 seconds as your disconnect interval).

## Version History

Please refer to CHANGELOG.md for details.
