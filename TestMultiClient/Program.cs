using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WatsonTcp;
using ConcurrentList;

namespace TestMultiClient
{
    class Program
    {
        static int serverPort = 9000;
        static WatsonTcpServer server = null;
        static int clientThreads = 16;
        static int numIterations = 1000;
        static int connectionCount = 0;
        static ConcurrentList<string> connections = new ConcurrentList<string>();
        static bool clientsStarted = false;

        static Random rng;
        static byte[] data;
        
        static void Main(string[] args)
        {
            rng = new Random((int)DateTime.Now.Ticks);
            data = InitByteArray(65536, 0x00);
            Console.WriteLine("Data MD5: " + BytesToHex(Md5(data)));

            Console.WriteLine("Starting server");
            server = new WatsonTcpServer(null, serverPort);
            server.ClientConnected = ServerClientConnected;
            server.ClientDisconnected = ServerClientDisconnected;
            server.MessageReceived = ServerMsgReceived;
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

        static void ClientTask()
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

        static bool ServerClientConnected(string ipPort)
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

        static bool ServerClientDisconnected(string ipPort)
        {
            connectionCount--;
            Console.WriteLine("[server] disconnection from " + ipPort + " (now " + connectionCount + ")");
            return true;
        }

        static bool ServerMsgReceived(string ipPort, byte[] data)
        {
            // Console.WriteLine("[server] msg from " + ipPort + ": " + BytesToHex(Md5(data)) + " (" + data.Length + " bytes)");
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
            // Console.WriteLine("[server] msg from server: " + BytesToHex(Md5(data)) + " (" + data.Length + " bytes)");
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
