namespace TestServerStream
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using WatsonTcp;

    internal class TestServerStream
    {
        private static string serverIp = String.Empty;
        private static int serverPort = 0;
        private static bool useSsl = false;
        private static WatsonTcpServer server = null;
        private static string certFile = String.Empty;
        private static string certPass = String.Empty;
        private static bool acceptInvalidCerts = true;
        private static bool mutualAuthentication = true;

        private static void Main()
        {
            serverIp = Common.InputString("Server IP:", "127.0.0.1", false);
            serverPort = Common.InputInteger("Server port:", 9000, true, false);
            useSsl = Common.InputBoolean("Use SSL:", false);

            if (!useSsl)
            {
                server = new WatsonTcpServer(serverIp, serverPort);
            }
            else
            {
                certFile = Common.InputString("Certificate file:", "test.pfx", false);
                certPass = Common.InputString("Certificate password:", "password", false);
                acceptInvalidCerts = Common.InputBoolean("Accept Invalid Certs:", true);
                mutualAuthentication = Common.InputBoolean("Mutually authenticate:", true);

                server = new WatsonTcpServer(serverIp, serverPort, certFile, certPass)
                {
                    AcceptInvalidCertificates = acceptInvalidCerts,
                    MutuallyAuthenticate = mutualAuthentication,
                };
            }

            server.ClientConnected = ClientConnected;
            server.ClientDisconnected = ClientDisconnected;
            server.StreamReceived = StreamReceived;
            server.ReadDataStream = false;
            // server.Debug = true;
            server.Start();

            bool runForever = true;
            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine();

                byte[] data = null;
                MemoryStream ms = null;
                bool success = false;

                List<string> clients;
                string ipPort;

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
                        Console.WriteLine("  list       list clients");
                        Console.WriteLine("  send       send message to a client");
                        Console.WriteLine("  sendasync  send message to a client asynchronously");
                        Console.WriteLine("  remove     disconnect client");
                        Console.WriteLine("  psk        set preshared key");
                        Console.WriteLine("  debug      enable/disable debug (currently " + server.Debug + ")");
                        break;

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
                            foreach (string curr in clients)
                            {
                                Console.WriteLine("  " + curr);
                            }
                        }
                        else
                        {
                            Console.WriteLine("None");
                        }

                        break;

                    case "send":
                        Console.Write("IP:Port: ");
                        ipPort = Console.ReadLine();
                        if (String.IsNullOrEmpty(ipPort))
                        {
                            break;
                        }

                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput))
                        {
                            break;
                        }

                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = server.Send(ipPort, data.Length, ms);
                        Console.WriteLine(success);
                        break;

                    case "sendasync":
                        Console.Write("IP:Port: ");
                        ipPort = Console.ReadLine();
                        if (String.IsNullOrEmpty(ipPort))
                        {
                            break;
                        }

                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput))
                        {
                            break;
                        }

                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = server.SendAsync(ipPort, data.Length, ms).Result;
                        Console.WriteLine(success);
                        break;

                    case "remove":
                        Console.Write("IP:Port: ");
                        ipPort = Console.ReadLine();
                        server.DisconnectClient(ipPort);
                        break;

                    case "psk":
                        server.PresharedKey = Common.InputString("Preshared key:", "1234567812345678", false);
                        break;

                    case "debug":
                        server.Debug = !server.Debug;
                        Console.WriteLine("Debug set to: " + server.Debug);
                        break;

                    default:
                        break;
                }
            }
        }

        private static bool ClientConnected(string ipPort)
        {
            Console.WriteLine("Client connected: " + ipPort);
            return true;
        }

        private static bool ClientDisconnected(string ipPort)
        {
            Console.WriteLine("Client disconnected: " + ipPort);
            return true;
        }

        private static bool StreamReceived(string ipPort, long contentLength, Stream stream)
        {
            try
            {
                Console.Write("Stream from " + ipPort + " [" + contentLength + " bytes]: ");

                int bytesRead = 0;
                int bufferSize = 65536;
                byte[] buffer = new byte[bufferSize];
                long bytesRemaining = contentLength;

                if (stream != null && stream.CanRead)
                {
                    while (bytesRemaining > 0)
                    {
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                        Console.WriteLine("Read " + bytesRead);

                        if (bytesRead > 0)
                        {
                            byte[] consoleBuffer = new byte[bytesRead];
                            Buffer.BlockCopy(buffer, 0, consoleBuffer, 0, bytesRead);
                            Console.Write(Encoding.UTF8.GetString(consoleBuffer));
                        }

                        bytesRemaining -= bytesRead;
                    }

                    Console.WriteLine(String.Empty);
                }
                else
                {
                    Console.WriteLine("[null]");
                }

                return true;
            }
            catch (Exception e)
            {
                Common.LogException("StreamReceived", e);
                return false;
            }
        }
    }
}
