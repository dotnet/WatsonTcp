namespace TestClientStream
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using GetSomeInput;
    using WatsonTcp;

    internal class TestClientStream
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

        private static async Task Main(string[] args)
        {
            serverIp = Inputty.GetString("Server IP:", "127.0.0.1", false);
            serverPort = Inputty.GetInteger("Server port:", 9000, true, false);
            useSsl = Inputty.GetBoolean("Use SSL:", false);

            InitializeClient();

            bool runForever = true;
            Dictionary<string, object> metadata;
            bool success;

            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine();
                byte[] data = null;
                MemoryStream ms = null; 

                if (String.IsNullOrEmpty(userInput))
                {
                    continue;
                }

                switch (userInput)
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?              help (this menu)");
                        Console.WriteLine("  q              quit");
                        Console.WriteLine("  cls            clear screen");
                        Console.WriteLine("  send           send message to server");
                        Console.WriteLine("  send md        send message with metadata to server");
                        Console.WriteLine("  sendandwait    send message and wait for a response");
                        Console.WriteLine("  status         show if client connected");
                        Console.WriteLine("  dispose        dispose of the client");
                        Console.WriteLine("  connect        connect to the server");
                        Console.WriteLine("  disconnect     disconnect from the server");
                        Console.WriteLine("  psk            set the preshared key");
                        Console.WriteLine("  auth           authenticate using the preshared key");
                        Console.WriteLine("  debug          enable/disable debug");
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
                        success = await client.SendAsync(data.Length, ms);
                        Console.WriteLine(success);
                        break;

                    case "send md":
                        metadata = Inputty.GetDictionary<string, object>("Key  :", "Value:");;
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = await client.SendAsync(data.Length, ms, metadata);
                        Console.WriteLine(success);
                        break;

                    case "sendandwait":
                        await SendAndWait();
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
                        client.Connect(); 
                        break;

                    case "disconnect":
                        client.Disconnect();
                        break;

                    case "psk":
                        presharedKey = Inputty.GetString("Preshared key:", "1234567812345678", false);
                        break;

                    case "auth":
                        await client.AuthenticateAsync(presharedKey);
                        break;

                    case "debug":
                        client.Settings.DebugMessages = !client.Settings.DebugMessages;
                        Console.WriteLine("Debug set to: " + client.Settings.DebugMessages);
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
                certFile = Inputty.GetString("Certificate file:", "test.pfx", false);
                certPass = Inputty.GetString("Certificate password:", "password", false);
                acceptInvalidCerts = Inputty.GetBoolean("Accept Invalid Certs:", true);
                mutualAuthentication = Inputty.GetBoolean("Mutually authenticate:", true);

                client = new WatsonTcpClient(serverIp, serverPort, certFile, certPass);
                client.Settings.AcceptInvalidCertificates = acceptInvalidCerts;
                client.Settings.MutuallyAuthenticate = mutualAuthentication;
            }

            client.Callbacks.AuthenticationRequested = AuthenticationRequested;
            client.Events.AuthenticationFailure += AuthenticationFailure;
            client.Events.AuthenticationSucceeded += AuthenticationSucceeded;
            client.Events.ServerConnected += ServerConnected;
            client.Events.ServerDisconnected += ServerDisconnected;
            client.Events.StreamReceived += StreamReceived;
            client.Callbacks.SyncRequestReceivedAsync = SyncRequestReceived;
            client.Settings.Logger = Logger;
            // client.Debug = true;
            client.Connect();
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
         
        private static void StreamReceived(object sender, StreamReceivedEventArgs args) 
        {
            try
            {
                Console.Write("Stream from server [" + args.ContentLength + " bytes]: ");

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
                }
                else
                {
                    Console.WriteLine("[null]");
                }
                  
                if (args.Metadata != null && args.Metadata.Count > 0)
                {
                    Console.WriteLine("Metadata:");
                    foreach (KeyValuePair<string, object> curr in args.Metadata)
                    {
                        Console.WriteLine("  " + curr.Key.ToString() + ": " + curr.Value.ToString());
                    }
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
            // Task.Delay(10000).Wait();
            return new SyncResponse(req, retMetadata, "Here is your response!");
        }

        private static string AuthenticationRequested()
        {
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("Server requests authentication");
            Console.WriteLine("Press ENTER and THEN enter your preshared key");
            if (String.IsNullOrEmpty(presharedKey)) presharedKey = Inputty.GetString("Preshared key:", "1234567812345678", false);
            return presharedKey;
        }
         
        private static void AuthenticationSucceeded(object sender, EventArgs args) 
        {
            Console.WriteLine("Authentication succeeded");
        }
         
        private static void AuthenticationFailure(object sender, EventArgs args) 
        {
            Console.WriteLine("Authentication failed");
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
                SyncResponse resp = await client.SendAndWaitAsync(timeoutMs, userInput, metadata);
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
            int timeoutMs = Inputty.GetInteger("Timeout (milliseconds):", 5000, true, false);

            Dictionary<object, object> dict = new Dictionary<object, object>();
            dict.Add("foo", "bar");

            try
            {
                SyncResponse resp = await client.SendAndWaitAsync(timeoutMs, "");
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