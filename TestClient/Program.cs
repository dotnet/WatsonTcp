using System;
using System.Text;
using System.Threading.Tasks;
using WatsonTcp;

namespace TestClient
{
    internal class TestClient
    {
        private static string serverIp = "";
        private static int serverPort = 0;
        private static bool useSsl = false;
        private static string certFile = "";
        private static string certPass = "";
        private static bool acceptInvalidCerts = true;
        private static bool mutualAuthentication = true;
        private static WatsonTcpClient client = null;
        private static string presharedKey = null;

        private static void Main(string[] args)
        {
            InitializeClient();

            bool runForever = true;
            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine();
                if (String.IsNullOrEmpty(userInput))
                {
                    continue;
                }

                switch (userInput)
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?          help (this menu)");
                        Console.WriteLine("  q          quit");
                        Console.WriteLine("  cls        clear screen");
                        Console.WriteLine("  send       send message to server");
                        Console.WriteLine("  sendasync  send message to server asynchronously");
                        Console.WriteLine("  status     show if client connected");
                        Console.WriteLine("  dispose    dispose of the connection");
                        Console.WriteLine("  connect    connect to the server if not connected");
                        Console.WriteLine("  reconnect  disconnect if connected, then reconnect");
                        Console.WriteLine("  psk        set the preshared key");
                        Console.WriteLine("  auth       authenticate using the preshared key");
                        Console.WriteLine("  debug      enable/disable debug (currently " + client.Debug + ")");
                        break;

                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "send":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput))
                        {
                            break;
                        }

                        if (!client.Send(Encoding.UTF8.GetBytes(userInput))) Console.WriteLine("Failed");
                        break;

                    case "sendasync":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput))
                        {
                            break;
                        }

                        if (!client.SendAsync(Encoding.UTF8.GetBytes(userInput)).Result) Console.WriteLine("Failed");
                        break;

                    case "status":
                        if (client == null)
                        {
                            Console.WriteLine("Connected: False (null)");
                        }
                        else
                        {
                            Console.WriteLine("Connected: " + client.Connected);
                        }

                        break;

                    case "dispose":
                        client.Dispose();
                        break;

                    case "connect":
                        if (client != null && client.Connected)
                        {
                            Console.WriteLine("Already connected");
                        }
                        else
                        {
                            client = new WatsonTcpClient(serverIp, serverPort);
                            client.ServerConnected = ServerConnected;
                            client.ServerDisconnected = ServerDisconnected;
                            client.MessageReceived = MessageReceived;
                            client.Start();
                        }
                        break;

                    case "reconnect":
                        ConnectClient();
                        break;

                    case "psk":
                        presharedKey = Common.InputString("Preshared key:", "1234567812345678", false);
                        break;

                    case "auth":
                        client.Authenticate(presharedKey);
                        break;

                    case "debug":
                        client.Debug = !client.Debug;
                        Console.WriteLine("Debug set to: " + client.Debug);
                        break;

                    default:
                        break;
                }
            }
        }

        private static void InitializeClient()
        {
            serverIp = Common.InputString("Server IP:", "127.0.0.1", false);
            serverPort = Common.InputInteger("Server port:", 9000, true, false);
            useSsl = Common.InputBoolean("Use SSL:", false);

            if (!useSsl)
            {
                client = new WatsonTcpClient(serverIp, serverPort);
            }
            else
            {
                bool supplyCert = Common.InputBoolean("Supply SSL certificate:", false);

                if (supplyCert)
                {
                    certFile = Common.InputString("Certificate file:", "test.pfx", false);
                    certPass = Common.InputString("Certificate password:", "password", false);
                }

                acceptInvalidCerts = Common.InputBoolean("Accept Invalid Certs:", true);
                mutualAuthentication = Common.InputBoolean("Mutually authenticate:", false);

                client = new WatsonTcpClient(serverIp, serverPort, certFile, certPass);
                client.AcceptInvalidCertificates = acceptInvalidCerts;
                client.MutuallyAuthenticate = mutualAuthentication;
            }

            ConnectClient();
        }

        private static void ConnectClient()
        { 
            if (client != null) client.Dispose();

            if (!useSsl)
            {
                client = new WatsonTcpClient(serverIp, serverPort);
            }
            else
            {
                client = new WatsonTcpClient(serverIp, serverPort, certFile, certPass);
                client.AcceptInvalidCertificates = acceptInvalidCerts;
                client.MutuallyAuthenticate = mutualAuthentication;
            }

            client.AuthenticationFailure = AuthenticationFailure;
            client.AuthenticationRequested = AuthenticationRequested;
            client.AuthenticationSucceeded = AuthenticationSucceeded;
            client.ServerConnected = ServerConnected;
            client.ServerDisconnected = ServerDisconnected;
            client.MessageReceived = MessageReceived;
            client.ReadDataStream = true;
            client.ReadStreamBufferSize = 65536;
            // client.Debug = true;
            client.Start(); 
        }

        private static string AuthenticationRequested()
        {
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("Server requests authentication");
            Console.WriteLine("Press ENTER and THEN enter your preshared key");
            if (String.IsNullOrEmpty(presharedKey)) presharedKey = Common.InputString("Preshared key:", "1234567812345678", false);
            return presharedKey;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        private static async Task AuthenticationSucceeded()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            Console.WriteLine("Authentication succeeded");
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        private static async Task AuthenticationFailure()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            Console.WriteLine("Authentication failed");
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        private static async Task MessageReceived(byte[] data)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            Console.WriteLine("Message from server: " + Encoding.UTF8.GetString(data));
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        private static async Task ServerConnected()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            Console.WriteLine("Server connected");
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

        private static async Task ServerDisconnected()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            Console.WriteLine("Server disconnected");
        }
    }
}