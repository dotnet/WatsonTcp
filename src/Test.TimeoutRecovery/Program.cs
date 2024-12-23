namespace Test.TimeoutRecovery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using GetSomeInput;
    using WatsonTcp;

    internal class TestServer
    {
        private static string _ServerIp = "";
        private static int _ServerPort = 0;
        private static bool _UseSsl = false;
        private static WatsonTcpServer _Server = null;
        private static string _CertFile = "";
        private static string _CertPassword = "";
        private static bool _DebugMessages = true;
        private static bool _AcceptInvalidCertificates = true;
        private static bool _MutuallyAuthenticate = true;
        private static Guid _LastGuid = Guid.Empty;

        private static async Task Main(string[] args)
        {
            _ServerIp = Inputty.GetString("Server IP:", "127.0.0.1", false);
            _ServerPort = Inputty.GetInteger("Server port:", 9000, true, false);
            _UseSsl = Inputty.GetBoolean("Use SSL:", false);

            try
            {
                if (!_UseSsl)
                {
                    _Server = new WatsonTcpServer(_ServerIp, _ServerPort);
                }
                else
                {
                    _CertFile = Inputty.GetString("Certificate file:", "test.pfx", false);
                    _CertPassword = Inputty.GetString("Certificate password:", "password", false);
                    _AcceptInvalidCertificates = Inputty.GetBoolean("Accept invalid certs:", true);
                    _MutuallyAuthenticate = Inputty.GetBoolean("Mutually authenticate:", false);

                    _Server = new WatsonTcpServer(_ServerIp, _ServerPort, _CertFile, _CertPassword);
                    _Server.Settings.AcceptInvalidCertificates = _AcceptInvalidCertificates;
                    _Server.Settings.MutuallyAuthenticate = _MutuallyAuthenticate;
                }

                _Server.Events.ClientConnected += ClientConnected;
                _Server.Events.ClientDisconnected += ClientDisconnected;
                _Server.Events.MessageReceived += MessageReceived;
                _Server.Callbacks.SyncRequestReceivedAsync = SyncRequestReceived;
                // server.Settings.PresharedKey = "0000000000000000";
                // server.IdleClientTimeoutSeconds = 10;
                _Server.Settings.Logger = Logger;
                _Server.Settings.DebugMessages = _DebugMessages;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return;
            }
             
            _Server.Start();

            bool runForever = true;
            List<ClientMetadata> clients;
            Guid guid;
            Dictionary<string, object> metadata;

            Console.WriteLine("");
            Console.WriteLine("To test timeout recovery, send a message from a client with an integer");
            Console.WriteLine("as payload indicating the number of seconds the server should wait");
            Console.WriteLine("before responding."); 
            Console.WriteLine("");

            while (runForever)
            {
                string userInput = Inputty.GetString("Command [? for help]:", null, false);

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
                        Console.WriteLine("  sendandwait         send message and wait for a response");
                        Console.WriteLine("  sendempty           send empty message with metadata");
                        Console.WriteLine("  sendandwait empty   send empty message with metadata and wait for a response");
                        Console.WriteLine("  remove              disconnect client");
                        Console.WriteLine("  psk                 set preshared key");
                        Console.WriteLine("  stats               display server statistics");
                        Console.WriteLine("  stats reset         reset statistics other than start time and uptime"); 
                        Console.WriteLine("  debug               enable/disable debug (currently " + _Server.Settings.DebugMessages + ")");
                        break;

                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "list":
                        clients = _Server.ListClients().ToList();
                        if (clients != null && clients.Count > 0)
                        {
                            Console.WriteLine("");
                            Console.WriteLine("Clients");
                            Console.WriteLine("-------");
                            foreach (ClientMetadata curr in clients)
                            {
                                Console.WriteLine(curr.Guid.ToString() + ": " + curr.IpPort);
                            }
                            Console.WriteLine("");
                        }
                        else
                        {
                            Console.WriteLine("None");
                        }
                        break;

                    case "dispose":
                        _Server.Dispose();
                        break;

                    case "send":
                        guid = Guid.Parse(Inputty.GetString("GUID:", _LastGuid.ToString(), false));
                        userInput = Inputty.GetString("Data:", null, false);
                        if (!await _Server.SendAsync(guid, userInput)) 
                            Console.WriteLine("Failed");
                        break;

                    case "send md":
                        guid = Guid.Parse(Inputty.GetString("GUID:", _LastGuid.ToString(), false));
                        userInput = Inputty.GetString("Data:", null, false);
                        metadata = Inputty.GetDictionary<string, object>("Key  :", "Value:");;
                        if (!await _Server.SendAsync(guid, userInput, metadata)) 
                            Console.WriteLine("Failed");
                        break;

                    case "send md large":
                        guid = Guid.Parse(Inputty.GetString("GUID:", _LastGuid.ToString(), false));
                        metadata = new Dictionary<string, object>();
                        for (int i = 0; i < 100000; i++) metadata.Add(i.ToString(), i);
                        if (!await _Server.SendAsync(guid, "Hello!", metadata)) 
                            Console.WriteLine("Failed");
                        break;

                    case "sendandwait":
                        await SendAndWait();
                        break;

                    case "sendempty":
                        guid = Guid.Parse(Inputty.GetString("GUID:", _LastGuid.ToString(), false));
                        metadata = Inputty.GetDictionary<string, object>("Key  :", "Value:");;
                        if (!await _Server.SendAsync(guid, "", metadata)) 
                            Console.WriteLine("Failed");
                        break;

                    case "sendandwait empty":
                        await SendAndWaitEmpty();
                        break;

                    case "remove":
                        guid = Guid.Parse(Inputty.GetString("GUID:", _LastGuid.ToString(), false));
                        await _Server.DisconnectClientAsync(guid);
                        break;

                    case "psk":
                        _Server.Settings.PresharedKey = Inputty.GetString("Preshared key:", "1234567812345678", false);
                        break;

                    case "stats":
                        Console.WriteLine(_Server.Statistics.ToString());
                        break;

                    case "stats reset":
                        _Server.Statistics.Reset();
                        break;
                         
                    case "debug":
                        _Server.Settings.DebugMessages = !_Server.Settings.DebugMessages;
                        Console.WriteLine("Debug set to: " + _Server.Settings.DebugMessages);
                        break;

                    default:
                        break;
                }
            }
        }

        private static void ClientConnected(object sender, ConnectionEventArgs args)
        {
            _LastGuid = args.Client.Guid;
            Console.WriteLine("Client connected: " + args.Client.ToString());
        }

        private static void ClientDisconnected(object sender, DisconnectionEventArgs args)
        {
            Console.WriteLine("Client disconnected: " + args.Client.ToString() + ": " + args.Reason.ToString());
        }

        private static void MessageReceived(object sender, MessageReceivedEventArgs args)
        {
            _LastGuid = args.Client.Guid;
            Console.Write("Message received from " + args.Client.ToString() + ": ");
            if (args.Data != null) Console.WriteLine(Encoding.UTF8.GetString(args.Data));
            else Console.WriteLine("[null]");

            if (args.Metadata != null && args.Metadata.Count > 0)
            {
                Console.WriteLine("Metadata:");
                foreach (KeyValuePair<string, object> curr in args.Metadata)
                {
                    Console.WriteLine("  " + curr.Key.ToString() + ": " + curr.Value.ToString());
                }
            }
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private static async Task<SyncResponse> SyncRequestReceived(SyncRequest req)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            _LastGuid = req.Client.Guid;
            Console.Write("Message received from " + req.Client.ToString() + ": ");
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
                foreach (KeyValuePair<string, object> curr in req.Metadata)
                {
                    Console.WriteLine("  " + curr.Key.ToString() + ": " + curr.Value.ToString());
                }
            }

            Dictionary<string, object> retMetadata = new Dictionary<string, object>();
            retMetadata.Add("foo", "bar");
            retMetadata.Add("bar", "baz");
             
            return new SyncResponse(req, retMetadata, resp);
        }

        private static async Task SendAndWait()
        {
            Guid guid = Guid.Parse(Inputty.GetString("GUID:", _LastGuid.ToString(), false));
            string userInput = Inputty.GetString("Data:", null, false);
            int timeoutMs = Inputty.GetInteger("Timeout (milliseconds):", 5000, true, false);

            try
            {
                SyncResponse resp = await _Server.SendAndWaitAsync(timeoutMs, guid, userInput);
                if (resp.Metadata != null && resp.Metadata.Count > 0)
                {
                    Console.WriteLine("Metadata:");
                    foreach (KeyValuePair<string, object> curr in resp.Metadata)
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

        private static async Task SendAndWaitEmpty()
        {
            Guid guid = Guid.Parse(Inputty.GetString("GUID:", _LastGuid.ToString(), false));
            int timeoutMs = Inputty.GetInteger("Timeout (milliseconds):", 5000, true, false);

            Dictionary<string, object> dict = new Dictionary<string, object>();
            dict.Add("foo", "bar");

            try
            {
                SyncResponse resp = await _Server.SendAndWaitAsync(timeoutMs, guid, "", dict);
                if (resp.Metadata != null && resp.Metadata.Count > 0)
                {
                    Console.WriteLine("Metadata:");
                    foreach (KeyValuePair<string, object> curr in resp.Metadata)
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
