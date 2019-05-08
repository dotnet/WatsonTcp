using System;
using System.IO;
using System.Text;
using WatsonTcp;

namespace TestClientStream
{
    class TestClientStream
    {
        static string serverIp = "";
        static int serverPort = 0;
        static bool useSsl = false;
        static string certFile = "";
        static string certPass = "";
        static bool acceptInvalidCerts = true;
        static bool mutualAuthentication = true;
        static WatsonTcpClient client = null;
        static string presharedKey = null;

        static void Main(string[] args)
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
                byte[] data = null;
                MemoryStream ms = null;
                bool success = false;

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
                        if (String.IsNullOrEmpty(userInput)) break;
                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = client.Send(data.Length, ms);
                        Console.WriteLine(success);
                        break;

                    case "sendasync":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = client.SendAsync(data.Length, ms).Result;
                        Console.WriteLine(success);
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
                            client.StreamReceived = StreamReceived;
                            client.Start();
                        }
                        break;

                    case "reconnect":
                        if (client != null) client.Dispose();
                        client = new WatsonTcpClient(serverIp, serverPort);
                        client.ServerConnected = ServerConnected;
                        client.ServerDisconnected = ServerDisconnected;
                        client.StreamReceived = StreamReceived;
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

        static bool StreamReceived(long contentLength, Stream stream)
        {
            try
            {
                Console.Write("Stream from server [" + contentLength + " bytes]: ");

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

                    Console.WriteLine("");
                }
                else
                {
                    Console.WriteLine("[null]");
                }
                 
                return true;
            } 
            catch (Exception e)
            {
                LogException(e);
                return false;
            }
        }

        static void InitializeClient()
        {
            if (!useSsl)
            {
                client = new WatsonTcpClient(serverIp, serverPort);
            }
            else
            {
                certFile = Common.InputString("Certificate file:", "test.pfx", false);
                certPass = Common.InputString("Certificate password:", "password", false);
                acceptInvalidCerts = Common.InputBoolean("Accept Invalid Certs:", true);
                mutualAuthentication = Common.InputBoolean("Mutually authenticate:", true);

                client = new WatsonTcpClient(serverIp, serverPort, certFile, certPass);
                client.AcceptInvalidCertificates = acceptInvalidCerts;
                client.MutuallyAuthenticate = mutualAuthentication;
            }

            client.AuthenticationFailure = AuthenticationFailure;
            client.AuthenticationRequested = AuthenticationRequested;
            client.AuthenticationSucceeded = AuthenticationSucceeded;
            client.ServerConnected = ServerConnected;
            client.ServerDisconnected = ServerDisconnected;
            client.StreamReceived = StreamReceived;
            client.ReadDataStream = false;
            // client.Debug = true;
            client.Start(); 
        }

        static string AuthenticationRequested()
        {
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("Server requests authentication");
            Console.WriteLine("Press ENTER and THEN enter your preshared key");
            if (String.IsNullOrEmpty(presharedKey)) presharedKey = Common.InputString("Preshared key:", "1234567812345678", false);
            return presharedKey;
        }

        static bool AuthenticationSucceeded()
        {
            Console.WriteLine("Authentication succeeded");
            return true;
        }

        static bool AuthenticationFailure()
        {
            Console.WriteLine("Authentication failed");
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

        static void LogException(Exception e)
        {
            Console.WriteLine("================================================================================");
            Console.WriteLine(" = Exception Type: " + e.GetType().ToString());
            Console.WriteLine(" = Exception Data: " + e.Data);
            Console.WriteLine(" = Inner Exception: " + e.InnerException);
            Console.WriteLine(" = Exception Message: " + e.Message);
            Console.WriteLine(" = Exception Source: " + e.Source);
            Console.WriteLine(" = Exception StackTrace: " + e.StackTrace);
            Console.WriteLine("================================================================================");
        }
    }
}
