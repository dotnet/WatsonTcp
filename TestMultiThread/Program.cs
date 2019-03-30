using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using WatsonTcp;

namespace TestMultiThread
{
    class Program
    {
        static int serverPort = 8000;
        static int clientThreads = 128;
        static int numIterations = 10000;
        static Random rng;
        static byte[] data;

        static WatsonTcpServer server;
        static WatsonTcpClient c;

        static void Main(string[] args)
        {
            rng = new Random((int)DateTime.Now.Ticks);
            data = InitByteArray(262144, 0x00);
            Console.WriteLine("Data MD5: " + BytesToHex(Md5(data)));
            Console.WriteLine("Starting in 3 seconds...");

            server = new WatsonTcpServer(null, serverPort);
            server.ClientConnected = ServerClientConnected;
            server.ClientDisconnected = ServerClientDisconnected;
            server.MessageReceived = ServerMsgReceived;
            server.Start();

            Thread.Sleep(3000);

            c = new WatsonTcpClient("localhost", serverPort);
            c.ServerConnected = ClientServerConnected;
            c.ServerDisconnected = ClientServerDisconnected;
            c.MessageReceived = ClientMsgReceived;
            c.Start(); 

            Console.WriteLine("Press ENTER to exit");

            for (int i = 0; i < clientThreads; i++)
            {
                Task.Run(() => ClientTask());
            }

            Console.ReadLine();
        }

        static void ClientTask()
        {
            for (int i = 0; i < numIterations; i++)
            {
                Task.Delay(rng.Next(0, 25)).Wait();
                c.Send(data);
            }

            Console.WriteLine("[client] finished");
        }

        static bool ServerClientConnected(string ipPort)
        {
            Console.WriteLine("[server] connection from " + ipPort);
            return true;
        }

        static bool ServerClientDisconnected(string ipPort)
        {
            Console.WriteLine("[server] disconnection from " + ipPort);
            return true;
        }

        static bool ServerMsgReceived(string ipPort, byte[] data)
        {
            Console.WriteLine("[server] msg from " + ipPort + ": " + BytesToHex(Md5(data)) + " (" + data.Length + " bytes)");
            return true;
        }

        static bool ClientServerConnected()
        {
            return true;
        }

        static bool ClientServerDisconnected()
        {
            return true;
        }

        static bool ClientMsgReceived(byte[] data)
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

        static byte[] Md5(byte[] data)
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
