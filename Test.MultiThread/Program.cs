﻿using System;
using System.Security.Cryptography;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WatsonTcp;

namespace TestMultiThread
{
    internal class Program
    {
        private static int _NumIterations = 128;
        private static Random _Random;
        private static int _DataLargeSize = 10485760;
        private static byte[] _DataLargeBytes;
        private static string _DataLargeMd5;
        private static int _DataSmallSize = 128;
        private static byte[] _DataSmallBytes;
        private static string _DataSmallMd5;
        private static int _SendAndWaitInterval = 2000;
        private static bool _Debug = false;

        private static WatsonTcpServer _Server; 
        private static WatsonTcpClient _Client; 
        private static string _ClientIpPort = null;
        private static bool _UseStreams = true;

        private static int _ServerPort = 8000;
        private static int _ServerThreads = 2;
        private static int _ClientThreads = 2;
        private static int _MaxProxiedStreamSize = 524288;
        // private static int _MaxProxiedStreamSize = 524288000;

        private static int _Success = 0;
        private static int _Failure = 0;

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
            _Server.Events.ClientConnected += ServerClientConnected;
            _Server.Events.ClientDisconnected += ServerClientDisconnected;
            if (!_UseStreams) _Server.Events.MessageReceived += ServerMsgReceived; 
            else _Server.Events.StreamReceived += ServerStreamReceived;
            _Server.Callbacks.SyncRequestReceived = ServerSyncRequestReceived;
            _Server.Settings.MaxProxiedStreamSize = _MaxProxiedStreamSize;
            _Server.Settings.Logger = ServerLogger;
            _Server.Settings.DebugMessages = _Debug;
            _Server.Start();
            
            Thread.Sleep(2000);

            _Client = new WatsonTcpClient("localhost", _ServerPort);
            _Client.Events.ServerConnected += ClientServerConnected;
            _Client.Events.ServerDisconnected += ClientServerDisconnected;
            if (!_UseStreams) _Client.Events.MessageReceived += ClientMsgReceived;
            else _Client.Events.StreamReceived += ClientStreamReceived; 
            _Client.Callbacks.SyncRequestReceived = ClientSyncRequestReceived;
            _Client.Settings.MaxProxiedStreamSize = _MaxProxiedStreamSize;
            _Client.Settings.Logger = ClientLogger;
            _Client.Settings.DebugMessages = _Debug;
            _Client.Connect();
            
            Thread.Sleep(2000);

            Console.WriteLine("Press ENTER to exit");

            for (int i = 0; i < _ClientThreads; i++)
            {
                Console.WriteLine("Starting client thread " + i);
                Task.Run(() => ClientTask());
            }

            Console.WriteLine("Press ENTER after completion to view statistics");
            Console.ReadLine();

            Console.WriteLine("Success: " + _Success);
            Console.WriteLine("Failure: " + _Failure);
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
            _Server.Events.ClientConnected += ServerClientConnected;
            _Server.Events.ClientDisconnected += ServerClientDisconnected;
            if (!_UseStreams) _Server.Events.MessageReceived += ServerMsgReceived;
            else _Server.Events.StreamReceived += ServerStreamReceived;
            _Server.Callbacks.SyncRequestReceived = ServerSyncRequestReceived;
            _Server.Settings.MaxProxiedStreamSize = _MaxProxiedStreamSize;
            _Server.Settings.Logger = ServerLogger;
            _Server.Start();

            Thread.Sleep(2000);

            _Client = new WatsonTcpClient("localhost", _ServerPort);
            _Client.Events.ServerConnected += ClientServerConnected;
            _Client.Events.ServerDisconnected += ClientServerDisconnected;
            if (!_UseStreams) _Client.Events.MessageReceived += ClientMsgReceived;
            else _Client.Events.StreamReceived += ClientStreamReceived;
            _Client.Callbacks.SyncRequestReceived = ClientSyncRequestReceived;
            _Client.Settings.MaxProxiedStreamSize = _MaxProxiedStreamSize;
            _Client.Settings.Logger = ClientLogger;
            _Client.Connect();

            while (String.IsNullOrEmpty(_ClientIpPort)) ;

            Thread.Sleep(2000);

            Console.WriteLine("Press ENTER to exit");

            for (int i = 0; i < _ServerThreads; i++)
            {
                Console.WriteLine("Starting server thread " + i);
                Task.Run(() => ServerTask());
            }

            Console.WriteLine("Press ENTER after completion to view statistics");
            Console.ReadLine();

            Console.WriteLine("Success: " + _Success);
            Console.WriteLine("Failure: " + _Failure);
        }

        private static void ClientTask()
        {
            for (int i = 0; i < _NumIterations; i++)
            {
                int waitVal = _Random.Next(0, 12);
                Task.Delay(waitVal).Wait();
                if (waitVal % 3 == 0)
                {
                    Console.WriteLine("[client] " + (i + 1).ToString() + "/" + _NumIterations.ToString() + " Sending large message");
                    _Client.Send(_DataLargeBytes);
                }
                else if (waitVal % 2 == 0)
                {
                    Console.WriteLine("[client] " + (i + 1).ToString() + "/" + _NumIterations.ToString() + " Sending small message");
                    _Client.Send(_DataSmallBytes);
                }
                else
                {
                    Console.WriteLine("[client] " + (i + 1).ToString() + "/" + _NumIterations.ToString() + " Send and wait small message");
                    try
                    {
                        SyncResponse syncResponse = _Client.SendAndWait(_SendAndWaitInterval, _DataSmallBytes);
                        Console.WriteLine("[client] Sync response received");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[client] Sync response not received: " + e.Message);
                    }
                }
            }

            Console.WriteLine("[client] Finished");
        }

        private static void ServerTask()
        {
            for (int i = 0; i < _NumIterations; i++)
            {
                int waitVal = _Random.Next(0, 12);
                Task.Delay(waitVal).Wait();
                if (waitVal % 3 == 0)
                {
                    Console.WriteLine("[server] " + (i + 1).ToString() + "/" + _NumIterations.ToString() + " Sending large message");
                    _Server.Send(_ClientIpPort, _DataLargeBytes);
                }
                else if (waitVal % 2 == 0)
                {
                    Console.WriteLine("[server] " + (i + 1).ToString() + "/" + _NumIterations.ToString() + " Sending small message");
                    _Server.Send(_ClientIpPort, _DataSmallBytes);
                }
                else
                {
                    Console.WriteLine("[server] " + (i + 1).ToString() + "/" + _NumIterations.ToString() + " Send and wait small message");
                    try
                    {
                        SyncResponse syncResponse = _Server.SendAndWait(_SendAndWaitInterval, _ClientIpPort, _DataSmallBytes);
                        Console.WriteLine("[server] Sync response received");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[server] Sync response not received: " + e.Message);
                    }
                }
            }

            Console.WriteLine("[server] Finished");
        }

        private static void ServerClientConnected(object sender, ConnectionEventArgs args) 
        {
            Console.WriteLine("[server] connection from " + args.IpPort);
            _ClientIpPort = args.IpPort;
        }
         
        private static void ServerClientDisconnected(object sender, DisconnectionEventArgs args)
        {
            Console.WriteLine("[server] disconnection from " + args.IpPort + ": " + args.Reason.ToString());
        }

        private static void ServerMsgReceived(object sender, MessageReceivedEventArgs args)
        {
            // Console.WriteLine("[server] msg from " + args.IpPort + ": " + BytesToHex(Md5(args.Data)) + " (" + args.Data.Length + " bytes)");
            try
            {
                string md5 = BytesToHex(Md5(args.Data));
                if (!md5.Equals(_DataLargeMd5) && !md5.Equals(_DataSmallMd5))
                {
                    Interlocked.Increment(ref _Failure);
                    Console.WriteLine("[server] [msg] [async] Data MD5 validation failed");
                }
                else
                {
                    Interlocked.Increment(ref _Success);
                    // Console.WriteLine("[server] [msg] [async] Data MD5 validation success: " + md5);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private static void ServerStreamReceived(object sender, StreamReceivedEventArgs args)
        {
            // Console.WriteLine("[server] stream from " + args.IpPort + ": " + BytesToHex(Md5(args.Data)) + " (" + args.Data.Length + " bytes)");
            try
            {
                string md5 = BytesToHex(Md5(args.Data)); 
                if (!md5.Equals(_DataLargeMd5) && !md5.Equals(_DataSmallMd5))
                {
                    Interlocked.Increment(ref _Failure);
                    Console.WriteLine("[server] [stream] [async] Data MD5 validation failed");
                }
                else
                {
                    Interlocked.Increment(ref _Success);
                    // Console.WriteLine("[server] [stream] [async] Data MD5 validation success: " + md5);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private static SyncResponse ServerSyncRequestReceived(SyncRequest req)
        {
            try
            {
                string md5 = BytesToHex(Md5(req.Data));
                if (!md5.Equals(_DataLargeMd5) && !md5.Equals(_DataSmallMd5))
                {
                    Interlocked.Increment(ref _Failure);
                    Console.WriteLine("[server] [sync] Data MD5 validation failed");
                }
                else
                {
                    Interlocked.Increment(ref _Success);
                    // Console.WriteLine("[server] [sync] Data MD5 validation success: " + md5);
                } 
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString()); 
            }

            return new SyncResponse(req, new byte[0]);
        }

        private static void ClientServerConnected(object sender, ConnectionEventArgs args) 
        {
        }
         
        private static void ClientServerDisconnected(object sender, DisconnectionEventArgs args) 
        {
        }

        private static void ClientMsgReceived(object sender, MessageReceivedEventArgs args)
        {  
            try
            {
                string md5 = BytesToHex(Md5(args.Data));
                if (!md5.Equals(_DataLargeMd5) && !md5.Equals(_DataSmallMd5))
                {
                    Interlocked.Increment(ref _Failure);
                    Console.WriteLine("[client] [msg] [async] Data MD5 validation failed");
                }
                else
                {
                    Interlocked.Increment(ref _Success);
                    // Console.WriteLine("[client] [msg] [async] Data MD5 validation success: " + md5);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private static void ClientStreamReceived(object sender, StreamReceivedEventArgs args)
        { 
            try
            {
                string md5 = BytesToHex(Md5(args.Data)); 
                if (!md5.Equals(_DataLargeMd5) && !md5.Equals(_DataSmallMd5))
                {
                    Interlocked.Increment(ref _Failure);
                    Console.WriteLine("[client] [stream] [async] Data MD5 validation failed");
                }
                else
                {
                    Interlocked.Increment(ref _Success);
                    // Console.WriteLine("[client] [stream] [async] Data MD5 validation success: " + md5);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private static SyncResponse ClientSyncRequestReceived(SyncRequest req)
        {
            try
            {
                string md5 = BytesToHex(Md5(req.Data));
                if (!md5.Equals(_DataLargeMd5) && !md5.Equals(_DataSmallMd5))
                {
                    Interlocked.Increment(ref _Failure);
                    Console.WriteLine("[client] [sync] Data MD5 validation failed");
                }
                else
                {
                    Interlocked.Increment(ref _Success);
                    // Console.WriteLine("[client] [sync] Data MD5 validation success: " + md5);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            return new SyncResponse(req, new byte[0]);
        }

        private static byte[] InitByteArray(int count, byte val)
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

        private static string BytesToHex(byte[] bytes)
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

        private static byte[] ReadFromStream(Stream stream, long count, int bufferLen)
        {
            if (count <= 0) return new byte[0];
            if (bufferLen <= 0) throw new ArgumentException("Buffer must be greater than zero bytes.");
            byte[] buffer = new byte[bufferLen];

            int read = 0;
            long bytesRemaining = count;
            MemoryStream ms = new MemoryStream();

            while (bytesRemaining > 0)
            {
                if (bufferLen > bytesRemaining) buffer = new byte[bytesRemaining];

                read = stream.Read(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    ms.Write(buffer, 0, read);
                    bytesRemaining -= read;
                }
                else
                {
                    throw new IOException("Could not read from supplied stream.");
                }
            }

            byte[] data = ms.ToArray();
            return data;
        }

        private static void ServerLogger(Severity sev, string msg)
        {
            Console.WriteLine("[Server] [" + sev.ToString().PadRight(9) + "] " + msg);
        }

        private static void ClientLogger(Severity sev, string msg)
        {
            Console.WriteLine("[Client] [" + sev.ToString().PadRight(9) + "] " + msg);
        }
    }
}