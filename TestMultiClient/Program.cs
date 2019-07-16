namespace TestMultiClient
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonTcp;
    using ConcurrentList;

    internal class Program
    {
        private static readonly int serverPort = 9000;
        private static WatsonTcpServer server = null;
        private static readonly int clientThreads = 16;
        private static readonly int numIterations = 1000;
        private static int connectionCount = 0;
        private static readonly ConcurrentList<string> connections = new ConcurrentList<string>();
        private static bool clientsStarted = false;
        private static Random rng;
        private static byte[] data;

        private static void Main()
        {
            rng = new Random((int)DateTime.Now.Ticks);
            data = Common.InitByteArray(65536, 0x00);
            Console.WriteLine("Data MD5: " + Common.BytesToHex(Common.Md5(data)));

            Console.WriteLine("Starting server");
            server = new WatsonTcpServer(null, serverPort)
            {
                ClientConnected = ServerClientConnected,
                ClientDisconnected = ServerClientDisconnected,
                MessageReceived = ServerMsgReceived,
            };

            server.Start();

            Thread.Sleep(3000);

            Console.WriteLine("Starting clients");
            for (int i = 0; i < clientThreads; i++)
            {
                Console.WriteLine("Starting client " + i);
                Task.Run(() => ClientTask());
            }

            Console.WriteLine("Press ENTER to exit");
            Console.ReadLine();
        }

        private static void ClientTask()
        {
            Console.WriteLine("ClientTask entering");
            using (WatsonTcpClient client = new WatsonTcpClient("localhost", serverPort))
            {
                client.ServerConnected = ClientServerConnected;
                client.ServerDisconnected = ClientServerDisconnected;
                client.MessageReceived = ClientMsgReceived;
                client.Start();

                while (!clientsStarted)
                {
                    Thread.Sleep(100);
                }

                for (int i = 0; i < numIterations; i++)
                {
                    Task.Delay(rng.Next(0, 1000)).Wait();
                    client.Send(data);
                }
            }

            Console.WriteLine("[client] finished");
        }

        private static bool ServerClientConnected(string ipPort)
        {
            connectionCount++;
            Console.WriteLine("[server] connection from " + ipPort + " (now " + connectionCount + ")");

            if (connectionCount >= clientThreads)
            {
                clientsStarted = true;
            }

            connections.Add(ipPort);
            return true;
        }

        private static bool ServerClientDisconnected(string ipPort)
        {
            connectionCount--;
            Console.WriteLine("[server] disconnection from " + ipPort + " (now " + connectionCount + ")");
            return true;
        }

        private static bool ServerMsgReceived(string ipPort, byte[] data)
        {
            // Console.WriteLine("[server] msg from " + ipPort + ": " + BytesToHex(Md5(data)) + " (" + data.Length + " bytes)");
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
            // Console.WriteLine("[server] msg from server: " + BytesToHex(Md5(data)) + " (" + data.Length + " bytes)");
            return true;
        }
    }
}
