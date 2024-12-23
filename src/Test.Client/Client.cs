namespace TestClient
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Threading.Tasks;
    using GetSomeInput;
    using WatsonTcp;

    internal class TestClient
    {
        private static string _ServerIp = "";
        private static int _ServerPort = 0;
        private static bool _Ssl = false;
        private static string _CertFile = "";
        private static string _CertPass = "";
        private static bool _DebugMessages = true;
        private static bool _AcceptInvalidCerts = true;
        private static bool _MutualAuth = true;
        private static WatsonTcpClient _Client = null;
        private static string _PresharedKey = null;
        
        private static async Task Main(string[] args)
        {
            InitializeClient();

            bool runForever = true;
            Dictionary<string, object> metadata;

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
                        Console.WriteLine("  send                send message to server");
                        Console.WriteLine("  send offset         send message to server with offset");
                        Console.WriteLine("  send md             send message with metadata to server");
                        Console.WriteLine("  sendandwait         send message and wait for a response");
                        Console.WriteLine("  sendempty           send empty message with metadata");
                        Console.WriteLine("  sendandwait empty   send empty message with metadata and wait for a response");
                        Console.WriteLine("  status              show if client connected");
                        Console.WriteLine("  dispose             dispose of the client");
                        Console.WriteLine("  connect             connect to the server");
                        Console.WriteLine("  disconnect          disconnect from the server"); 
                        Console.WriteLine("  psk                 set the preshared key");
                        Console.WriteLine("  auth                authenticate using the preshared key");
                        Console.WriteLine("  stats               display client statistics");
                        Console.WriteLine("  stats reset         reset statistics other than start time and uptime"); 
                        Console.WriteLine("  debug               enable/disable debug");
                        break;

                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "send":
                        userInput = Inputty.GetString("Data:", null, false);
                        if (!await _Client.SendAsync(Encoding.UTF8.GetBytes(userInput))) Console.WriteLine("Failed");
                        break;

                    case "send offset":
                        userInput = Inputty.GetString("Data:", null, false);
                        int offset = Inputty.GetInteger("Offset:", 0, true, true);
                        if (!await _Client.SendAsync(Encoding.UTF8.GetBytes(userInput), null, offset)) Console.WriteLine("Failed");
                        break;

                    case "send md":
                        userInput = Inputty.GetString("Data:", null, false);
                        metadata = Inputty.GetDictionary<string, object>("Key  :", "Value:");
                        metadata.Add("time", DateTime.UtcNow);
                        if (!await _Client.SendAsync(Encoding.UTF8.GetBytes(userInput), metadata)) Console.WriteLine("Failed");
                        break;

                    case "send md large":
                        metadata = new Dictionary<string, object>();
                        for (int i = 0; i < 100000; i++) metadata.Add(i.ToString(), i);
                        if (!await _Client.SendAsync("Hello!", metadata)) Console.WriteLine("Failed");
                        break;

                    case "sendandwait":
                        await SendAndWait();
                        break;

                    case "sendempty":
                        metadata = Inputty.GetDictionary<string, object>("Key  :", "Value:");
                        if (!await _Client.SendAsync("", metadata)) Console.WriteLine("Failed");
                        break;

                    case "sendandwait empty":
                        await SendAndWaitEmpty();
                        break;

                    case "status":
                        if (_Client == null)
                        {
                            Console.WriteLine("Connected: False (null)");
                        }
                        else
                        {
                            Console.WriteLine("Connected: " + _Client.Connected);
                        }

                        break;

                    case "dispose":
                        _Client.Dispose();
                        break;

                    case "connect": 
                        _Client.Connect(); 
                        break;

                    case "disconnect":
                        _Client.Disconnect();
                        break;
                         
                    case "psk":
                        _PresharedKey = Inputty.GetString("Preshared key:", "1234567812345678", false);
                        break;

                    case "auth":
                        await _Client.AuthenticateAsync(_PresharedKey);
                        break;

                    case "stats":
                        Console.WriteLine(_Client.Statistics.ToString());
                        break;

                    case "stats reset":
                        _Client.Statistics.Reset();
                        break;
                         
                    case "debug":
                        _Client.Settings.DebugMessages = !_Client.Settings.DebugMessages;
                        Console.WriteLine("Debug set to: " + _Client.Settings.DebugMessages);
                        break;

                    default:
                        break;
                }
            }
        }

        private static void InitializeClient()
        {
            _ServerIp = Inputty.GetString("Server IP:", "localhost", false);
            _ServerPort = Inputty.GetInteger("Server port:", 9000, true, false);
            _Ssl = Inputty.GetBoolean("Use SSL:", false);
             
            if (_Ssl)
            {
                bool supplyCert = Inputty.GetBoolean("Supply SSL certificate:", false);

                if (supplyCert)
                {
                    _CertFile = Inputty.GetString("Certificate file:", "test.pfx", false);
                    _CertPass = Inputty.GetString("Certificate password:", "password", false);
                }

                _AcceptInvalidCerts = Inputty.GetBoolean("Accept invalid certs:", true);
                _MutualAuth = Inputty.GetBoolean("Mutually authenticate:", false); 
            }

            ConnectClient();
        }

        private static void ConnectClient()
        { 
            if (_Client != null) _Client.Dispose();

            if (!_Ssl)
            {
                _Client = new WatsonTcpClient(_ServerIp, _ServerPort);
            }
            else
            {
                _Client = new WatsonTcpClient(_ServerIp, _ServerPort, _CertFile, _CertPass);
                _Client.Settings.AcceptInvalidCertificates = _AcceptInvalidCerts;
                _Client.Settings.MutuallyAuthenticate = _MutualAuth;
            }

            _Client.Events.AuthenticationFailure += AuthenticationFailure;
            _Client.Events.AuthenticationSucceeded += AuthenticationSucceeded;
            _Client.Events.ServerConnected += ServerConnected;
            _Client.Events.ServerDisconnected += ServerDisconnected;
            _Client.Events.MessageReceived += MessageReceived;
            _Client.Events.ExceptionEncountered += ExceptionEncountered;

            _Client.Callbacks.SyncRequestReceivedAsync = SyncRequestReceived;
            _Client.Callbacks.AuthenticationRequested = AuthenticationRequested;

            // _Client.Settings.IdleServerTimeoutMs = 5000;
            _Client.Settings.DebugMessages = _DebugMessages;
            _Client.Settings.Logger = Logger;
            _Client.Settings.NoDelay = true;

            _Client.Keepalive.EnableTcpKeepAlives = true;
            _Client.Keepalive.TcpKeepAliveInterval = 1;
            _Client.Keepalive.TcpKeepAliveTime = 1;
            _Client.Keepalive.TcpKeepAliveRetryCount = 3;

            _Client.Connect();
        }

        private static void ExceptionEncountered(object sender, ExceptionEventArgs e)
        {
            Console.WriteLine(e.Exception.ToString());
        }

        private static string AuthenticationRequested()
        {
            // return "0000000000000000";
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("Server requests authentication");
            Console.WriteLine("Press ENTER and THEN enter your preshared key");
            _PresharedKey = Inputty.GetString("Preshared key:", "1234567812345678", false);
            _Client.Settings.PresharedKey = _PresharedKey;
            return _PresharedKey;
        }
         
        private static void AuthenticationSucceeded(object sender, EventArgs args) 
        {
            Console.WriteLine("Authentication succeeded");
        }
         
        private static void AuthenticationFailure(object sender, EventArgs args) 
        {
            Console.WriteLine("Authentication failed");
        }
         
        private static void MessageReceived(object sender, MessageReceivedEventArgs args)
        {
            Console.Write("Message from server: ");
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

                if (args.Metadata.ContainsKey("foo")) Console.WriteLine(args.Metadata["foo"]);
            }
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private static async Task<SyncResponse> SyncRequestReceived(SyncRequest req)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            Console.Write("Message received from server: ");
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
            // await Task.Delay(10000);
            return new SyncResponse(req, retMetadata, "Here is your response!");
        }

        private static void ServerConnected(object sender, ConnectionEventArgs args) 
        {
            Console.WriteLine("Server connected"); 
        }

        private static void ServerDisconnected(object sender, DisconnectionEventArgs args)
        {
            Console.WriteLine("Server disconnected: " + args.Reason.ToString());
        }

        private static async Task SendAndWait()
        {
            string userInput = Inputty.GetString("Data:", null, false);
            int timeoutMs = Inputty.GetInteger("Timeout (milliseconds):", 5000, true, false);
            Dictionary<string, object> metadata = new Dictionary<string, object>();
            metadata.Add("foo", "bar");

            try
            {
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                SyncResponse resp = await _Client.SendAndWaitAsync(timeoutMs, userInput, metadata);
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
                Console.WriteLine("Server responded in {0} ms/{1} ticks.",stopwatch.ElapsedMilliseconds,stopwatch.ElapsedTicks);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.ToString());
            }
        }

        private static async Task SendAndWaitEmpty()
        { 
            int timeoutMs = Inputty.GetInteger("Timeout (milliseconds):", 5000, true, false);

            Dictionary<string, object> dict = new Dictionary<string, object>();
            dict.Add("foo", "bar");

            try
            {
                SyncResponse resp = await _Client.SendAndWaitAsync(timeoutMs, "", dict);
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