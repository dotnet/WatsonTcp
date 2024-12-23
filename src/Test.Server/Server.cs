namespace TestServer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using GetSomeInput;
    using WatsonTcp;

    internal class TestServer
    {
        private static string _ServerIp = "";
        private static int _ServerPort = 0;
        private static bool _Ssl = false;
        private static WatsonTcpServer _Server = null;
        private static string _CertFile = "";
        private static string _CertPass = "";
        private static bool _DebugMessages = true;
        private static bool _AcceptInvalidCerts = true;
        private static bool _MutualAuth = true;
        private static Guid _LastGuid = Guid.Empty;

        private static async Task Main(string[] args)
        {
            _ServerIp = Inputty.GetString("Server IP:", "localhost", false);
            _ServerPort = Inputty.GetInteger("Server port:", 9000, true, false);
            _Ssl = Inputty.GetBoolean("Use SSL:", false);

            try
            {
                if (!_Ssl)
                {
                    _Server = new WatsonTcpServer(_ServerIp, _ServerPort);
                }
                else
                {
                    _CertFile = Inputty.GetString("Certificate file:", "test.pfx", false);
                    _CertPass = Inputty.GetString("Certificate password:", "password", false);
                    _AcceptInvalidCerts = Inputty.GetBoolean("Accept invalid certs:", true);
                    _MutualAuth = Inputty.GetBoolean("Mutually authenticate:", false);

                    _Server = new WatsonTcpServer(_ServerIp, _ServerPort, _CertFile, _CertPass);
                    _Server.Settings.AcceptInvalidCertificates = _AcceptInvalidCerts;
                    _Server.Settings.MutuallyAuthenticate = _MutualAuth;
                }

                _Server.Events.ClientConnected += ClientConnected;
                _Server.Events.ClientDisconnected += ClientDisconnected;
                _Server.Events.MessageReceived += MessageReceived;
                _Server.Events.ServerStarted += ServerStarted;
                _Server.Events.ServerStopped += ServerStopped;
                _Server.Events.ExceptionEncountered += ExceptionEncountered;

                _Server.Callbacks.SyncRequestReceivedAsync = SyncRequestReceived;
                
                _Server.Settings.IdleClientTimeoutSeconds = 10;
                // _Server.Settings.PresharedKey = "0000000000000000";
                _Server.Settings.Logger = Logger;
                _Server.Settings.DebugMessages = _DebugMessages;
                _Server.Settings.NoDelay = true;

                _Server.Keepalive.EnableTcpKeepAlives = true;
                _Server.Keepalive.TcpKeepAliveInterval = 1;
                _Server.Keepalive.TcpKeepAliveTime = 1;
                _Server.Keepalive.TcpKeepAliveRetryCount = 3;
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
            MessageStatus reason = MessageStatus.Removed;
            Dictionary<string, object> metadata;

            while (runForever)
            {
                string userInput = Inputty.GetString("Command [? for help]:", null, false);
                 
                switch (userInput)
                {
                    case "?":
                        bool listening = (_Server != null ? _Server.IsListening : false);
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?                   help (this menu)");
                        Console.WriteLine("  q                   quit");
                        Console.WriteLine("  cls                 clear screen");
                        Console.WriteLine("  start               start listening for connections (listening: " + listening.ToString() + ")");
                        Console.WriteLine("  stop                stop listening for connections  (listening: " + listening.ToString() + ")");
                        Console.WriteLine("  list                list clients");
                        Console.WriteLine("  dispose             dispose of the server");
                        Console.WriteLine("  send                send message to client");
                        Console.WriteLine("  send offset         send message to client with offset");
                        Console.WriteLine("  send md             send message with metadata to client");
                        Console.WriteLine("  sendandwait         send message and wait for a response");
                        Console.WriteLine("  sendempty           send empty message with metadata");
                        Console.WriteLine("  sendandwait empty   send empty message with metadata and wait for a response");
                        Console.WriteLine("  remove              disconnect client");
                        Console.WriteLine("  remove all          disconnect all clients");
                        Console.WriteLine("  psk                 set preshared key");
                        Console.WriteLine("  stats               display server statistics");
                        Console.WriteLine("  stats reset         reset statistics other than start time and uptime"); 
                        Console.WriteLine("  debug               enable/disable debug");
                        break;

                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "start":
                        _Server.Start();
                        break;

                    case "stop":
                        _Server.Stop();
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

                    case "send offset":
                        guid = Guid.Parse(Inputty.GetString("GUID:", _LastGuid.ToString(), false));
                        userInput = Inputty.GetString("Data:", null, false);
                        int offset = Inputty.GetInteger("Offset:", 0, true, true);
                        if (!await _Server.SendAsync(guid, Encoding.UTF8.GetBytes(userInput), null, offset)) 
                            Console.WriteLine("Failed");
                        break;

                    case "send10":
                        guid = Guid.Parse(Inputty.GetString("GUID:", _LastGuid.ToString(), false));
                        userInput = Inputty.GetString("Data:", null, false);
                        for (int i = 0; i < 10; i++)
                        {
                            Console.WriteLine("Sending " + i);
                            if (!await _Server.SendAsync(guid, userInput + "[" + i.ToString() + "]")) 
                                    Console.WriteLine("Failed");
                        }
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
                        Console.WriteLine("Valid disconnect reasons: Removed, Normal, Shutdown, Timeout");
                        reason = (MessageStatus)(Enum.Parse(typeof(MessageStatus), Inputty.GetString("Disconnect reason:", "Removed", false)));
                        await _Server.DisconnectClientAsync(guid, reason);
                        break;

                    case "remove all":
                        await _Server.DisconnectClientsAsync();
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

        private static void ExceptionEncountered(object sender, ExceptionEventArgs e)
        {
            Console.WriteLine(e.Exception.ToString());
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
            Console.Write(args.Data.Length + " byte message from " + args.Client.ToString() + ": ");
            if (args.Data != null) Console.WriteLine(Encoding.UTF8.GetString(args.Data));
            else Console.WriteLine("[null]");

            if (args.Metadata != null)
            {
                Console.Write("Metadata: ");
                if (args.Metadata.Count < 1)
                {
                    Console.WriteLine("(none)");
                }
                else
                {
                    Console.WriteLine(args.Metadata.Count);
                    foreach (KeyValuePair<string, object> curr in args.Metadata)
                    {
                        Console.WriteLine("  " + curr.Key.ToString() + ": " + curr.Value.ToString());
                    }
                }
            }
        }

        private static void ServerStarted(object sender, EventArgs args)
        {
            Console.WriteLine("Server started");
        }

        private static void ServerStopped(object sender, EventArgs args)
        {
            Console.WriteLine("Server stopped");
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private static async Task<SyncResponse> SyncRequestReceived(SyncRequest req)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            _LastGuid = req.Client.Guid;
            Console.Write("Synchronous request received from " + req.Client.ToString() + ": ");
            if (req.Data != null) Console.WriteLine(Encoding.UTF8.GetString(req.Data));
            else Console.WriteLine("[null]");

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

            // Uncomment to test timeout
            // Task.Delay(10000).Wait();
            Console.WriteLine("Sending synchronous response");
            return new SyncResponse(req, retMetadata, "Here is your response!");
        }

        private static async Task SendAndWait()
        {
            Guid guid = Guid.Parse(Inputty.GetString("GUID:", _LastGuid.ToString(), false));
            string userInput = Inputty.GetString("Data:", null, false);
            int timeoutMs = Inputty.GetInteger("Timeout (milliseconds):", 5000, true, false);

            try
            {
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                SyncResponse resp = await _Server.SendAndWaitAsync(timeoutMs, guid, userInput);
                stopwatch.Stop();
                if (resp.Metadata != null && resp.Metadata.Count > 0)
                {
                    Console.WriteLine("Metadata:");
                    foreach (KeyValuePair<string, object> curr in resp.Metadata)
                    {
                        Console.WriteLine("  " + curr.Key.ToString() + ": " + curr.Value.ToString());
                    }
                }

                Console.WriteLine("Response: " + Encoding.UTF8.GetString(resp.Data));
                Console.WriteLine("Client responded in {0} ms/{1} ticks.", stopwatch.ElapsedMilliseconds, stopwatch.ElapsedTicks);
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