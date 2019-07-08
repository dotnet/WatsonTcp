namespace TestParallel
{
    using System;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonTcp;

    internal class Program
    {
        private static readonly int serverPort = 8000;
        private static readonly int clientThreads = 8;
        private static readonly int numIterations = 10000;
        private static Random rng;
        private static byte[] data;
        private static WatsonTcpServer server;

        private static void Main(string[] args)
        {
            rng = new Random((int)DateTime.Now.Ticks);
            data = InitByteArray(262144, 0x00);
            Console.WriteLine("Data MD5: " + BytesToHex(Md5(data)));
            Console.WriteLine("Starting in 3 seconds...");

            server = new WatsonTcpServer(null, serverPort)
            {
                ClientConnected = ServerClientConnected,
                ClientDisconnected = ServerClientDisconnected,
                MessageReceived = ServerMsgReceived
            };

            server.Start();

            Thread.Sleep(3000);

            Console.WriteLine("Press ENTER to exit");

            for (int i = 0; i < clientThreads; i++)
            {
                Task.Run(() => ClientTask());
            }

            Console.ReadLine();
        }

        private static void ClientTask()
        {
            using (WatsonTcpClient client = new WatsonTcpClient("localhost", serverPort))
            {
                client.ServerConnected = ClientServerConnected;
                client.ServerDisconnected = ClientServerDisconnected;
                client.MessageReceived = ClientMsgReceived;
                client.Start();

                for (int i = 0; i < numIterations; i++)
                {
                    Task.Delay(rng.Next(0, 25)).Wait();
                    client.Send(data);
                }
            }

            Console.WriteLine("[client] finished");
        }

        private static bool ServerClientConnected(string ipPort)
        {
            Console.WriteLine("[server] connection from " + ipPort);
            return true;
        }

        private static bool ServerClientDisconnected(string ipPort)
        {
            Console.WriteLine("[server] disconnection from " + ipPort);
            return true;
        }

        private static bool ServerMsgReceived(string ipPort, byte[] data)
        {
            Console.WriteLine("[server] msg from " + ipPort + ": " + BytesToHex(Md5(data)) + " (" + data.Length + " bytes)");
            return true;
        }

        private static bool ClientServerConnected()
        {
            return true;
        }

        private static bool ClientServerDisconnected()
        {
            return true;
        }

        private static bool ClientMsgReceived(byte[] data)
        {
            Console.WriteLine("[server] msg from server: " + BytesToHex(Md5(data)) + " (" + data.Length + " bytes)");
            return true;
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

            return BitConverter.ToString(bytes).Replace("-", String.Empty);
        }
    }
}
