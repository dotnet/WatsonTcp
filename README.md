![alt tag](https://github.com/jchristn/watsontcp/blob/master/assets/watson.ico)

# Watson TCP

[![][nuget-img]][nuget]

[nuget]:     https://www.nuget.org/packages/WatsonTcp/
[nuget-img]: https://badge.fury.io/nu/Object.svg

WatsonTcp is the fastest, most efficient way to build TCP-based clients and servers in C# with integrated framing, reliable transmission, fast disconnect detection, and easy to understand callbacks.

## New in v2.1.4

- Minor breaking change; ClientDisconnect now includes DisconnectReason to differentiate between normal, kicked, or timeout disconnections

## Test Applications

Test projects for both client and server are included which will help you understand and exercise the class library.

## SSL

WatsonTcp supports data exchange with or without SSL.  The server and client classes include constructors that allow you to include fields for the PFX certificate file and password.  An example certificate can be found in the test projects, which has a password of 'password'.

## To Stream or Not To Stream...

WatsonTcp allows you to receive messages using either streams or byte arrays.  The ```MessageReceived``` callback uses byte arrays and provides the easiest implementation, but the entire message payload is copied into memory, making it inefficient for larger messages.  For larger message sizes (generally measured in 10s or 100s of megabytes or beyond), it is **strongly** recommended that you use the ```StreamReceived``` callback.  Only one of these methods can be assigned; you cannot use both.

When sending messages, the ```Send``` and ```SendAsync``` methods have both byte array and stream variants.  You are free to use whichever, or both, as you choose, regardless of whether you have implemented ```MessageReceived``` or ```StreamReceived```.

It is important to note that when using ```StreamReceived```, the socket is blocked until you have fully read the stream and control has returned from your consuming application back to WatsonTcp.  That's required, because otherwise, WatsonTcp would begin reading at the wrong place in the stream.  With ```MessageReceived```, WatsonTcp will call your callback and begin reading immediately, since the entirety of the message data has already been read from the stream by WatsonTcp.

Please see below for examples with byte arrays and with streams.

## Running under Mono

.NET Core should always be the preferred option for multi-platform deployments.  However, WatsonTcp works well in Mono environments with the .NET Framework to the extent that we have tested it. It is recommended that when running under Mono, you execute the containing EXE using --server and after using the Mono Ahead-of-Time Compiler (AOT).  Note that TLS 1.2 is hard-coded, which may need to be downgraded to TLS in Mono environments.

NOTE: Windows accepts '0.0.0.0' as an IP address representing any interface.  On Mac and Linux you must be specified ('127.0.0.1' is also acceptable, but '0.0.0.0' is NOT).
```
mono --aot=nrgctx-trampolines=8096,nimt-trampolines=8096,ntrampolines=4048 --server myapp.exe
mono --server myapp.exe
```

## Contributions

Special thanks to @brudo, @MrMikeJJ, @mikkleini, and @pha3z for their support of this project!  If you'd like to contribute, please jump right into the source code and create a pull request. 

## Examples

The following examples show a simple client and server example using WatsonTcp without SSL.

### Local vs External Connections

**IMPORTANT**
* If you specify ```127.0.0.1``` as the listener IP address in WatsonTcpServer, it will only be able to accept connections from within the local host.  
* To accept connections from other machines, specify a specific IP address, or, use ```null``` for the listener IP address.
* If you use ```null``` for the IP address, or any variant representing any IP address such as ```0.0.0.0```, ```+```, or ```*```, you may have to run WatsonTcpServer with administrative privileges (this is required by the operating system).

### Server

Using byte arrays (```MessageReceived```)

```
using WatsonTcp;

static void Main(string[] args)
{
    WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", 9000);
    server.ClientConnected = ClientConnected;
    server.ClientDisconnected = ClientDisconnected;
    server.MessageReceived = MessageReceived; 
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

static async Task ClientConnected(string ipPort)
{
    Console.WriteLine("Client connected: " + ipPort);
}

static async Task ClientDisconnected(string ipPort)
{
    Console.WriteLine("Client disconnected: " + ipPort);
}

static async Task MessageReceived(string ipPort, byte[] data)
{
    string msg = "";
    if (data != null && data.Length > 0) msg = Encoding.UTF8.GetString(data);
    Console.WriteLine("Message received from " + ipPort + ": " + msg);
}
```

### Client

Using byte arrays (```MessageReceived```)

```
using WatsonTcp;

static void Main(string[] args)
{
    WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 9000);
    client.ServerConnected = ServerConnected;
    client.ServerDisconnected = ServerDisconnected;
    client.MessageReceived = MessageReceived; 
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

static async Task MessageReceived(byte[] data)
{
    Console.WriteLine("Message from server: " + Encoding.UTF8.GetString(data));
}

static async Task ServerConnected()
{
    Console.WriteLine("Server connected");
}

static async Task ServerDisconnected()
{
    Console.WriteLine("Server disconnected");
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

Refer to the ```TestClientStream``` and ```TestServerStream``` projects for a full example.  
```
// server
WatsonTcpServer server = new WatsonTcpSslServer("127.0.0.1", 9000);
server.ClientConnected = ClientConnected;
server.ClientDisconnected = ClientDisconnected;
server.StreamReceived = StreamReceived; 
server.Start();

static async Task StreamReceived(string ipPort, long contentLength, Stream stream)
{
    // read contentLength bytes from the stream from client ipPort and process
    return true;
}

// client
WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 9000);
client.ServerConnected = ServerConnected;
client.ServerDisconnected = ServerDisconnected;
client.StreamReceived = StreamReceived; 
client.Start();

static async Task StreamReceived(long contentLength, Stream stream)
{
    // read contentLength bytes from the stream and process
}
```

## Disconnection Handling

The project TcpTest (https://github.com/jchristn/TcpTest) was built specifically to provide a reference for WatsonTcp to handle a variety of disconnection scenarios.  These include:

| Test case | Description | Pass/Fail |
|---|---|---|
| Server-side dispose | Graceful termination of all client connections | PASS |
| Server-side client removal | Graceful termination of a single client | PASS |
| Server-side termination | Abrupt termination due to process abort or CTRL-C | PASS |
| Client-side dispose | Graceful termination of a client connection | PASS |
| Client-side termination | Abrupt termination due to a process abort or CTRL-C | PASS |

## Disconnecting Idle Clients

If you wish to have WatsonTcpServer automatically disconnect clients that have been idle for a period of time, set ```WatsonTcpServer.IdleClientTimeoutSeconds``` to a positive integer.  Receiving a message from a client automatically resets their timeout.  Client timeouts are evaluated every 5 seconds by Watson, so the disconnection may not be precise (for instance, if you use 7 seconds as your disconnect interval).

## Version History

Please refer to CHANGELOG.md for details.
