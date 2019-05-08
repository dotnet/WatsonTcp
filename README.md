# Watson TCP

[![][nuget-img]][nuget]

[nuget]:     https://www.nuget.org/packages/WatsonTcp/
[nuget-img]: https://badge.fury.io/nu/Object.svg

A simple C# async TCP server and client with integrated framing for reliable transmission and receipt of data.  

## New in v1.3.x

- Numerous fixes to authentication using preshared keys
- Authentication callbacks in the client to handle authentication events
  - ```AuthenticationRequested``` - authentication requested by the server, return the preshared key string (16 bytes)
  - ```AuthenticationSucceeded``` - authentication has succeeded, return true
  - ```AuthenticationFailure``` - authentication has failed, return true
- Support for sending and receiving larger messages by using streams instead of byte arrays
- Refer to ```TestServerStream``` and ```TestClientStream``` for a reference implementation.  You must set ```client.ReadDataStream = false``` and ```server.ReadDataStream = false``` and use the ```StreamReceived``` callback instead of ```MessageReceived```

## Test Applications

Test projects for both client and server are included which will help you understand and exercise the class library.

## SSL

WatsonTcp supports data exchange with or without SSL.  The server and client classes include constructors that allow you to include fields for the PFX certificate file and password.  An example certificate can be found in the test projects, which has a password of 'password'.

## Running under Mono

.NET Core should always be the preferred option for multi-platform deployments.  However, WatsonTcp works well in Mono environments with the .NET Framework to the extent that we have tested it. It is recommended that when running under Mono, you execute the containing EXE using --server and after using the Mono Ahead-of-Time Compiler (AOT).  Note that TLS 1.2 is hard-coded, which may need to be downgraded to TLS in Mono environments.

NOTE: Windows accepts '0.0.0.0' as an IP address representing any interface.  On Mac and Linux you must be specified ('127.0.0.1' is also acceptable, but '0.0.0.0' is NOT).
```
mono --aot=nrgctx-trampolines=8096,nimt-trampolines=8096,ntrampolines=4048 --server myapp.exe
mono --server myapp.exe
```

## Contributions

Special thanks to @brudo and @MrMikeJJ for their support of this project!  

If you'd like to contribute, please jump right into the source code and create a pull request. 

## Examples

The following examples show a simple client and server example using WatsonTcp without SSL.

### Server
```
using WatsonTcp;

static void Main(string[] args)
{
    WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", 9000);
    server.ClientConnected = ClientConnected;
    server.ClientDisconnected = ClientDisconnected;
    server.MessageReceived = MessageReceived;
    server.Debug = false;
    server.Start();

    bool runForever = true;
    while (runForever)
    {
        Console.Write("Command [q cls list send]: ");
        string userInput = Console.ReadLine();
        if (String.IsNullOrEmpty(userInput)) continue;

        List<string> clients;
        string ipPort;

        switch (userInput)
        {
            case "q":
                runForever = false;
                break;
            case "cls":
                Console.Clear();
                break;
            case "list":
                clients = server.ListClients();
                if (clients != null && clients.Count > 0)
                {
                    Console.WriteLine("Clients");
                    foreach (string curr in clients) Console.WriteLine("  " + curr); 
                }
                else Console.WriteLine("None"); 
                break;
            case "send":
                Console.Write("IP:Port: ");
                ipPort = Console.ReadLine();
                Console.Write("Data: ");
                userInput = Console.ReadLine();
                if (String.IsNullOrEmpty(userInput)) break;
                server.Send(ipPort, Encoding.UTF8.GetBytes(userInput));
                break;
        }
    }
}

static bool ClientConnected(string ipPort)
{
    Console.WriteLine("Client connected: " + ipPort);
    return true;
}

static bool ClientDisconnected(string ipPort)
{
    Console.WriteLine("Client disconnected: " + ipPort);
    return true;
}

static bool MessageReceived(string ipPort, byte[] data)
{
    string msg = "";
    if (data != null && data.Length > 0) msg = Encoding.UTF8.GetString(data);
    Console.WriteLine("Message received from " + ipPort + ": " + msg);
    return true;
}
```

### Client
```
using WatsonTcp;

static void Main(string[] args)
{
    WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 9000);
    client.ServerConnected = ServerConnected;
    client.ServerDisconnected = ServerDisconnected;
    client.MessageReceived = MessageReceived;
    client.Debug = false;
    client.Start();

    bool runForever = true;
    while (runForever)
    {
        Console.Write("Command [q cls send auth]: ");
        string userInput = Console.ReadLine();
        if (String.IsNullOrEmpty(userInput)) continue;

        switch (userInput)
        {
            case "q":
                runForever = false;
                break;
            case "cls":
                Console.Clear();
                break;
            case "send":
                Console.Write("Data: ");
                userInput = Console.ReadLine();
                if (String.IsNullOrEmpty(userInput)) break;
                client.Send(Encoding.UTF8.GetBytes(userInput));
                break;
            case "auth":
                Console.Write("Preshared key: ");
                userInput = Console.ReadLine();
                if (String.IsNullOrEmpty(userInput)) break;
                client.Authenticate(userInput);
                break;
        }
    }
}

static bool MessageReceived(byte[] data)
{
    Console.WriteLine("Message from server: " + Encoding.UTF8.GetString(data));
    return true;
}

static bool ServerConnected()
{
    Console.WriteLine("Server connected");
    return true;
}

static bool ServerDisconnected()
{
    Console.WriteLine("Server disconnected");
    return true;
}
```

## Example with SSL

The examples above can be modified to use SSL as follows.  No other changes are needed.  Ensure that the certificate is exported as a PFX file and is resident in the directory of execution.
```
// server
WatsonTcpServer server = new WatsonTcpSslServer("127.0.0.1", 9000, "test.pfx", "password");
server.ClientConnected = ClientConnected;
server.ClientDisconnected = ClientDisconnected;
server.MessageReceived = MessageReceived;
server.AcceptInvalidCertificates = true;
server.MutuallyAuthenticate = true;
server.Start();

// client
WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 9000, "test.pfx", "password");
client.ServerConnected = ServerConnected;
client.ServerDisconnected = ServerDisconnected;
client.MessageReceived = MessageReceived;
client.AcceptInvalidCertificates = true;
client.MutuallyAuthenticate = true;
client.Start();
```

## Example with Streams

Refer to the ```TestClientStream``` and ```TestServerStream``` projects for a full example
```
// server
WatsonTcpServer server = new WatsonTcpSslServer("127.0.0.1", 9000);
server.ClientConnected = ClientConnected;
server.ClientDisconnected = ClientDisconnected;
server.StreamReceived = StreamReceived;
server.ReadDataStream = false;
server.Start();

static bool StreamReceived(string ipPort, long contentLength, Stream stream)
{
    // read contentLength bytes from the stream from client ipPort and process
    return true;
}

// client
WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 9000);
client.ServerConnected = ServerConnected;
client.ServerDisconnected = ServerDisconnected;
client.StreamReceived = StreamReceived;
client.ReadDataStream = false;
client.Start();

static bool StreamReceived(long contentLength, Stream stream)
{
    // read contentLength bytes from the stream and process
    return true;
}
```

## Version History

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
