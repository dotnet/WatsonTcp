using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using WatsonTcp;

namespace TestClient
{
    internal class TestClient
    {
        private static string serverIp = "";
        private static int serverPort = 0;
        private static bool useSsl = false;
        private static string certFile = "";
        private static string certPass = "";
        private static bool debugMessages = true;
        private static bool acceptInvalidCerts = true;
        private static bool mutualAuthentication = true;
        private static WatsonTcpClient client = null;
        private static string presharedKey = null;
        
        private static void Main(string[] args)
        {
            InitializeClient();

            bool runForever = true;
            Dictionary<object, object> metadata; 
            bool success;

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
                        Console.WriteLine("  send                send message to server");
                        Console.WriteLine("  send md             send message with metadata to server");
                        Console.WriteLine("  sendasync           send message to server asynchronously");
                        Console.WriteLine("  sendasync md        send message with metadata to server asynchronously");
                        Console.WriteLine("  sendandwait         send message and wait for a response");
                        Console.WriteLine("  sendempty           send empty message with metadata");
                        Console.WriteLine("  sendandwait empty   send empty message with metadata and wait for a response");
                        Console.WriteLine("  status              show if client connected");
                        Console.WriteLine("  dispose             dispose of the connection");
                        Console.WriteLine("  connect             connect to the server if not connected");
                        Console.WriteLine("  reconnect           disconnect if connected, then reconnect");
                        Console.WriteLine("  psk                 set the preshared key");
                        Console.WriteLine("  auth                authenticate using the preshared key");
                        Console.WriteLine("  stats               display client statistics");
                        Console.WriteLine("  stats reset         reset statistics other than start time and uptime"); 
                        Console.WriteLine("  debug               enable/disable debug (currently " + client.Settings.DebugMessages + ")");
                        break;

                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "send":
                        userInput = InputString("Data:", null, false);
                        if (!client.Send(Encoding.UTF8.GetBytes(userInput))) Console.WriteLine("Failed");
                        break;

                    case "send md":
                        userInput = InputString("Data:", null, false);
                        metadata = InputDictionary();
                        if (!client.Send(metadata, Encoding.UTF8.GetBytes(userInput))) Console.WriteLine("Failed");
                        break;

                    case "send md large":
                        metadata = new Dictionary<object, object>();
                        for (int i = 0; i < 100000; i++) metadata.Add(i, i);
                        if (!client.Send(metadata, "Hello!")) Console.WriteLine("Failed");
                        break;

                    case "sendasync":
                        userInput = InputString("Data:", null, false);
                        success = client.SendAsync(Encoding.UTF8.GetBytes(userInput)).Result;
                        if (!success) Console.WriteLine("Failed");
                        break;

                    case "sendasync md":
                        userInput = InputString("Data:", null, false);
                        metadata = InputDictionary();
                        success = client.SendAsync(metadata, Encoding.UTF8.GetBytes(userInput)).Result;
                        if (!success) Console.WriteLine("Failed");
                        break;

                    case "sendandwait":
                        SendAndWait();
                        break;

                    case "sendempty":
                        metadata = InputDictionary();
                        success = client.Send(metadata);
                        if (!success) Console.WriteLine("Failed");
                        break;

                    case "sendandwait empty":
                        SendAndWaitEmpty();
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
                            client.Events.ServerConnected += ServerConnected;
                            client.Events.ServerDisconnected += ServerDisconnected;
                            client.Events.MessageReceived += MessageReceived;
                            client.Start();
                        }
                        break;

                    case "reconnect":
                        ConnectClient();
                        break;

                    case "psk":
                        presharedKey = InputString("Preshared key:", "1234567812345678", false);
                        break;

                    case "auth":
                        client.Authenticate(presharedKey);
                        break;

                    case "stats":
                        Console.WriteLine(client.Statistics.ToString());
                        break;

                    case "stats reset":
                        client.Statistics.Reset();
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
            serverIp = InputString("Server IP:", "127.0.0.1", false);
            serverPort = InputInteger("Server port:", 9000, true, false);
            useSsl = InputBoolean("Use SSL:", false);
             
            if (useSsl)
            {
                bool supplyCert = InputBoolean("Supply SSL certificate:", false);

                if (supplyCert)
                {
                    certFile = InputString("Certificate file:", "test.pfx", false);
                    certPass = InputString("Certificate password:", "password", false);
                }

                acceptInvalidCerts = InputBoolean("Accept invalid certs:", true);
                mutualAuthentication = InputBoolean("Mutually authenticate:", false); 
            }

            ConnectClient();
        }

        private static void ConnectClient()
        { 
            if (client != null) client.Dispose();

            if (!useSsl)
            {
                client = new WatsonTcpClient(serverIp, serverPort);
            }
            else
            {
                client = new WatsonTcpClient(serverIp, serverPort, certFile, certPass);
                client.Settings.AcceptInvalidCertificates = acceptInvalidCerts;
                client.Settings.MutuallyAuthenticate = mutualAuthentication;
            }

            client.Events.AuthenticationFailure += AuthenticationFailure;
            client.Events.AuthenticationSucceeded += AuthenticationSucceeded;
            client.Events.ServerConnected += ServerConnected;
            client.Events.ServerDisconnected += ServerDisconnected;
            client.Events.MessageReceived += MessageReceived;

            client.Callbacks.SyncRequestReceived = SyncRequestReceived;
            client.Callbacks.AuthenticationRequested = AuthenticationRequested;

            client.Settings.DebugMessages = debugMessages;
            client.Settings.Logger = Logger;

            client.Keepalive.EnableTcpKeepAlives = true;
            client.Keepalive.TcpKeepAliveInterval = 1;
            client.Keepalive.TcpKeepAliveTime = 1;

            // client.Start();
            Task startClient = client.StartAsync();
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

        private static string AuthenticationRequested()
        {
            // return "0000000000000000";
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("Server requests authentication");
            Console.WriteLine("Press ENTER and THEN enter your preshared key");
            if (String.IsNullOrEmpty(presharedKey)) presharedKey = InputString("Preshared key:", "1234567812345678", false);
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
         
        private static void MessageReceived(object sender, MessageReceivedFromServerEventArgs args)
        {
            Console.Write("Message from server: ");
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
            if (req.Data != null) Console.WriteLine(Encoding.UTF8.GetString(req.Data));
            else Console.WriteLine("[null]");

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

            // Uncomment to test timeout
            // Task.Delay(10000).Wait();
            return new SyncResponse(req, retMetadata, "Here is your response!");
        }

        private static void ServerConnected(object sender, EventArgs args) 
        {
            Console.WriteLine("Server connected"); 
        }

        private static void ServerDisconnected(object sender, EventArgs args)
        {
            Console.WriteLine("Server disconnected");
        }

        private static void SendAndWait()
        {
            string userInput = InputString("Data:", null, false);
            int timeoutMs = InputInteger("Timeout (milliseconds):", 5000, true, false);
            Dictionary<object, object> metadata = new Dictionary<object, object>();
            metadata.Add("foo", "bar");

            try
            {
                SyncResponse resp = client.SendAndWait(metadata, timeoutMs, userInput);
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
            int timeoutMs = InputInteger("Timeout (milliseconds):", 5000, true, false);

            Dictionary<object, object> dict = new Dictionary<object, object>();
            dict.Add("foo", "bar");

            try
            {
                SyncResponse resp = client.SendAndWait(dict, timeoutMs);
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

        private static void Logger(string msg)
        {
            Console.WriteLine(msg);
        }
    }
}