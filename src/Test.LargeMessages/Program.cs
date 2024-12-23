namespace TestLargeMessages
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using WatsonTcp;

    class Program
    {
        static WatsonTcpServer _Server = null;
        static WatsonTcpClient _Client = null;

        static async Task Main(string[] args)
        {
            _Server = new WatsonTcpServer("127.0.0.1", 9000);
            _Server.Events.ClientConnected += ServerClientConnected;
            _Server.Events.ClientDisconnected += ServerClientDisconnected;
            _Server.Events.MessageReceived += ServerMessageReceived;
            // server.StreamReceived = ServerStreamReceived;
            // server.Debug = true;
            _Server.Start();

            _Client = new WatsonTcpClient("127.0.0.1", 9000);
            _Client.Events.ServerConnected += ServerConnected;
            _Client.Events.ServerDisconnected += ServerDisconnected;
            _Client.Events.MessageReceived += MessageReceived;
            // client.Events.StreamReceived = StreamReceived;
            // client.Debug = true;
            _Client.Connect();

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
                await _Client.SendAsync(Encoding.UTF8.GetBytes(randomString));
            } 

            Console.WriteLine("");
            Console.WriteLine("---");

            Guid guid = _Server.ListClients().ToList()[0].Guid;
            Console.WriteLine("Sending messages from server to client " + guid.ToString() + "...");

            for (int i = 0; i < msgCount; i++)
            {
                string randomString = RandomString(msgSize);
                string md5 = Md5(randomString);
                Console.WriteLine("Server sending " + msgSize + " bytes: MD5 " + md5);
                await _Server.SendAsync(guid, randomString);
            }

            Console.WriteLine("");
            Console.WriteLine("---");
            Console.WriteLine("Press ENTER to exit");
            Console.ReadLine();
        }

        #region Server-Callbacks
         
        static void ServerClientConnected(object sender, ConnectionEventArgs args) 
        {
            Console.WriteLine("Server detected connection from client: " + args.Client.ToString());
        }
         
        static void ServerClientDisconnected(object sender, DisconnectionEventArgs args) 
        {
            Console.WriteLine("Server detected disconnection from client: " + args.Client.ToString() + " [" + args.Reason.ToString() + "]");
        }
         
        static void ServerMessageReceived(object sender, MessageReceivedEventArgs args) 
        {
            Console.WriteLine("Server received " + args.Data.Length + " bytes from " + args.Client.ToString() + ": MD5 " + Md5(args.Data));
        }
         
        static void ServerStreamReceived(object sender, StreamReceivedEventArgs args)
        {
            Console.Write("Server received " + args.ContentLength + " bytes from " + args.Client.ToString() + ": MD5 " + Md5(args.DataStream)); 
        }

        #endregion

        #region Client-Callbacks

        private static void ServerConnected(object sender, ConnectionEventArgs args)
        {
            Console.WriteLine("Server connected");
        }

        private static void ServerDisconnected(object sender, DisconnectionEventArgs args)
        {
            Console.WriteLine("Server disconnected: " + args.Reason.ToString());
        }

        static void MessageReceived(object sender, MessageReceivedEventArgs args) 
        {
            Console.WriteLine("Client received " + args.Data.Length + " bytes from server: MD5 " + Md5(args.Data));
        }
         
        static void StreamReceived(object sender, StreamReceivedEventArgs args) 
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
            Random random = new Random((int)DateTime.UtcNow.Ticks);
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
