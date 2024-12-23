namespace TestMultiClient
{
    using ConcurrentList;
    using System;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonTcp;

    internal class Program
    {
        private static int serverPort = 9000;
        private static WatsonTcpServer server = null;
        private static int clientThreads = 16;
        private static int numIterations = 1000;
        private static int connectionCount = 0;
        private static ConcurrentList<string> connections = new ConcurrentList<string>();
        private static bool clientsStarted = false;

        private static Random rng;
        private static byte[] data;

        private static async Task Main(string[] args)
        {
            rng = new Random((int)DateTime.UtcNow.Ticks);
            data = InitByteArray(65536, 0x00);
            Console.WriteLine("Data MD5: " + BytesToHex(Md5(data)));

            Console.WriteLine("Starting server");
            server = new WatsonTcpServer(null, serverPort);
            server.Events.ClientConnected += ServerClientConnected;
            server.Events.ClientDisconnected += ServerClientDisconnected;
            server.Events.MessageReceived += ServerMsgReceived;
            server.Start();

            await Task.Delay(3000);

            Console.WriteLine("Starting clients");
            for (int i = 0; i < clientThreads; i++)
            {
                Console.WriteLine("Starting client " + i);
                await Task.Run(() => ClientTask());
            }

            Console.WriteLine("Press ENTER to exit");
            Console.ReadLine();
        }

        private static async Task ClientTask()
        {
            Console.WriteLine("ClientTask entering");
            using (WatsonTcpClient client = new WatsonTcpClient("localhost", serverPort))
            {
                client.Events.ServerConnected += ClientServerConnected;
                client.Events.ServerDisconnected += ClientServerDisconnected;
                client.Events.MessageReceived += ClientMsgReceived;
                client.Connect();

                while (!clientsStarted)
                {
                    await Task.Delay(100);
                }

                for (int i = 0; i < numIterations; i++)
                {
                    Task.Delay(rng.Next(0, 1000)).Wait();
                    await client.SendAsync(data);
                }
            }

            Console.WriteLine("[client] finished");
        }
         
        private static void ServerClientConnected(object sender, ConnectionEventArgs args) 
        {
            connectionCount++;
            Console.WriteLine("[server] connection from " + args.Client.ToString() + " (now " + connectionCount + ")");

            if (connectionCount >= clientThreads)
            {
                clientsStarted = true;
            }

            connections.Add(args.Client.ToString());
        }
         
        private static void ServerClientDisconnected(object sender, DisconnectionEventArgs args) 
        {
            connectionCount--;
            Console.WriteLine("[server] disconnection from " + args.Client.ToString() + " [now " + connectionCount + "]: " + args.Reason.ToString());
        }
         
        private static void ServerMsgReceived(object sender, MessageReceivedEventArgs args) 
        {
        }
         
        private static void ClientServerConnected(object sender, ConnectionEventArgs args) 
        {
        }
         
        private static void ClientServerDisconnected(object sender, DisconnectionEventArgs args) 
        {
        }
         
        private static void ClientMsgReceived(object sender, MessageReceivedEventArgs args) 
        {
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

            using (MD5 m = MD5.Create())
            {
                return m.ComputeHash(data);
            }
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