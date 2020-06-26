using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using WatsonTcp;

namespace TestLargeMessages
{
    class Program
    {
        static WatsonTcpServer<BlankMetadata> server = null;
        static WatsonTcpClient<BlankMetadata> client = null;

        static void Main(string[] args)
        {
            server = new WatsonTcpServer<BlankMetadata>("127.0.0.1", 9000);
            server.ClientConnected += ServerClientConnected;
            server.ClientDisconnected += ServerClientDisconnected;
            server.MessageReceived += ServerMessageReceived;
            // server.StreamReceived = ServerStreamReceived;
            // server.Debug = true;
            server.Start();

            client = new WatsonTcpClient<BlankMetadata>("127.0.0.1", 9000);
            client.ServerConnected += ServerConnected;
            client.ServerDisconnected += ServerDisconnected;
            client.MessageReceived += MessageReceived;
            // client.StreamReceived = StreamReceived;
            // client.Debug = true;
            client.Start();

            int msgSize = (1024 * 128);
            Console.Write("Message size (default 128KB): ");
            string userInput = Console.ReadLine();
            if (!String.IsNullOrEmpty(userInput)) msgSize = Convert.ToInt32(userInput);

            int msgCount = 4;
            Console.Write("Message count (default 4): ");
            userInput = Console.ReadLine();
            if (!String.IsNullOrEmpty(userInput)) msgCount = Convert.ToInt32(userInput);

            Console.WriteLine("");
            Console.WriteLine("---");
            Console.WriteLine("Sending messages from client to server...");

            for (int i = 0; i < msgCount; i++)
            {
                string randomString = RandomString(msgSize);
                string md5 = Md5(randomString);
                Console.WriteLine("Client sending " + msgSize + " bytes: MD5 " + md5);
                client.Send(Encoding.UTF8.GetBytes(randomString));
            } 

            Console.WriteLine("");
            Console.WriteLine("---");

            string ipPort = server.ListClients().ToList()[0];
            Console.WriteLine("Sending messages from server to client " + ipPort + "...");

            for (int i = 0; i < msgCount; i++)
            {
                string randomString = RandomString(msgSize);
                string md5 = Md5(randomString);
                Console.WriteLine("Server sending " + msgSize + " bytes: MD5 " + md5);
                server.Send(ipPort, Encoding.UTF8.GetBytes(randomString));
            }

            Console.WriteLine("");
            Console.WriteLine("---");
            Console.WriteLine("Press ENTER to exit");
            Console.ReadLine();
        }

        #region Server-Callbacks
         
        static void ServerClientConnected(object sender, ClientConnectedEventArgs args) 
        {
            Console.WriteLine("Server detected connection from client: " + args.IpPort);
        }
         
        static void ServerClientDisconnected(object sender, ClientDisconnectedEventArgs args) 
        {
            Console.WriteLine("Server detected disconnection from client: " + args.IpPort + " [" + args.Reason.ToString() + "]");
        }
         
        static void ServerMessageReceived(object sender, MessageReceivedFromClientEventArgs<BlankMetadata> args) 
        {
            Console.WriteLine("Server received " + args.Data.Length + " bytes from " + args.IpPort + ": MD5 " + Md5(args.Data));
        }
         
        static void ServerStreamReceived(object sender, StreamReceivedFromClientEventArgs<BlankMetadata> args)
        {
            Console.Write("Server received " + args.ContentLength + " bytes from " + args.IpPort + ": MD5 " + Md5(args.DataStream)); 
        }

        #endregion

        #region Client-Callbacks
         
        static void ServerConnected(object sender, EventArgs args) 
        {
            Console.WriteLine("Client detected successful connection to server");
        }
         
        static void ServerDisconnected(object sender, EventArgs args) 
        {
            Console.WriteLine("Client detected disconnection from server");
        }
         
        static void MessageReceived(object sender, MessageReceivedFromServerEventArgs<BlankMetadata> args) 
        {
            Console.WriteLine("Client received " + args.Data.Length + " bytes from server: MD5 " + Md5(args.Data));
        }
         
        static void StreamReceived(object sender, StreamReceivedFromServerEventArgs<BlankMetadata> args) 
        {
            Console.Write("Client received " + args.ContentLength + " bytes from server: MD5 " + Md5(args.DataStream));
        }

        #endregion

        private static string Md5(byte[] data)
        {
            if (data == null) return null;

            MD5 md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(data);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("X2"));
            string ret = sb.ToString();
            return ret;
        }

        private static string Md5(string data)
        {
            if (String.IsNullOrEmpty(data)) return null;

            MD5 md5 = MD5.Create();
            byte[] dataBytes = System.Text.Encoding.ASCII.GetBytes(data);
            byte[] hash = md5.ComputeHash(dataBytes);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("X2"));
            string ret = sb.ToString();
            return ret;
        }

        private static string Md5(Stream stream)
        {
            if (stream == null || !stream.CanRead) return null;

            MD5 md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(stream);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("X2"));
            string ret = sb.ToString();
            return ret;
        }

        private static string RandomString(int numChar)
        {
            string ret = "";
            if (numChar < 1) return null;
            int valid = 0;
            Random random = new Random((int)DateTime.Now.Ticks);
            int num = 0;

            for (int i = 0; i < numChar; i++)
            {
                num = 0;
                valid = 0;
                while (valid == 0)
                {
                    num = random.Next(126);
                    if (((num > 47) && (num < 58)) ||
                        ((num > 64) && (num < 91)) ||
                        ((num > 96) && (num < 123)))
                    {
                        valid = 1;
                    }
                }
                ret += (char)num;
            }

            return ret;
        }
    }
}
