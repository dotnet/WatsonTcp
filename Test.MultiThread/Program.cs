using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using WatsonTcp;

namespace TestMultiThread
{
    internal class Program
    {
        private static int _ServerPort = 8000;
        private static int _ServerThreads = 4;
        private static int _ClientThreads = 4;
        private static int _NumIterations = 256;
        private static Random _Random;
        private static int _DataLargeSize = 10485760;
        private static byte[] _DataLargeBytes;
        private static string _DataLargeMd5;
        private static int _DataSmallSize = 128;
        private static byte[] _DataSmallBytes;
        private static string _DataSmallMd5;
        private static int _SendAndWaitInterval = 5000;
        private static bool _Debug = false;

        private static WatsonTcpServer _Server;
        private static WatsonTcpClient _Client;
        private static string _ClientIpPort = null;
         
        private static void Main(string[] args)
        {
            Console.WriteLine("1: Client to server");
            Console.WriteLine("2: Server to client");
            Console.Write("Test [1/2]: ");
            int testNum = Convert.ToInt32(Console.ReadLine());
            if (testNum == 1) ClientToServer();
            else if (testNum == 2) ServerToClient();
        }

        private static void ClientToServer()
        {
            _Random = new Random((int)DateTime.Now.Ticks);
            _DataLargeBytes = InitByteArray(_DataLargeSize, 0x00);
            _DataLargeMd5 = BytesToHex(Md5(_DataLargeBytes));
            _DataSmallBytes = InitByteArray(_DataSmallSize, 0x00);
            _DataSmallMd5 = BytesToHex(Md5(_DataSmallBytes));
            Console.WriteLine("Large Data MD5: " + _DataLargeMd5);
            Console.WriteLine("Small Data MD5: " + _DataSmallMd5);
            Console.WriteLine("Starting in 3 seconds...");

            _Server = new WatsonTcpServer(null, _ServerPort);
            _Server.ClientConnected += ServerClientConnected;
            _Server.ClientDisconnected += ServerClientDisconnected;
            _Server.MessageReceived += ServerMsgReceived;
            _Server.SyncRequestReceived = ServerSyncRequestReceived;
            _Server.Logger = Console.WriteLine;
            _Server.DebugMessages = _Debug;
            _Server.Start();
            
            Thread.Sleep(2000);

            _Client = new WatsonTcpClient("localhost", _ServerPort);
            _Client.ServerConnected += ClientServerConnected;
            _Client.ServerDisconnected += ClientServerDisconnected;
            _Client.MessageReceived += ClientMsgReceived;
            _Client.SyncRequestReceived = ClientSyncRequestReceived;
            _Client.Logger = Console.WriteLine;
            _Client.DebugMessages = _Debug;
            _Client.Start();
            
            Thread.Sleep(2000);

            Console.WriteLine("Press ENTER to exit");

            for (int i = 0; i < _ClientThreads; i++)
            {
                Console.WriteLine("Starting client thread " + i);
                Task.Run(() => ClientTask());
            }

            Console.ReadLine();
        } 
         
        private static void ServerToClient()
        {
            _Random = new Random((int)DateTime.Now.Ticks);
            _DataLargeBytes = InitByteArray(_DataLargeSize, 0x00);
            _DataLargeMd5 = BytesToHex(Md5(_DataLargeBytes));
            _DataSmallBytes = InitByteArray(_DataSmallSize, 0x00);
            _DataSmallMd5 = BytesToHex(Md5(_DataSmallBytes));
            Console.WriteLine("Large Data MD5: " + _DataLargeMd5);
            Console.WriteLine("Small Data MD5: " + _DataSmallMd5);
            Console.WriteLine("Starting in 3 seconds...");

            _Server = new WatsonTcpServer(null, _ServerPort);
            _Server.ClientConnected += ServerClientConnected;
            _Server.ClientDisconnected += ServerClientDisconnected;
            _Server.MessageReceived += ServerMsgReceived;
            _Server.SyncRequestReceived = ServerSyncRequestReceived;
            _Server.Logger = Console.WriteLine;
            _Server.Start();

            Thread.Sleep(2000);

            _Client = new WatsonTcpClient("localhost", _ServerPort);
            _Client.ServerConnected += ClientServerConnected;
            _Client.ServerDisconnected += ClientServerDisconnected;
            _Client.MessageReceived += ClientMsgReceived;
            _Client.SyncRequestReceived = ClientSyncRequestReceived;
            _Client.Logger = Console.WriteLine;
            _Client.Start();

            while (String.IsNullOrEmpty(_ClientIpPort)) ;

            Thread.Sleep(2000);

            Console.WriteLine("Press ENTER to exit");

            for (int i = 0; i < _ServerThreads; i++)
            {
                Console.WriteLine("Starting server thread " + i);
                Task.Run(() => ServerTask());
            }

            Console.ReadLine();
        }

        private static void ClientTask()
        {
            for (int i = 0; i < _NumIterations; i++)
            {
                int waitVal = _Random.Next(0, 12);
                Task.Delay(waitVal).Wait();
                if (waitVal % 3 == 0)
                {
                    Console.WriteLine(i.ToString() + "/" + _NumIterations.ToString() + " Sending large message");
                    _Client.Send(_DataLargeBytes);
                }
                else if (waitVal % 2 == 0)
                {
                    Console.WriteLine(i.ToString() + "/" + _NumIterations.ToString() + " Sending small message");
                    _Client.Send(_DataSmallBytes);
                }
                else
                {
                    Console.WriteLine(i.ToString() + "/" + _NumIterations.ToString() + " Send and wait small message");
                    _Client.SendAndWait(_SendAndWaitInterval, _DataSmallBytes); 
                }
            }

            Console.WriteLine("[client] finished");
        }

        private static void ServerTask()
        {
            for (int i = 0; i < _NumIterations; i++)
            {
                int waitVal = _Random.Next(0, 12);
                Task.Delay(waitVal).Wait();
                if (waitVal % 3 == 0)
                {
                    Console.WriteLine(i.ToString() + "/" + _NumIterations.ToString() + " Sending large message");
                    _Server.Send(_ClientIpPort, _DataLargeBytes);
                }
                else if (waitVal % 2 == 0)
                {
                    Console.WriteLine(i.ToString() + "/" + _NumIterations.ToString() + " Sending small message");
                    _Server.Send(_ClientIpPort, _DataSmallBytes);
                }
                else
                {
                    Console.WriteLine(i.ToString() + "/" + _NumIterations.ToString() + " Send and wait small message");
                    _Server.SendAndWait(_ClientIpPort, _SendAndWaitInterval, _DataSmallBytes);
                }
            }

            Console.WriteLine("[client] finished");
        }

        private static void ServerClientConnected(object sender, ClientConnectedEventArgs args) 
        {
            Console.WriteLine("[server] connection from " + args.IpPort);
            _ClientIpPort = args.IpPort;
        }
         
        private static void ServerClientDisconnected(object sender, ClientDisconnectedEventArgs args)
        {
            Console.WriteLine("[server] disconnection from " + args.IpPort + ": " + args.Reason.ToString());
        }
         
        private static void ServerMsgReceived(object sender, MessageReceivedFromClientEventArgs args)
        {
            // Console.WriteLine("[server] msg from " + args.IpPort + ": " + BytesToHex(Md5(args.Data)) + " (" + args.Data.Length + " bytes)");
            string md5 = BytesToHex(Md5(args.Data));
            if (!md5.Equals(_DataLargeMd5) && !md5.Equals(_DataSmallMd5)) Console.WriteLine("[async] Data MD5 validation failed");
            // else Console.WriteLine("[async] Data MD5 validation success: " + md5);
        }
         
        private static SyncResponse ServerSyncRequestReceived(SyncRequest req)
        { 
            string md5 = BytesToHex(Md5(req.Data));
            if (!md5.Equals(_DataLargeMd5) && !md5.Equals(_DataSmallMd5)) Console.WriteLine("[sync] Data MD5 validation failed");
            // else Console.WriteLine("[sync] Data MD5 validation success: " + md5);
            return new SyncResponse(req, new byte[0]);
        }

        private static void ClientServerConnected(object sender, EventArgs args) 
        {
        }
         
        private static void ClientServerDisconnected(object sender, EventArgs args) 
        {
        }
         
        private static void ClientMsgReceived(object sender, MessageReceivedFromServerEventArgs args) 
        {
            // Console.WriteLine("[client] msg from server: " + BytesToHex(Md5(args.Data)) + " (" + args.Data.Length + " bytes)");
            string md5 = BytesToHex(Md5(args.Data)); 
            if (!md5.Equals(_DataLargeMd5) && !md5.Equals(_DataSmallMd5)) Console.WriteLine("[async] Data MD5 validation failed");
            // else Console.WriteLine("[async] Data MD5 validation success: " + md5);
        }

        private static SyncResponse ClientSyncRequestReceived(SyncRequest req)
        {
            string md5 = BytesToHex(Md5(req.Data));
            if (!md5.Equals(_DataLargeMd5) && !md5.Equals(_DataSmallMd5)) Console.WriteLine("[sync] Data MD5 validation failed");
            // else Console.WriteLine("[sync] Data MD5 validation success: " + md5);
            return new SyncResponse(req, new byte[0]);
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