namespace TestServerStream
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using GetSomeInput;
    using WatsonTcp;

    internal class TestServerStream
    {
        private static string _ServerIp = "";
        private static int _ServerPort = 0;
        private static bool _Ssl = false;
        private static WatsonTcpServer _Server = null;
        private static string _CertFile = "";
        private static string _CertPass = "";
        private static bool _AcceptInvalidCerts = true;
        private static bool _MutualAuth = true;
        private static Guid _LastGuid = Guid.Empty;

        private static async Task Main(string[] args)
        {
            _ServerIp = Inputty.GetString("Server IP:", "127.0.0.1", false);
            _ServerPort = Inputty.GetInteger("Server port:", 9000, true, false);
            _Ssl = Inputty.GetBoolean("Use SSL:", false);

            if (!_Ssl)
            {
                _Server = new WatsonTcpServer(_ServerIp, _ServerPort);
            }
            else
            {
                _CertFile = Inputty.GetString("Certificate file:", "test.pfx", false);
                _CertPass = Inputty.GetString("Certificate password:", "password", false);
                _AcceptInvalidCerts = Inputty.GetBoolean("Accept Invalid Certs:", true);
                _MutualAuth = Inputty.GetBoolean("Mutually authenticate:", true);

                _Server = new WatsonTcpServer(_ServerIp, _ServerPort, _CertFile, _CertPass);
                _Server.Settings.AcceptInvalidCertificates = _AcceptInvalidCerts;
                _Server.Settings.MutuallyAuthenticate = _MutualAuth;
            }

            _Server.Events.ClientConnected += ClientConnected;
            _Server.Events.ClientDisconnected += ClientDisconnected;
            _Server.Events.StreamReceived += StreamReceived;
            _Server.Callbacks.SyncRequestReceivedAsync = SyncRequestReceived;
            _Server.Settings.Logger = Logger;
            // server.Debug = true;
            _Server.Start();

            bool runForever = true;
            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine();

                byte[] data = null;
                MemoryStream ms = null;
                Dictionary<string, object> metadata;
                bool success = false;

                List<ClientMetadata> clients;
                Guid guid;

                if (String.IsNullOrEmpty(userInput)) continue;

                switch (userInput)
                {
                    case "?":
                        bool listening = (_Server != null ? _Server.IsListening : false);
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?              help (this menu)");
                        Console.WriteLine("  q              quit");
                        Console.WriteLine("  cls            clear screen");
                        Console.WriteLine("  start          start listening for connections (listening: " + listening.ToString() + ")");
                        Console.WriteLine("  stop           stop listening for connections  (listening: " + listening.ToString() + ")");
                        Console.WriteLine("  list           list clients");
                        Console.WriteLine("  send           send message to client");
                        Console.WriteLine("  send md        send message with metadata to client");
                        Console.WriteLine("  sendandwait    send message and wait for a response");
                        Console.WriteLine("  remove         disconnect client");
                        Console.WriteLine("  remove all     disconnect all clients");
                        Console.WriteLine("  psk            set preshared key");
                        Console.WriteLine("  debug          enable/disable debug");
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

                    case "send":
                        guid = Guid.Parse(Inputty.GetString("GUID:", _LastGuid.ToString(), false)); 
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = await _Server.SendAsync(guid, data.Length, ms);
                        Console.WriteLine(success);
                        break;

                    case "send md":
                        guid = Guid.Parse(Inputty.GetString("GUID:", _LastGuid.ToString(), false)); 
                        metadata = Inputty.GetDictionary<string, object>("Key  :", "Value:");;
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = await _Server.SendAsync(guid, data.Length, ms, metadata);
                        Console.WriteLine(success);
                        break;

                    case "sendandwait":
                        await SendAndWait();
                        break;

                    case "remove":
                        guid = Guid.Parse(Inputty.GetString("GUID:", _LastGuid.ToString(), false));
                        await _Server.DisconnectClientAsync(guid);
                        break;

                    case "remove all":
                        await _Server.DisconnectClientsAsync();
                        break;

                    case "psk":
                        _Server.Settings.PresharedKey = Inputty.GetString("Preshared key:", "1234567812345678", false);
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

        private static void LogException(string method, Exception e)
        {
            Console.WriteLine("");
            Console.WriteLine("An exception was encountered.");
            Console.WriteLine("   Method        : " + method);
            Console.WriteLine("   Type          : " + e.GetType().ToString());
            Console.WriteLine("   Data          : " + e.Data);
            Console.WriteLine("   Inner         : " + e.InnerException);
            Console.WriteLine("   Message       : " + e.Message);
            Console.WriteLine("   Source        : " + e.Source);
            Console.WriteLine("   StackTrace    : " + e.StackTrace);
            Console.WriteLine("");
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
         
        private static void StreamReceived(object sender, StreamReceivedEventArgs args)
        {
            try
            {
                Console.Write("Stream from " + args.Client.ToString() + " [" + args.ContentLength + " bytes]: ");

                int bytesRead = 0;
                int bufferSize = 65536;
                byte[] buffer = new byte[bufferSize];
                long bytesRemaining = args.ContentLength;

                if (args.DataStream != null && args.DataStream.CanRead)
                {
                    while (bytesRemaining > 0)
                    {
                        bytesRead = args.DataStream.Read(buffer, 0, buffer.Length);
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

                    if (args.Metadata != null && args.Metadata.Count > 0)
                    {
                        Console.WriteLine("Metadata:");
                        foreach (KeyValuePair<string, object> curr in args.Metadata)
                        {
                            Console.WriteLine("  " + curr.Key.ToString() + ": " + curr.Value.ToString());
                        }
                    }
                }
                else
                {
                    Console.WriteLine("[null]");
                }
            }
            catch (Exception e)
            {
                LogException("StreamReceived", e);
            }
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private static async Task<SyncResponse> SyncRequestReceived(SyncRequest req)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
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