namespace TestParallel
{
    using System;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using GetSomeInput;
    using WatsonTcp;

    internal class Program
    {
        private static int serverPort = 8000;
        private static int clientThreads = 8;
        private static int numIterations = 10000;
        private static Random rng;
        private static byte[] data;
        private static WatsonTcpServer server;

        private static async Task Main(string[] args)
        {
            rng = new Random((int)DateTime.UtcNow.Ticks);
            data = InitByteArray(262144, 0x00);
            Console.WriteLine("Data MD5: " + BytesToHex(Md5(data)));
            Console.WriteLine("Starting in 3 seconds...");

            server = new WatsonTcpServer(null, serverPort);
            server.Events.ClientConnected += ServerClientConnected;
            server.Events.ClientDisconnected += ServerClientDisconnected;
            server.Events.MessageReceived += ServerMsgReceived;
            server.Start();

            await Task.Delay(3000);

            Console.WriteLine("Press ENTER to exit");

            for (int i = 0; i < clientThreads; i++)
            {
                await Task.Run(() => ClientTask());
            }

            Console.ReadLine();
        }

        private static async Task ClientTask()
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
                    await client.SendAsync(data);
                }
            }

            Console.WriteLine("[client] finished");
        }
         
        private static void ServerClientConnected(object sender, ConnectionEventArgs args) 
        {
            Console.WriteLine("[server] connection from " + args.Client.ToString());
        }
         
        private static void ServerClientDisconnected(object sender, DisconnectionEventArgs args) 
        {
            Console.WriteLine("[server] disconnection from " + args.Client.ToString() + ": " + args.Reason.ToString());
        }
         
        private static void ServerMsgReceived(object sender, MessageReceivedEventArgs args) 
        {
            Console.WriteLine("[server] msg from " + args.Client.ToString() + ": " + BytesToHex(Md5(args.Data)) + " (" + args.Data.Length + " bytes)");
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