using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using WatsonTcp;

namespace TestParallel
{
    internal class Program
    {
        private static int serverPort = 8000;
        private static int clientThreads = 8;
        private static int numIterations = 10000;
        private static Random rng;
        private static byte[] data;
        private static WatsonTcpServer server;

        private static void Main(string[] args)
        {
            rng = new Random((int)DateTime.Now.Ticks);
            data = InitByteArray(262144, 0x00);
            Console.WriteLine("Data MD5: " + BytesToHex(Md5(data)));
            Console.WriteLine("Starting in 3 seconds...");

            server = new WatsonTcpServer(null, serverPort);
            server.Events.ClientConnected += ServerClientConnected;
            server.Events.ClientDisconnected += ServerClientDisconnected;
            server.Events.MessageReceived += ServerMsgReceived;
            server.Start();

            Thread.Sleep(3000);

            Console.WriteLine("Press ENTER to exit");

            for (int i = 0; i < clientThreads; i++)
            {
                Task.Run(() => ClientTask());
            }

            Console.ReadLine();
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

        private static void ClientTask()
        {
            using (WatsonTcpClient client = new WatsonTcpClient("localhost", serverPort))
            {
                client.Events.ServerConnected += ClientServerConnected;
                client.Events.ServerDisconnected += ClientServerDisconnected;
                client.Events.MessageReceived += ClientMsgReceived;
                client.Connect();

                for (int i = 0; i < numIterations; i++)
                {
                    Task.Delay(rng.Next(0, 25)).Wait();
                    client.Send(data);
                }
            }

            Console.WriteLine("[client] finished");
        }
         
        private static void ServerClientConnected(object sender, ConnectionEventArgs args) 
        {
            Console.WriteLine("[server] connection from " + args.IpPort);
        }
         
        private static void ServerClientDisconnected(object sender, DisconnectionEventArgs args) 
        {
            Console.WriteLine("[server] disconnection from " + args.IpPort + ": " + args.Reason.ToString());
        }
         
        private static void ServerMsgReceived(object sender, MessageReceivedEventArgs args) 
        {
            Console.WriteLine("[server] msg from " + args.IpPort + ": " + BytesToHex(Md5(args.Data)) + " (" + args.Data.Length + " bytes)");
        }
         
        private static void ClientServerConnected(object sender, ConnectionEventArgs args) 
        {
        }
         
        private static void ClientServerDisconnected(object sender, DisconnectionEventArgs args) 
        {
        }
         
        private static void ClientMsgReceived(object sender, MessageReceivedEventArgs args) 
        {
            Console.WriteLine("[client] msg from server: " + BytesToHex(Md5(args.Data)) + " (" + args.Data.Length + " bytes)");
        }

        public static byte[] InitByteArray(int count, byte val)
        {
            byte[] ret = new byte[count];
            for (int i = 0; i < ret.Length; i++)
            {
                ret[i] = val;
            }
            return ret;
        }

        private static byte[] Md5(byte[] data)
        {
            if (data == null || data.Length < 1)
            {
                return null;
            }

            MD5 m = MD5.Create();
            return m.ComputeHash(data);
        }

        public static string BytesToHex(byte[] bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            if (bytes.Length < 1)
            {
                return null;
            }

            return BitConverter.ToString(bytes).Replace("-", "");
        }
    }
}