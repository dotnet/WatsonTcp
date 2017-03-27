# Watson TCP

[![][nuget-img]][nuget]

[nuget]:     https://www.nuget.org/packages/WatsonTcp/
[nuget-img]: https://badge.fury.io/nu/Object.svg

A simple C# async TCP server and client with integrated framing for reliable transmission and receipt of data.  

## Test App
A test project for both client and server are included which will help you understand and exercise the class library.

## SSL
Two classes for each server and client are supplied, one without SSL support and one with.  The SSL server and client classes include fields for the PFX certificate file and password in the constructor.  An example certificate can be found in the TestSslClient and TestSslServer projects, which has a password of 'password'.

## Running under Mono
Watson works well in Mono environments to the extent that we have tested it. It is recommended that when running under Mono, you execute the containing EXE using --server and after using the Mono Ahead-of-Time Compiler (AOT).  Note that TLS 1.2 is hard-coded, which may need to be downgraded to TLS in Mono environments.

NOTE: Windows accepts '0.0.0.0' as an IP address representing any interface.  On Mac and Linux you must be specified ('127.0.0.1' is also acceptable, but '0.0.0.0' is NOT).
```
mono --aot=nrgctx-trampolines=8096,nimt-trampolines=8096,ntrampolines=4048 --server myapp.exe
mono --server myapp.exe
```

## Contributions
Thanks to @brudo for his contributions to add async support to WatsonTcp (pushed in v1.0.7).

## Example
The following example shows a simple client and server example using WatsonTcp without SSL.

### Server
```
using WatsonTcp;

static void Main(string[] args)
{
    WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", 9000, ClientConnected, ClientDisconnected, MessageReceived, true);

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
    WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 9000, ServerConnected, ServerDisconnected, MessageReceived, true);

    bool runForever = true;
    while (runForever)
    {
        Console.Write("Command [q cls send]: ");
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
The examples above can be modified to use WatsonTcpSslServer and WatsonTcpSslClient as follows.  No other changes are needed.  Ensure that the certificate is exported as a PFX file and is resident in the directory of execution.

### Server
```
WatsonTcpSslServer server = new WatsonTcpSslServer("127.0.0.1", 9000, "test.pfx", "password", true, ClientConnected, ClientDisconnected, MessageReceived, true);
```

### Client
```
WatsonTcpSslClient client = new WatsonTcpSslClient("127.0.0.1", 9000, "test.pfx", "password", true, ServerConnected, ServerDisconnected, MessageReceived, true);
```

