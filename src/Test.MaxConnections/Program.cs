namespace Test.MaxConnections
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using GetSomeInput;
    using WatsonTcp;

    internal class Program
    {
        private static string _ServerHostname = "127.0.0.1";
        private static int _ServerPort = 9000;
        private static bool _UseSsl = false;
        private static WatsonTcpServer _Server = null;
        private static string _CertFile = "";
        private static string _CertPassword = "";
        private static bool _Debug = false;
        private static bool _AcceptInvalidCertificates = true;
        private static bool _MutuallyAuthenticate = true;

        private static async Task Main(string[] args)
        {
            _ServerHostname = Inputty.GetString("Server IP:", "127.0.0.1", false);
            _ServerPort = Inputty.GetInteger("Server port:", 9000, true, false);
            _UseSsl = Inputty.GetBoolean("Use SSL:", false);

            try
            {
                if (!_UseSsl)
                {
                    _Server = new WatsonTcpServer(_ServerHostname, _ServerPort);
                }
                else
                {
                    _CertFile = Inputty.GetString("Certificate file:", "test.pfx", false);
                    _CertPassword = Inputty.GetString("Certificate password:", "password", false);
                    _AcceptInvalidCertificates = Inputty.GetBoolean("Accept invalid certs:", true);
                    _MutuallyAuthenticate = Inputty.GetBoolean("Mutually authenticate:", false);

                    _Server = new WatsonTcpServer(_ServerHostname, _ServerPort, _CertFile, _CertPassword);
                    _Server.Settings.AcceptInvalidCertificates = _AcceptInvalidCertificates;
                    _Server.Settings.MutuallyAuthenticate = _MutuallyAuthenticate;
                }

                _Server.Events.ClientConnected += ClientConnected;
                _Server.Events.ClientDisconnected += ClientDisconnected;
                _Server.Events.MessageReceived += MessageReceived;
                // server.IdleClientTimeoutSeconds = 10;
                _Server.Settings.Logger = Logger;
                _Server.Settings.DebugMessages = _Debug;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return;
            }
             
            _Server.Start();

            bool runForever = true;
            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine();

                List<ClientMetadata> clients;
                string guid;
                Dictionary<string, object> metadata;
                bool success = false;

                if (String.IsNullOrEmpty(userInput)) continue;

                switch (userInput)
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?              help (this menu)");
                        Console.WriteLine("  q              quit");
                        Console.WriteLine("  cls            clear screen");
                        Console.WriteLine("  list           list clients");
                        Console.WriteLine("  dispose        dispose of the connection");
                        Console.WriteLine("  send           send message to client");
                        Console.WriteLine("  send md        send message with metadata to client");
                        Console.WriteLine("  remove         disconnect client");
                        Console.WriteLine("  psk            set preshared key");
                        Console.WriteLine("  stats          display server statistics");
                        Console.WriteLine("  stats reset    reset statistics other than start time and uptime");
                        Console.WriteLine("  conn           show connection count");
                        Console.WriteLine("  max            set max connections (currently " + _Server.Settings.MaxConnections + ")");
                        Console.WriteLine("  debug          enable/disable debug (currently " + _Server.Settings.DebugMessages + ")");
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
                        Console.Write("GUID: ");
                        guid = Console.ReadLine();
                        if (String.IsNullOrEmpty(guid)) break;
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        success = await _Server.SendAsync(Guid.Parse(guid), userInput);
                        Console.WriteLine(success);
                        break;

                    case "send md":
                        Console.Write("GUID: ");
                        guid = Console.ReadLine();
                        if (String.IsNullOrEmpty(guid)) break;
                        metadata = Inputty.GetDictionary<string, object>("Key  :", "Value:");;
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        success = await _Server.SendAsync(Guid.Parse(guid), Encoding.UTF8.GetBytes(userInput), metadata);
                        Console.WriteLine(success);
                        break;

                    case "remove":
                        Console.Write("GUID: ");
                        guid = Console.ReadLine();
                        await _Server.DisconnectClientAsync(Guid.Parse(guid));
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

                    case "conn":
                        Console.WriteLine("Connections: " + _Server.Connections);
                        break;

                    case "max":
                        _Server.Settings.MaxConnections = Inputty.GetInteger("Max connections:", 4096, true, false);
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
            Console.WriteLine("Client connected: " + args.Client.ToString());
        }

        private static void ClientDisconnected(object sender, DisconnectionEventArgs args)
        {
            Console.WriteLine("Client disconnected: " + args.Client.ToString() + ": " + args.Reason.ToString());
        }

        private static void MessageReceived(object sender, MessageReceivedEventArgs args)
        {
            Console.WriteLine("Message received from " + args.Client.ToString() + ": " + Encoding.UTF8.GetString(args.Data));
            if (args.Metadata != null && args.Metadata.Count > 0)
            {
                Console.WriteLine("Metadata:");
                foreach (KeyValuePair<string, object> curr in args.Metadata)
                {
                    Console.WriteLine("  " + curr.Key.ToString() + ": " + curr.Value.ToString());
                }
            }
        }

        private static void Logger(Severity sev, string msg)
        {
            Console.WriteLine("[" + sev.ToString().PadRight(9) + "] " + msg);
        }
    }
}
