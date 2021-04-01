using System;
using System.Text;
using System.Threading.Tasks;
using WatsonTcp;

namespace Test.Deadlock
{
    class Program
    {
        static string serverHostname = "127.0.0.1";
        static int serverPort = 8000;
        static WatsonTcpServer server = null;
        static WatsonTcpClient client = null;
        static string clientIpPort = null;

        static void Main(string[] args)
        {
            using (server = new WatsonTcpServer(serverHostname, serverPort))
            {
                server.Events.ClientConnected += (s, e) =>
                {
                    Console.WriteLine("Client connected to server: " + e.IpPort);
                    clientIpPort = e.IpPort;
                };

                server.Events.ClientDisconnected += (s, e) =>
                {
                    Console.WriteLine("Client disconnected from server: " + e.IpPort);
                    clientIpPort = null;
                };

                server.Events.MessageReceived += (s, e) =>
                {
                    Console.WriteLine("Server received message from client " + e.IpPort + ": " + Encoding.UTF8.GetString(e.Data));
                };

                server.Callbacks.SyncRequestReceived = delegate (SyncRequest req)
                {
                    Console.WriteLine("Server received sync message from client " + req.IpPort + ": " + Encoding.UTF8.GetString(req.Data));
                    return new SyncResponse(req, "Here's your response from the server!");
                };

                server.Settings.Logger = ServerLogger;
                server.Start();

                using (client = new WatsonTcpClient(serverHostname, serverPort))
                {
                    client.Events.ServerConnected += (s, e) =>
                    {
                        Console.WriteLine("Client connected to server");
                    };

                    client.Events.ServerDisconnected += (s, e) =>
                    {
                        Console.WriteLine("Client disconnected from server");
                    };

                    client.Events.MessageReceived += (s, e) =>
                    {
                        Console.WriteLine("Client received message from server: " + Encoding.UTF8.GetString(e.Data));
                    };

                    client.Callbacks.SyncRequestReceived = delegate (SyncRequest req)
                    {
                        Console.WriteLine("Client received sync message from server: " + Encoding.UTF8.GetString(req.Data));
                        return new SyncResponse(req, "Here's your response from the client!");
                    };

                    client.Settings.Logger = ClientLogger;
                    client.Connect();

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
                SyncResponse resp = server.SendAndWait(5000, clientIpPort, "Here's your request from the server!");
                if (resp == null)
                {
                    Console.WriteLine("Server did not receive response from client");
                }
                else
                {
                    Console.WriteLine("Server received response from " + clientIpPort + ": " + Encoding.UTF8.GetString(resp.Data));
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
                SyncResponse resp = client.SendAndWait(5000, "Here's your request from the client!");
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
