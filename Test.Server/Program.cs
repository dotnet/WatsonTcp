using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WatsonTcp;

namespace TestServer
{
    internal class TestServer
    {
        private static string serverIp = "";
        private static int serverPort = 0;
        private static bool useSsl = false;
        private static WatsonTcpServer<BlankMetadata> server = null;
        private static string certFile = "";
        private static string certPass = "";
        private static bool debugMessages = true;
        private static bool acceptInvalidCerts = true;
        private static bool mutualAuthentication = true;
        private static string lastIpPort = null;

        private static void Main(string[] args)
        {
            serverIp = InputString("Server IP:", "127.0.0.1", false);
            serverPort = InputInteger("Server port:", 9000, true, false);
            useSsl = InputBoolean("Use SSL:", false);

            try
            {
                if (!useSsl)
                {
                    server = new WatsonTcpServer<BlankMetadata>(serverIp, serverPort);
                }
                else
                {
                    certFile = InputString("Certificate file:", "test.pfx", false);
                    certPass = InputString("Certificate password:", "password", false);
                    acceptInvalidCerts = InputBoolean("Accept invalid certs:", true);
                    mutualAuthentication = InputBoolean("Mutually authenticate:", false);

                    server = new WatsonTcpServer<BlankMetadata>(serverIp, serverPort, certFile, certPass);
                    server.AcceptInvalidCertificates = acceptInvalidCerts;
                    server.MutuallyAuthenticate = mutualAuthentication;
                }

                server.ClientConnected += ClientConnected;
                server.ClientDisconnected += ClientDisconnected;
                server.MessageReceived += MessageReceived;
                server.SyncRequestReceived = SyncRequestReceived;
                // server.PresharedKey = "0000000000000000";
                // server.IdleClientTimeoutSeconds = 10;
                server.Logger = Logger;
                server.DebugMessages = debugMessages;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return;
            }

            // server.Start();
            Task serverStart = server.StartAsync();

            bool runForever = true;
            List<string> clients;
            string ipPort;
            BlankMetadata metadata;
            bool success = false;

            while (runForever)
            {
                string userInput = InputString("Command [? for help]:", null, false);
                 
                switch (userInput)
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?                   help (this menu)");
                        Console.WriteLine("  q                   quit");
                        Console.WriteLine("  cls                 clear screen");
                        Console.WriteLine("  list                list clients");
                        Console.WriteLine("  dispose             dispose of the connection");
                        Console.WriteLine("  send                send message to client");
                        Console.WriteLine("  send md             send message with metadata to client");
                        Console.WriteLine("  sendasync           send message to a client asynchronously");
                        Console.WriteLine("  sendasync md        send message with metadata to a client asynchronously");
                        Console.WriteLine("  sendandwait         send message and wait for a response");
                        Console.WriteLine("  sendempty           send empty message with metadata");
                        Console.WriteLine("  sendandwait empty   send empty message with metadata and wait for a response");
                        Console.WriteLine("  remove              disconnect client");
                        Console.WriteLine("  psk                 set preshared key");
                        Console.WriteLine("  stats               display server statistics");
                        Console.WriteLine("  stats reset         reset statistics other than start time and uptime");
                        Console.WriteLine("  comp                set the compression type, currently: " + server.Compression.ToString());
                        Console.WriteLine("  debug               enable/disable debug (currently " + server.DebugMessages + ")");
                        break;

                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "list":
                        clients = server.ListClients().ToList();
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

                    case "dispose":
                        server.Dispose();
                        break;

                    case "send":
                        ipPort = InputString("IP:port:", lastIpPort, false);
                        userInput = InputString("Data:", null, false);
                        if (!server.Send(ipPort, userInput)) Console.WriteLine("Failed");
                        break;

                    case "send md":
                        ipPort = InputString("IP:port:", lastIpPort, false);
                        userInput = InputString("Data:", null, false);
                        metadata = InputDictionary();
                        if (!server.Send(ipPort, metadata, userInput)) Console.WriteLine("Failed");
                        Console.WriteLine(success);
                        break;

                    case "send md large":
                        ipPort = InputString("IP:port:", lastIpPort, false);
                        metadata = new BlankMetadata();
                        
                        if (!server.Send(ipPort, metadata, "Hello!")) Console.WriteLine("Failed");
                        break;

                    case "sendasync":
                        ipPort = InputString("IP:port:", lastIpPort, false);
                        userInput = InputString("Data:", null, false); 
                        success = server.SendAsync(ipPort, Encoding.UTF8.GetBytes(userInput)).Result;
                        if (!success) Console.WriteLine("Failed");
                        break;

                    case "sendasync md":
                        ipPort = InputString("IP:port:", lastIpPort, false);
                        userInput = InputString("Data:", null, false);
                        metadata = InputDictionary();
                        success = server.SendAsync(ipPort, metadata, Encoding.UTF8.GetBytes(userInput)).Result;
                        if (!success) Console.WriteLine("Failed");
                        break;

                    case "sendandwait":
                        SendAndWait();
                        break;

                    case "sendempty":
                        ipPort = InputString("IP:port:", lastIpPort, false);
                        metadata = InputDictionary();
                        if (!server.Send(ipPort, metadata)) Console.WriteLine("Failed");
                        break;

                    case "sendandwait empty":
                        SendAndWaitEmpty();
                        break;

                    case "remove":
                        ipPort = InputString("IP:port:", lastIpPort, false);
                        server.DisconnectClient(ipPort);
                        break;

                    case "psk":
                        server.PresharedKey = InputString("Preshared key:", "1234567812345678", false);
                        break;

                    case "stats":
                        Console.WriteLine(server.Stats.ToString());
                        break;

                    case "stats reset":
                        server.Stats.Reset();
                        break;

                    case "comp":
                        server.Compression = (CompressionType)(Enum.Parse(typeof(CompressionType), InputString("Compression [None|Default|Gzip]:", "None", false)));
                        break;

                    case "debug":
                        server.DebugMessages = !server.DebugMessages;
                        Console.WriteLine("Debug set to: " + server.DebugMessages);
                        break;
                         
                    default:
                        break;
                }
            }
        }

        private static bool InputBoolean(string question, bool yesDefault)
        {
            Console.Write(question);

            if (yesDefault) Console.Write(" [Y/n]? ");
            else Console.Write(" [y/N]? ");

            string userInput = Console.ReadLine();

            if (String.IsNullOrEmpty(userInput))
            {
                if (yesDefault) return true;
                return false;
            }

            userInput = userInput.ToLower();

            if (yesDefault)
            {
                if (
                    (String.Compare(userInput, "n") == 0)
                    || (String.Compare(userInput, "no") == 0)
                   )
                {
                    return false;
                }

                return true;
            }
            else
            {
                if (
                    (String.Compare(userInput, "y") == 0)
                    || (String.Compare(userInput, "yes") == 0)
                   )
                {
                    return true;
                }

                return false;
            }
        }

        private static string InputString(string question, string defaultAnswer, bool allowNull)
        {
            while (true)
            {
                Console.Write(question);

                if (!String.IsNullOrEmpty(defaultAnswer))
                {
                    Console.Write(" [" + defaultAnswer + "]");
                }

                Console.Write(" ");

                string userInput = Console.ReadLine();

                if (String.IsNullOrEmpty(userInput))
                {
                    if (!String.IsNullOrEmpty(defaultAnswer)) return defaultAnswer;
                    if (allowNull) return null;
                    else continue;
                }

                return userInput;
            }
        }

        private static int InputInteger(string question, int defaultAnswer, bool positiveOnly, bool allowZero)
        {
            while (true)
            {
                Console.Write(question);
                Console.Write(" [" + defaultAnswer + "] ");

                string userInput = Console.ReadLine();

                if (String.IsNullOrEmpty(userInput))
                {
                    return defaultAnswer;
                }

                int ret = 0;
                if (!Int32.TryParse(userInput, out ret))
                {
                    Console.WriteLine("Please enter a valid integer.");
                    continue;
                }

                if (ret == 0)
                {
                    if (allowZero)
                    {
                        return 0;
                    }
                }

                if (ret < 0)
                {
                    if (positiveOnly)
                    {
                        Console.WriteLine("Please enter a value greater than zero.");
                        continue;
                    }
                }

                return ret;
            }
        }

        private static BlankMetadata InputDictionary()
        {
            Console.WriteLine("Build metadata, press ENTER on 'Key' to exit");
            return new BlankMetadata();
            // TODO: Reimplement me
        }
         
        private static void ClientConnected(object sender, ClientConnectedEventArgs args)
        {
            lastIpPort = args.IpPort;
            Console.WriteLine("Client connected: " + args.IpPort);
            // Console.WriteLine("Disconnecting: " + args.IpPort);
            // server.DisconnectClient(args.IpPort);
        }
         
        private static void ClientDisconnected(object sender, ClientDisconnectedEventArgs args)
        {
            Console.WriteLine("Client disconnected: " + args.IpPort + ": " + args.Reason.ToString());
        }
         
        private static void MessageReceived(object sender, MessageReceivedFromClientEventArgs<BlankMetadata> args)
        {
            lastIpPort = args.IpPort;
            Console.Write("Message received from " + args.IpPort + ": ");
            if (args.Data != null) Console.WriteLine(Encoding.UTF8.GetString(args.Data));
            else Console.WriteLine("[null]");

        }
         
        private static SyncResponse<BlankMetadata> SyncRequestReceived(SyncRequest<BlankMetadata> req)
        {
            Console.Write("Synchronous request received from " + req.IpPort + ": ");
            if (req.Data != null) Console.WriteLine(Encoding.UTF8.GetString(req.Data));
            else Console.WriteLine("[null]");


            var retMetadata = new BlankMetadata();

            // Uncomment to test timeout
            // Task.Delay(10000).Wait();
            Console.WriteLine("Sending synchronous response");
            return new SyncResponse<BlankMetadata>(req, retMetadata, "Here is your response!");
        }

        private static void SendAndWait()
        {
            string ipPort = InputString("IP:port:", lastIpPort, false);
            string userInput = InputString("Data:", null, false);
            int timeoutMs = InputInteger("Timeout (milliseconds):", 5000, true, false);

            try
            {
                SyncResponse<BlankMetadata> resp = server.SendAndWait(ipPort, timeoutMs, userInput);
                
                Console.WriteLine("Response: " + Encoding.UTF8.GetString(resp.Data));
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.ToString());
            }
        }

        private static void SendAndWaitEmpty()
        {
            string ipPort = InputString("IP:port:", lastIpPort, false); 
            int timeoutMs = InputInteger("Timeout (milliseconds):", 5000, true, false);

            var dict = new BlankMetadata();

            try
            {
                var resp = server.SendAndWait(ipPort, dict, timeoutMs);
                
                Console.WriteLine("Response: " + Encoding.UTF8.GetString(resp.Data));
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.ToString());
            }
        }

        private static void Logger(string msg)
        {
            Console.WriteLine(msg);
        }
    }
}