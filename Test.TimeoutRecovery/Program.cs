using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WatsonTcp;

namespace Test.TimeoutRecovery
{
    internal class TestServer
    {
        private static string serverIp = "";
        private static int serverPort = 0;
        private static bool useSsl = false;
        private static WatsonTcpServer server = null;
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
                    server = new WatsonTcpServer(serverIp, serverPort);
                }
                else
                {
                    certFile = InputString("Certificate file:", "test.pfx", false);
                    certPass = InputString("Certificate password:", "password", false);
                    acceptInvalidCerts = InputBoolean("Accept invalid certs:", true);
                    mutualAuthentication = InputBoolean("Mutually authenticate:", false);

                    server = new WatsonTcpServer(serverIp, serverPort, certFile, certPass);
                    server.Settings.AcceptInvalidCertificates = acceptInvalidCerts;
                    server.Settings.MutuallyAuthenticate = mutualAuthentication;
                }

                server.Events.ClientConnected += ClientConnected;
                server.Events.ClientDisconnected += ClientDisconnected;
                server.Events.MessageReceived += MessageReceived;
                server.Callbacks.SyncRequestReceived = SyncRequestReceived;
                // server.Settings.PresharedKey = "0000000000000000";
                // server.IdleClientTimeoutSeconds = 10;
                server.Settings.Logger = Logger;
                server.Settings.DebugMessages = debugMessages;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return;
            }
             
            server.Start();

            bool runForever = true;
            List<string> clients;
            string ipPort;
            Dictionary<object, object> metadata;
            bool success = false;

            Console.WriteLine("");
            Console.WriteLine("To test timeout recovery, send a message from a client with an integer");
            Console.WriteLine("as payload indicating the number of seconds the server should wait");
            Console.WriteLine("before responding."); 
            Console.WriteLine("");

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
                        Console.WriteLine("  debug               enable/disable debug (currently " + server.Settings.DebugMessages + ")");
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
                        if (!server.Send(ipPort, userInput, metadata)) Console.WriteLine("Failed");
                        Console.WriteLine(success);
                        break;

                    case "send md large":
                        ipPort = InputString("IP:port:", lastIpPort, false);
                        metadata = new Dictionary<object, object>();
                        for (int i = 0; i < 100000; i++) metadata.Add(i, i);
                        if (!server.Send(ipPort, "Hello!", metadata)) Console.WriteLine("Failed");
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
                        success = server.SendAsync(ipPort, Encoding.UTF8.GetBytes(userInput), metadata).Result;
                        if (!success) Console.WriteLine("Failed");
                        break;

                    case "sendandwait":
                        SendAndWait();
                        break;

                    case "sendempty":
                        ipPort = InputString("IP:port:", lastIpPort, false);
                        metadata = InputDictionary();
                        if (!server.Send(ipPort, "", metadata)) Console.WriteLine("Failed");
                        break;

                    case "sendandwait empty":
                        SendAndWaitEmpty();
                        break;

                    case "remove":
                        ipPort = InputString("IP:port:", lastIpPort, false);
                        server.DisconnectClient(ipPort);
                        break;

                    case "psk":
                        server.Settings.PresharedKey = InputString("Preshared key:", "1234567812345678", false);
                        break;

                    case "stats":
                        Console.WriteLine(server.Statistics.ToString());
                        break;

                    case "stats reset":
                        server.Statistics.Reset();
                        break;
                         
                    case "debug":
                        server.Settings.DebugMessages = !server.Settings.DebugMessages;
                        Console.WriteLine("Debug set to: " + server.Settings.DebugMessages);
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

        private static Dictionary<object, object> InputDictionary()
        {
            Console.WriteLine("Build metadata, press ENTER on 'Key' to exit");

            Dictionary<object, object> ret = new Dictionary<object, object>();

            while (true)
            {
                Console.Write("Key   : ");
                string key = Console.ReadLine();
                if (String.IsNullOrEmpty(key)) return ret;

                Console.Write("Value : ");
                string val = Console.ReadLine();
                ret.Add(key, val);
            }
        }

        private static void ClientConnected(object sender, ConnectionEventArgs args)
        {
            lastIpPort = args.IpPort;
            Console.WriteLine("Client connected: " + args.IpPort);
        }

        private static void ClientDisconnected(object sender, DisconnectionEventArgs args)
        {
            Console.WriteLine("Client disconnected: " + args.IpPort + ": " + args.Reason.ToString());
        }

        private static void MessageReceived(object sender, MessageReceivedEventArgs args)
        {
            lastIpPort = args.IpPort;
            Console.Write("Message received from " + args.IpPort + ": ");
            if (args.Data != null) Console.WriteLine(Encoding.UTF8.GetString(args.Data));
            else Console.WriteLine("[null]");

            if (args.Metadata != null && args.Metadata.Count > 0)
            {
                Console.WriteLine("Metadata:");
                foreach (KeyValuePair<object, object> curr in args.Metadata)
                {
                    Console.WriteLine("  " + curr.Key.ToString() + ": " + curr.Value.ToString());
                }
            }
        }

        private static SyncResponse SyncRequestReceived(SyncRequest req)
        {
            Console.Write("Message received from " + req.IpPort + ": ");
            string resp = "Here is your response!";

            if (req.Data != null)
            {
                string dataString = Encoding.UTF8.GetString(req.Data);
                if (Int32.TryParse(dataString, out int seconds))
                {
                    Console.WriteLine(dataString + " [Responding in " + seconds + " seconds]");
                    resp += "  I waited " + seconds + " seconds to send this to you.";
                    Task.Delay((seconds * 1000)).Wait();
                }
                else
                {
                    Console.WriteLine(dataString);
                }
            }
            else
            {
                Console.WriteLine("[null]");
            }

            if (req.Metadata != null && req.Metadata.Count > 0)
            {
                Console.WriteLine("Metadata:");
                foreach (KeyValuePair<object, object> curr in req.Metadata)
                {
                    Console.WriteLine("  " + curr.Key.ToString() + ": " + curr.Value.ToString());
                }
            }

            Dictionary<object, object> retMetadata = new Dictionary<object, object>();
            retMetadata.Add("foo", "bar");
            retMetadata.Add("bar", "baz");
             
            return new SyncResponse(req, retMetadata, resp);
        }

        private static void SendAndWait()
        {
            string ipPort = InputString("IP:port:", lastIpPort, false);
            string userInput = InputString("Data:", null, false);
            int timeoutMs = InputInteger("Timeout (milliseconds):", 5000, true, false);

            try
            {
                SyncResponse resp = server.SendAndWait(timeoutMs, ipPort, userInput);
                if (resp.Metadata != null && resp.Metadata.Count > 0)
                {
                    Console.WriteLine("Metadata:");
                    foreach (KeyValuePair<object, object> curr in resp.Metadata)
                    {
                        Console.WriteLine("  " + curr.Key.ToString() + ": " + curr.Value.ToString());
                    }
                }

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

            Dictionary<object, object> dict = new Dictionary<object, object>();
            dict.Add("foo", "bar");

            try
            {
                SyncResponse resp = server.SendAndWait(timeoutMs, ipPort, "", dict);
                if (resp.Metadata != null && resp.Metadata.Count > 0)
                {
                    Console.WriteLine("Metadata:");
                    foreach (KeyValuePair<object, object> curr in resp.Metadata)
                    {
                        Console.WriteLine("  " + curr.Key.ToString() + ": " + curr.Value.ToString());
                    }
                }

                Console.WriteLine("Response: " + Encoding.UTF8.GetString(resp.Data));
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.ToString());
            }
        }

        private static void Logger(Severity sev, string msg)
        {
            Console.WriteLine("[" + sev.ToString().PadRight(9) + "] " + msg);
        }
    }
}
