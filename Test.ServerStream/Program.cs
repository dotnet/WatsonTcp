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
        private static string serverIp = "";
        private static int serverPort = 0;
        private static bool useSsl = false;
        private static WatsonTcpServer<BlankMetadata> server = null;
        private static string certFile = "";
        private static string certPass = "";
        private static bool acceptInvalidCerts = true;
        private static bool mutualAuthentication = true;

        private static void Main(string[] args)
        {
            serverIp = InputString("Server IP:", "127.0.0.1", false);
            serverPort = InputInteger("Server port:", 9000, true, false);
            useSsl = InputBoolean("Use SSL:", false);

            if (!useSsl)
            {
                server = new WatsonTcpServer<BlankMetadata>(serverIp, serverPort);
            }
            else
            {
                certFile = InputString("Certificate file:", "test.pfx", false);
                certPass = InputString("Certificate password:", "password", false);
                acceptInvalidCerts = InputBoolean("Accept Invalid Certs:", true);
                mutualAuthentication = InputBoolean("Mutually authenticate:", true);

                server = new WatsonTcpServer<BlankMetadata>(serverIp, serverPort, certFile, certPass);
                server.AcceptInvalidCertificates = acceptInvalidCerts;
                server.MutuallyAuthenticate = mutualAuthentication;
            }

            server.ClientConnected += ClientConnected;
            server.ClientDisconnected += ClientDisconnected;
            server.StreamReceived += StreamReceived;
            server.Logger = Logger;
            // server.Debug = true;
            server.Start();

            bool runForever = true;
            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine();

                byte[] data = null;
                MemoryStream ms = null;
                BlankMetadata metadata;
                bool success = false;

                List<string> clients;
                string ipPort;

                if (String.IsNullOrEmpty(userInput)) continue;

                switch (userInput)
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?              help (this menu)");
                        Console.WriteLine("  q              quit");
                        Console.WriteLine("  cls            clear screen");
                        Console.WriteLine("  list           list clients");
                        Console.WriteLine("  send           send message to client");
                        Console.WriteLine("  send md        send message with metadata to client");
                        Console.WriteLine("  sendasync      send message to a client asynchronously");
                        Console.WriteLine("  sendasync md   send message with metadata to a client asynchronously");
                        Console.WriteLine("  remove         disconnect client");
                        Console.WriteLine("  psk            set preshared key");
                        Console.WriteLine("  debug          enable/disable debug (currently " + server.DebugMessages + ")");
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

                    case "send":
                        Console.Write("IP:Port: ");
                        ipPort = Console.ReadLine();
                        if (String.IsNullOrEmpty(ipPort)) break;
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = server.Send(ipPort, data.Length, ms);
                        Console.WriteLine(success);
                        break;

                    case "send md":
                        Console.Write("IP:Port: ");
                        ipPort = Console.ReadLine();
                        if (String.IsNullOrEmpty(ipPort)) break;
                        metadata = InputDictionary();
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = server.Send(ipPort, metadata, data.Length, ms);
                        Console.WriteLine(success);
                        break;

                    case "sendasync":
                        Console.Write("IP:Port: ");
                        ipPort = Console.ReadLine();
                        if (String.IsNullOrEmpty(ipPort)) break;
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = server.SendAsync(ipPort, data.Length, ms).Result;
                        Console.WriteLine(success);
                        break;

                    case "sendasync md":
                        Console.Write("IP:Port: ");
                        ipPort = Console.ReadLine();
                        if (String.IsNullOrEmpty(ipPort)) break;
                        metadata = InputDictionary();
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        data = Encoding.UTF8.GetBytes(userInput);
                        ms = new MemoryStream(data);
                        success = server.SendAsync(ipPort, metadata, data.Length, ms).Result;
                        Console.WriteLine(success);
                        break;

                    case "remove":
                        Console.Write("IP:Port: ");
                        ipPort = Console.ReadLine();
                        server.DisconnectClient(ipPort);
                        break;

                    case "psk":
                        server.PresharedKey = InputString("Preshared key:", "1234567812345678", false);
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
            return new BlankMetadata();
            // TODO: Reimplement this example.
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
         
        private static void ClientConnected(object sender, ClientConnectedEventArgs args)
        {
            Console.WriteLine("Client connected: " + args.IpPort);
        } 

        private static void ClientDisconnected(object sender, ClientDisconnectedEventArgs args)
        {
            Console.WriteLine("Client disconnected: " + args.IpPort + ": " + args.Reason.ToString());
        }
         
        private static void StreamReceived(object sender, StreamReceivedFromClientEventArgs<BlankMetadata> args)
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

        private static void Logger(string msg)
        {
            Console.WriteLine(msg);
        }
    }
}