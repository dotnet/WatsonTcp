using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WatsonTcp;

namespace TestServerStream
{
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
        private static string _LastIpPort = null;

        private static void Main(string[] args)
        {
            _ServerIp = InputString("Server IP:", "127.0.0.1", false);
            _ServerPort = InputInteger("Server port:", 9000, true, false);
            _Ssl = InputBoolean("Use SSL:", false);

            if (!_Ssl)
            {
                _Server = new WatsonTcpServer(_ServerIp, _ServerPort);
            }
            else
            {
                _CertFile = InputString("Certificate file:", "test.pfx", false);
                _CertPass = InputString("Certificate password:", "password", false);
                _AcceptInvalidCerts = InputBoolean("Accept Invalid Certs:", true);
                _MutualAuth = InputBoolean("Mutually authenticate:", true);

                _Server = new WatsonTcpServer(_ServerIp, _ServerPort, _CertFile, _CertPass);
                _Server.Settings.AcceptInvalidCertificates = _AcceptInvalidCerts;
                _Server.Settings.MutuallyAuthenticate = _MutualAuth;
            }

            _Server.Events.ClientConnected += ClientConnected;
            _Server.Events.ClientDisconnected += ClientDisconnected;
            _Server.Events.StreamReceived += StreamReceived;
            _Server.Callbacks.SyncRequestReceived = SyncRequestReceived;
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
                Dictionary<object, object> metadata;
                bool success = false;

                List<string> clients;
                string ipPort;

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
                        Console.WriteLine("  sendasync      send message to a client asynchronously");
                        Console.WriteLine("  sendasync md   send message with metadata to a client asynchronously");
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
                        ipPort = InputString("IP:port:", _LastIpPort, false); 
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = _Server.Send(ipPort, data.Length, ms);
                        Console.WriteLine(success);
                        break;

                    case "send md":
                        ipPort = InputString("IP:port:", _LastIpPort, false); 
                        metadata = InputDictionary();
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = _Server.Send(ipPort, data.Length, ms, metadata);
                        Console.WriteLine(success);
                        break;

                    case "sendasync":
                        ipPort = InputString("IP:port:", _LastIpPort, false); 
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        data = Encoding.UTF8.GetBytes(userInput); 
                        ms = new MemoryStream(data);
                        ms.Seek(0, SeekOrigin.Begin); 
                        success = _Server.SendAsync(ipPort, data.Length, ms).Result;
                        Console.WriteLine(success);
                        break;

                    case "sendasync md":
                        ipPort = InputString("IP:port:", _LastIpPort, false);
                        metadata = InputDictionary();
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = _Server.SendAsync(ipPort, data.Length, ms, metadata).Result;
                        Console.WriteLine(success);
                        break;

                    case "sendandwait":
                        SendAndWait();
                        break;

                    case "remove":
                        ipPort = InputString("IP:port:", _LastIpPort, false);
                        _Server.DisconnectClient(ipPort);
                        break;

                    case "remove all":
                        _Server.DisconnectClients();
                        break;

                    case "psk":
                        _Server.Settings.PresharedKey = InputString("Preshared key:", "1234567812345678", false);
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
            _LastIpPort = args.IpPort;
            Console.WriteLine("Client connected: " + args.IpPort);
        } 

        private static void ClientDisconnected(object sender, DisconnectionEventArgs args)
        {
            Console.WriteLine("Client disconnected: " + args.IpPort + ": " + args.Reason.ToString());
        }
         
        private static void StreamReceived(object sender, StreamReceivedEventArgs args)
        {
            try
            {
                Console.Write("Stream from " + args.IpPort + " [" + args.ContentLength + " bytes]: ");

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
                        foreach (KeyValuePair<object, object> curr in args.Metadata)
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

        private static SyncResponse SyncRequestReceived(SyncRequest req)
        {
            Console.Write("Synchronous request received from " + req.IpPort + ": ");
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
            Console.WriteLine("Sending synchronous response");
            return new SyncResponse(req, retMetadata, "Here is your response!");
        }

        private static void SendAndWait()
        {
            string ipPort = InputString("IP:port:", _LastIpPort, false);
            string userInput = InputString("Data:", null, false);
            int timeoutMs = InputInteger("Timeout (milliseconds):", 5000, true, false);

            try
            {
                SyncResponse resp = _Server.SendAndWait(timeoutMs, ipPort, userInput); 
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
            string ipPort = InputString("IP:port:", _LastIpPort, false);
            int timeoutMs = InputInteger("Timeout (milliseconds):", 5000, true, false);

            Dictionary<object, object> dict = new Dictionary<object, object>();
            dict.Add("foo", "bar");

            try
            {
                SyncResponse resp = _Server.SendAndWait(timeoutMs, ipPort, "", dict);
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