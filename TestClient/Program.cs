namespace TestClient
{
    using System;
    using System.Text;
    using WatsonTcp;

    internal class TestClient
    {
        private static string serverIp = String.Empty;
        private static int serverPort = 0;
        private static bool useSsl = false;
        private static string certFile = String.Empty;
        private static string certPass = String.Empty;
        private static bool acceptInvalidCerts = true;
        private static bool mutualAuthentication = true;
        private static WatsonTcpClient client = null;
        private static string presharedKey = null;

        private static void Main()
        {
            serverIp = Common.InputString("Server IP:", "127.0.0.1", false);
            serverPort = Common.InputInteger("Server port:", 9000, true, false);
            useSsl = Common.InputBoolean("Use SSL:", false);

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

                        client.Send(Encoding.UTF8.GetBytes(userInput));
                        break;

                    case "sendasync":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput))
                        {
                            break;
                        }

                        bool success = client.SendAsync(Encoding.UTF8.GetBytes(userInput)).Result;
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
                            client = new WatsonTcpClient(serverIp, serverPort)
                            {
                                ServerConnected = ServerConnected,
                                ServerDisconnected = ServerDisconnected,
                                MessageReceived = MessageReceived,
                            };

                            client.Start();
                        }

                        break;

                    case "reconnect":
                        if (client != null)
                        {
                            client.Dispose();
                        }

                        client = new WatsonTcpClient(serverIp, serverPort)
                        {
                            ServerConnected = ServerConnected,
                            ServerDisconnected = ServerDisconnected,
                            MessageReceived = MessageReceived,
                        };

                        client.Start();
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
            if (!useSsl)
            {
                client = new WatsonTcpClient(serverIp, serverPort);
            }
            else
            {
                bool provideCertificate = Common.InputBoolean("Do you wish to provide a certificate ? (required for mutual authenication)", true);
                acceptInvalidCerts = Common.InputBoolean("Accept Invalid Certs:", true);

                if (provideCertificate)
                {
                    certFile = Common.InputString("Certificate file:", "test.pfx", false);
                    certPass = Common.InputString("Certificate password:", "password", false);
                    mutualAuthentication = Common.InputBoolean("Mutually authenticate:", true);

                    client = new WatsonTcpClient(serverIp, serverPort, certFile, certPass)
                    {
                        AcceptInvalidCertificates = acceptInvalidCerts,
                        MutuallyAuthenticate = mutualAuthentication,
                    };
                }
                else
                {
                    client = new WatsonTcpClient(Mode.Ssl, serverIp, serverPort, null)
                    {
                        AcceptInvalidCertificates = acceptInvalidCerts,
                    };
                }
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
            Console.WriteLine(String.Empty);
            Console.WriteLine(String.Empty);
            Console.WriteLine("Server requests authentication");
            Console.WriteLine("Press ENTER and THEN enter your preshared key");
            if (String.IsNullOrEmpty(presharedKey))
            {
                presharedKey = Common.InputString("Preshared key:", "1234567812345678", false);
            }

            return presharedKey;
        }

        private static bool AuthenticationSucceeded()
        {
            Console.WriteLine("Authentication succeeded");
            return true;
        }

        private static bool AuthenticationFailure()
        {
            Console.WriteLine("Authentication failed");
            return true;
        }

        private static bool MessageReceived(byte[] data)
        {
            Console.WriteLine("Message from server: " + Encoding.UTF8.GetString(data));
            return true;
        }

        private static bool ServerConnected()
        {
            Console.WriteLine("Server connected");
            return true;
        }

        private static bool ServerDisconnected()
        {
            Console.WriteLine("Server disconnected");
            return true;
        }
    }
}
