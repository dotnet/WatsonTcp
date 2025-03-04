namespace Test.Deadlock
{
    using System;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using WatsonTcp;

    static class Program
    {
        static string _ServerHostname = "127.0.0.1";
        static int _ServerPort = 8000;
        static WatsonTcpServer _Server = null;
        static WatsonTcpClient _Client = null;

        private static async Task Main(string[] args)
        {
            // Start the server
            _Server = new WatsonTcpServer(_ServerHostname, _ServerPort);
            _Server.Events.MessageReceived += Events_MessageReceived;
            _Server.Events.ClientConnected += Events_ClientConnected;
            _Server.Events.ClientDisconnected += Events_ClientDisconnected;

            _Server.Start();

            // Connect the client
            _Client = new WatsonTcpClient(_ServerHostname, _ServerPort, 3_000);
            _Client.Events.MessageReceived += Events_MessageReceived1;
            _Client.Events.ServerConnected += Events_ServerConnected;
            _Client.Events.ServerDisconnected += Events_ServerDisconnected;

            _Client.Connect();

            await Task.Delay(-1);
        }

        private static void Events_MessageReceived1(object sender, MessageReceivedEventArgs e)
        {
        }

        private static void Events_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
        }

        private static void Events_ServerDisconnected(object sender, DisconnectionEventArgs e)
        {
            Console.WriteLine("[Client] disconnected, automatic reconnect should fire");
        }

        private static void Events_ServerConnected(object sender, ConnectionEventArgs e)
        {
            Console.WriteLine("[Client] connected");
            Console.WriteLine("[Client] press any key to disconnect and test automatic reconnection");
            Console.ReadKey();

            _Client.Disconnect(disableAutoReconnect: false);
        }

        private static void Events_ClientDisconnected(object sender, DisconnectionEventArgs e)
        {
            Console.WriteLine("[Server] client disconnected");
        }

        private static void Events_ClientConnected(object sender, ConnectionEventArgs e)
        {
            WatsonTcpServer server = (WatsonTcpServer)sender;
            Console.WriteLine("[Server] client connected, " + server.ListClients().Count() + " clients connected");
        }
    }
}