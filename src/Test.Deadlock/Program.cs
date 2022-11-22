using System;
using System.Text;
using System.Threading.Tasks;
using WatsonTcp;

namespace Test.Deadlock
{
    class Program
    {
        static string _ServerHostname = "127.0.0.1";
        static int _ServerPort = 8000;
        static WatsonTcpServer _Server = null;
        static WatsonTcpClient _Client = null;
        static Guid _ClientGuid = Guid.Empty;

        static void Main(string[] args)
        {
            using (_Server = new WatsonTcpServer(_ServerHostname, _ServerPort))
            {
                _Server.Events.ClientConnected += (s, e) =>
                {
                    Console.WriteLine("Client connected to server: " + e.Client.ToString());
                    _ClientGuid = e.Client.Guid;
                };

                _Server.Events.ClientDisconnected += (s, e) =>
                {
                    Console.WriteLine("Client disconnected from server: " + e.Client.ToString());
                    _ClientGuid = Guid.Empty;
                };

                _Server.Events.MessageReceived += (s, e) =>
                {
                    Console.WriteLine("Server received message from client " + e.Client.ToString() + ": " + Encoding.UTF8.GetString(e.Data));
                };

                _Server.Callbacks.SyncRequestReceived = delegate (SyncRequest req)
                {
                    Console.WriteLine("Server received sync message from client " + req.Client.ToString() + ": " + Encoding.UTF8.GetString(req.Data));
                    return new SyncResponse(req, "Here's your response from the server!");
                };

                _Server.Settings.Logger = ServerLogger;
                _Server.Start();

                using (_Client = new WatsonTcpClient(_ServerHostname, _ServerPort))
                {
                    _Client.Events.ServerConnected += (s, e) =>
                    {
                        Console.WriteLine("Client connected to server");
                    };

                    _Client.Events.ServerDisconnected += (s, e) =>
                    {
                        Console.WriteLine("Client disconnected from server");
                    };

                    _Client.Events.MessageReceived += (s, e) =>
                    {
                        Console.WriteLine("Client received message from server: " + Encoding.UTF8.GetString(e.Data));
                    };

                    _Client.Callbacks.SyncRequestReceived = delegate (SyncRequest req)
                    {
                        Console.WriteLine("Client received sync message from server: " + Encoding.UTF8.GetString(req.Data));
                        return new SyncResponse(req, "Here's your response from the client!");
                    };

                    _Client.Settings.Logger = ClientLogger;
                    _Client.Connect();

                    while (true)
                    {
                        Task.Delay(5000).Wait();
                        Task.Run(() => ServerTask());
                        Task.Run(() => ClientTask());
                    }
                }
            }
        }

        static void ServerLogger(Severity sev, string msg)
        {
            Console.WriteLine("[Server] [" + sev.ToString().PadRight(9) + "] " + msg);
        }

        static void ClientLogger(Severity sev, string msg)
        {
            Console.WriteLine("[Client] [" + sev.ToString().PadRight(9) + "] " + msg);
        }

        static void ServerTask()
        {
            try
            {
                SyncResponse resp = _Server.SendAndWait(5000, _ClientGuid, "Here's your request from the server!");
                if (resp == null)
                {
                    Console.WriteLine("Server did not receive response from client");
                }
                else
                {
                    Console.WriteLine("Server received response from " + _ClientGuid.ToString() + ": " + Encoding.UTF8.GetString(resp.Data));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Server exception: " + e.Message);
            }
        }

        static void ClientTask()
        {
            try
            {
                SyncResponse resp = _Client.SendAndWait(5000, "Here's your request from the client!");
                if (resp == null)
                {
                    Console.WriteLine("Client did not receive response from server");
                }
                else
                {
                    Console.WriteLine("Client received response from server: " + Encoding.UTF8.GetString(resp.Data));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Client exception: " + e.Message);
            }
        }
    }
}
