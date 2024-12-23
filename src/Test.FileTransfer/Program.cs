namespace Test.FileTransfer
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using GetSomeInput;
    using WatsonTcp;

    internal class Program
    {
        static WatsonTcpServer _Server = null;
        static WatsonTcpClient _Client = null;
        static Guid _ClientGuid = Guid.Empty;
        static int _StreamBufferSize = 4096;

        static async Task Main(string[] args)
        {
            using (_Server = new WatsonTcpServer("127.0.0.1", 9000))
            {
                _Server.Events.ClientConnected += ServerClientConnected;
                _Server.Events.ClientDisconnected += ServerClientDisconnected;
                _Server.Events.StreamReceived += ServerStreamReceived;
                _Server.Start();

                using (_Client = new WatsonTcpClient("127.0.0.1", 9000))
                {
                    _Client.Events.ServerConnected += ClientServerConnected;
                    _Client.Events.ServerDisconnected += ClientServerDisconnected;
                    _Client.Events.StreamReceived += ClientStreamReceived;
                    _Client.Connect();

                    await Worker();
                }
            }
        }

        private static void ServerClientConnected(object sender, ConnectionEventArgs e)
        {
            Console.WriteLine("[server] client connected: " + e.Client.ToString());
            _ClientGuid = e.Client.Guid;
        }

        private static void ServerClientDisconnected(object sender, DisconnectionEventArgs e)
        {
            Console.WriteLine("[server] client disconnected: " + e.Client.ToString());
            _ClientGuid = Guid.Empty;
        }

        private static void ServerStreamReceived(object sender, StreamReceivedEventArgs e)
        {
            long bytesRemaining = e.ContentLength;
            byte[] buffer = new byte[_StreamBufferSize];
            int read = 0;

            if (e.Metadata != null
                && e.Metadata.ContainsKey("filename")
                && e.Metadata.ContainsKey("md5"))
            {
                string filename = e.Metadata["filename"].ToString();
                string md5Request = e.Metadata["md5"].ToString();

                Console.WriteLine("");
                Console.WriteLine("Press ENTER, then supply the destination filename");
                string destFilename = Inputty.GetString("Destination filename:", null, false);

                Console.WriteLine("Incoming file transfer, saving as: " + destFilename + " [md5: " + md5Request + "]");

                using (FileStream fs = new FileStream(destFilename, FileMode.OpenOrCreate, FileAccess.Write))
                {
                    while (bytesRemaining > 0)
                    {
                        if (bytesRemaining >= _StreamBufferSize)
                            read = e.DataStream.Read(buffer, 0, _StreamBufferSize);
                        else
                            read = e.DataStream.Read(buffer, 0, (int)bytesRemaining);

                        if (read > 0)
                        {
                            bytesRemaining -= read;
                            fs.Write(buffer, 0, read);
                        }
                    }
                }

                // check MD5
                string md5File = Md5File(filename);
                Console.WriteLine("MD5 comparison: " + md5Request + "/" + md5File);
                if (!md5Request.Equals(md5File))
                {
                    Console.WriteLine("*** MD5 does not match");
                }
            }
            else
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    while (bytesRemaining > 0)
                    {
                        if (bytesRemaining >= _StreamBufferSize)
                            read = e.DataStream.Read(buffer, 0, _StreamBufferSize);
                        else
                            read = e.DataStream.Read(buffer, 0, (int)bytesRemaining);

                        if (read > 0)
                        {
                            bytesRemaining -= read;
                            ms.Write(buffer, 0, read);
                        }
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    Console.WriteLine("Message: " + Encoding.UTF8.GetString(ms.ToArray()));
                }
            }
        }

        private static void ClientServerConnected(object sender, ConnectionEventArgs e)
        {
            Console.WriteLine("[client] server connected");
        }

        private static void ClientServerDisconnected(object sender, DisconnectionEventArgs e)
        {
            Console.WriteLine("[client] server disconnected");
        }

        private static void ClientStreamReceived(object sender, StreamReceivedEventArgs e)
        {
            long bytesRemaining = e.ContentLength;
            byte[] buffer = new byte[_StreamBufferSize];
            int read = 0;

            if (e.Metadata != null
                && e.Metadata.ContainsKey("filename")
                && e.Metadata.ContainsKey("md5"))
            {
                string filename = e.Metadata["filename"].ToString();
                string md5Request = e.Metadata["md5"].ToString();

                Console.WriteLine("");
                Console.WriteLine("Press ENTER, then supply the destination filename");
                string destFilename = Inputty.GetString("Destination filename:", null, false);

                Console.WriteLine("Incoming file transfer, saving as: " + destFilename + " [md5: " + md5Request + "]");
                                
                using (FileStream fs = new FileStream(destFilename, FileMode.OpenOrCreate, FileAccess.Write))
                {
                    while (bytesRemaining > 0)
                    {
                        if (bytesRemaining >= _StreamBufferSize)
                            read = e.DataStream.Read(buffer, 0, _StreamBufferSize);
                        else
                            read = e.DataStream.Read(buffer, 0, (int)bytesRemaining);

                        if (read > 0)
                        {
                            bytesRemaining -= read;
                            fs.Write(buffer, 0, read);
                        }
                    }
                }

                // check MD5
                string md5File = Md5File(filename);
                Console.WriteLine("MD5 comparison: " + md5Request + "/" + md5File);
                if (!md5Request.Equals(md5File))
                {
                    Console.WriteLine("*** MD5 does not match");
                }
            }
            else
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    while (bytesRemaining > 0)
                    {
                        if (bytesRemaining >= _StreamBufferSize)
                            read = e.DataStream.Read(buffer, 0, _StreamBufferSize);
                        else
                            read = e.DataStream.Read(buffer, 0, (int)bytesRemaining);

                        if (read > 0)
                        {
                            bytesRemaining -= read;
                            ms.Write(buffer, 0, read);
                        }
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    Console.WriteLine("Message: " + Encoding.UTF8.GetString(ms.ToArray()));
                }
            }
        }

        static async Task Worker()
        {
            while (true)
            {
                string userInput = Inputty.GetString("Command [?/help]:", null, false);
                string msg = null;
                byte[] msgBytes = null;
                string filename = null;
                MemoryStream ms = null;
                FileStream fs = null;
                Dictionary<string, object> md = new Dictionary<string, object>();
                long len = 0;

                switch (userInput)
                {
                    case "?":
                        Menu();
                        break;

                    case "q":
                        return;

                    case "c":
                    case "cls":
                        Console.Clear();
                        break;

                    case "server send":
                        msg = Inputty.GetString("Message:", null, true);
                        if (String.IsNullOrEmpty(msg)) continue;
                        msgBytes = Encoding.UTF8.GetBytes(msg);
                        ms = new MemoryStream();
                        ms.Write(msgBytes, 0, msgBytes.Length);
                        ms.Seek(0, SeekOrigin.Begin);
                        await _Server.SendAsync(_ClientGuid, msgBytes.Length, ms);
                        break;

                    case "client send":
                        msg = Inputty.GetString("Message:", null, true);
                        if (String.IsNullOrEmpty(msg)) continue;
                        msgBytes = Encoding.UTF8.GetBytes(msg);
                        ms = new MemoryStream();
                        ms.Write(msgBytes, 0, msgBytes.Length);
                        ms.Seek(0, SeekOrigin.Begin);
                        await _Client.SendAsync(msgBytes.Length, ms);
                        break;

                    case "server send file":
                        filename = Inputty.GetString("Filename:", null, true);
                        if (String.IsNullOrEmpty(filename)) continue;
                        len = new FileInfo(filename).Length;
                        using (fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                        {
                            md.Add("filename", filename);
                            md.Add("md5", Md5File(filename));
                            await _Server.SendAsync(_ClientGuid, len, fs, md);
                        }
                        break;

                    case "client send file":
                        filename = Inputty.GetString("Filename:", null, true);
                        if (String.IsNullOrEmpty(filename)) continue;
                        len = new FileInfo(filename).Length;
                        using (fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                        {
                            md.Add("filename", filename);
                            md.Add("md5", Md5File(filename));
                            await _Client.SendAsync(len, fs, md);
                        }
                        break;
                }
            }
        }

        static string Md5File(string filename)
        {
            using (MD5 md5 = MD5.Create())
            {
                using (FileStream stream = File.OpenRead(filename))
                {
                    byte[] checksum = md5.ComputeHash(stream);
                    return BitConverter.ToString(checksum).Replace("-", "").ToLower();
                }
            }
        }
         
        static void Menu()
        {
            Console.WriteLine("");
            Console.WriteLine("Available commands");
            Console.WriteLine("------------------");
            Console.WriteLine("q                 Quit");
            Console.WriteLine("cls               Clear the screen");
            Console.WriteLine("?                 Help, this menu");
            Console.WriteLine("server send       Send message from server to client");
            Console.WriteLine("client send       Send message from client to server");
            Console.WriteLine("server send file  Send file from server to client");
            Console.WriteLine("client send file  Send file from client to server");
            Console.WriteLine("");
        }
    }
}
