namespace Test.SyncMessages
{
    using System;
    using System.ComponentModel.Design;
    using System.Text;
    using System.Threading.Tasks;
    using GetSomeInput;
    using WatsonTcp;

    internal class Program
    {
        static WatsonTcpServer _Server;
        static WatsonTcpClient _Client;
        static Guid _ClientGuid;

        static async Task Main(string[] args)
        {
            using (_Server = new WatsonTcpServer("127.0.0.1", 8000))
            {
                using (_Client = new WatsonTcpClient("127.0.0.1", 8000))
                {
                    _Server.Callbacks.SyncRequestReceivedAsync += ServerSyncRequestReceived;
                    _Server.Events.ClientConnected += ServerClientConnected;
                    _Server.Events.ClientDisconnected += ServerClientDisconnected;
                    _Server.Events.MessageReceived += ServerMessageReceived;

                    _Client.Callbacks.SyncRequestReceivedAsync += ClientSyncRequestReceived;
                    _Client.Events.ServerConnected += ClientServerConnected;
                    _Client.Events.ServerDisconnected += ClientServerDisconnected;
                    _Client.Events.MessageReceived += ClientMessageReceived;

                    _Server.Start();
                    _Client.Connect();

                    await Menu();
                }
            }
        }

        private static void ClientServerDisconnected(object sender, DisconnectionEventArgs e)
        {
            Console.WriteLine("Client: disconnected from server");
        }

        private static void ClientServerConnected(object sender, ConnectionEventArgs e)
        {
            Console.WriteLine("Client: connected to server");
        }

        private static void ServerClientDisconnected(object sender, DisconnectionEventArgs e)
        {
            Console.WriteLine("Server: client disconnected");
            _ClientGuid = Guid.Empty;
        }

        private static void ServerClientConnected(object sender, ConnectionEventArgs e)
        {
            Console.WriteLine("Server: client connected");
            _ClientGuid = e.Client.Guid;
        }

        static async Task Menu()
        {
            while (true)
            {
                string userInput = Inputty.GetString("[Command ?/help]:", null, false);

                string val = null;
                Command cmd = null;
                SyncResponse resp = null;

                switch (userInput)
                {
                    case "?":
                        Console.WriteLine("");
                        Console.WriteLine("Menu");
                        Console.WriteLine("----");
                        Console.WriteLine("server send        Send a message from the server");
                        Console.WriteLine("client send        Send a message from the client");
                        Console.WriteLine("server echo        Send an echo request from the server");
                        Console.WriteLine("client echo        Send an echo request from the client");
                        Console.WriteLine("server inc         Send an increment request from the server");
                        Console.WriteLine("client inc         Send an increment request from the client");
                        Console.WriteLine("server dec         Send an decrement request from the server");
                        Console.WriteLine("client dec         Send an decrement request from the client");
                        Console.WriteLine("");
                        break;

                    case "q":
                        return;

                    case "c":
                    case "cls":
                        Console.Clear();
                        break;

                    case "server send":
                        val = Inputty.GetString("Data:", null, true);
                        if (!String.IsNullOrEmpty(val) && _ClientGuid != Guid.Empty)
                            await _Server.SendAsync(_ClientGuid, val);
                        break;

                    case "client send":
                        val = Inputty.GetString("Data:", null, true);
                        if (!String.IsNullOrEmpty(val))
                            await _Client.SendAsync(val);
                        break;

                    case "server echo":
                        val = Inputty.GetString("Data:", null, true);
                        if (!String.IsNullOrEmpty(val) && _ClientGuid != Guid.Empty)
                        {
                            cmd = new Command();
                            cmd.CommandType = CommandTypeEnum.Echo;
                            cmd.Data = val;
                            resp = await _Server.SendAndWaitAsync(5000, _ClientGuid, _Server.SerializationHelper.SerializeJson(cmd, false));
                            if (resp != null)
                                Console.WriteLine("Response: " + Encoding.UTF8.GetString(resp.Data));
                        }
                        break;

                    case "client echo":
                        val = Inputty.GetString("Data:", null, true);
                        if (!String.IsNullOrEmpty(val))
                        {
                            cmd = new Command();
                            cmd.CommandType = CommandTypeEnum.Echo;
                            cmd.Data = val;
                            resp = await _Client.SendAndWaitAsync(5000, _Client.SerializationHelper.SerializeJson(cmd, false));
                            if (resp != null)
                                Console.WriteLine("Response: " + Encoding.UTF8.GetString(resp.Data));
                        }
                        break;

                    case "server inc":
                        val = Inputty.GetString("Data:", null, true);
                        if (!String.IsNullOrEmpty(val) && _ClientGuid != Guid.Empty)
                        {
                            cmd = new Command();
                            cmd.CommandType = CommandTypeEnum.Increment;
                            cmd.Int = Convert.ToInt32(val);
                            resp = await _Server.SendAndWaitAsync(5000, _ClientGuid, _Server.SerializationHelper.SerializeJson(cmd, false));
                            if (resp != null)
                                Console.WriteLine("Response: " + Encoding.UTF8.GetString(resp.Data));
                        }
                        break;

                    case "client inc":
                        val = Inputty.GetString("Data:", null, true);
                        if (!String.IsNullOrEmpty(val))
                        {
                            cmd = new Command();
                            cmd.CommandType = CommandTypeEnum.Increment;
                            cmd.Int = Convert.ToInt32(val);
                            resp = await _Client.SendAndWaitAsync(5000, _Client.SerializationHelper.SerializeJson(cmd, false));
                            if (resp != null)
                                Console.WriteLine("Response: " + Encoding.UTF8.GetString(resp.Data));
                        }
                        break;

                    case "server dec":
                        val = Inputty.GetString("Data:", null, true);
                        if (!String.IsNullOrEmpty(val) && _ClientGuid != Guid.Empty)
                        {
                            cmd = new Command();
                            cmd.CommandType = CommandTypeEnum.Decrement;
                            cmd.Int = Convert.ToInt32(val);
                            resp = await _Server.SendAndWaitAsync(5000, _ClientGuid, _Server.SerializationHelper.SerializeJson(cmd, false));
                            if (resp != null)
                                Console.WriteLine("Response: " + Encoding.UTF8.GetString(resp.Data));
                        }
                        break;

                    case "client dec":
                        val = Inputty.GetString("Data:", null, true);
                        if (!String.IsNullOrEmpty(val))
                        {
                            cmd = new Command();
                            cmd.CommandType = CommandTypeEnum.Decrement;
                            cmd.Int = Convert.ToInt32(val);
                            resp = await _Client.SendAndWaitAsync(5000, _Client.SerializationHelper.SerializeJson(cmd, false));
                            if (resp != null)
                                Console.WriteLine("Response: " + Encoding.UTF8.GetString(resp.Data));
                        }
                        break;
                }
            }
        }

        private static void ClientMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            Console.WriteLine("Client received message: " + Encoding.UTF8.GetString(e.Data));
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private static async Task<SyncResponse> ClientSyncRequestReceived(SyncRequest arg)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            Console.WriteLine("Client received sync request: " + Encoding.UTF8.GetString(arg.Data));

            Command cmd = _Client.SerializationHelper.DeserializeJson<Command>(Encoding.UTF8.GetString(arg.Data));

            switch (cmd.CommandType)
            {
                case CommandTypeEnum.Increment:
                    return new SyncResponse(arg, Encoding.UTF8.GetBytes((cmd.Int + 1).ToString()));
                case CommandTypeEnum.Decrement:
                    return new SyncResponse(arg, Encoding.UTF8.GetBytes((cmd.Int - 1).ToString()));
                case CommandTypeEnum.Echo:
                    return new SyncResponse(arg, cmd.Data);
            }

            throw new ArgumentException("Unknown command type.");
        }

        private static void ServerMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            Console.WriteLine("Server received message: " + Encoding.UTF8.GetString(e.Data));
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private static async Task<SyncResponse> ServerSyncRequestReceived(SyncRequest arg)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            Console.WriteLine("Server received sync request: " + Encoding.UTF8.GetString(arg.Data));

            Command cmd = _Server.SerializationHelper.DeserializeJson<Command>(Encoding.UTF8.GetString(arg.Data));

            switch (cmd.CommandType)
            {
                case CommandTypeEnum.Increment:
                    return new SyncResponse(arg, Encoding.UTF8.GetBytes((cmd.Int + 1).ToString()));
                case CommandTypeEnum.Decrement:
                    return new SyncResponse(arg, Encoding.UTF8.GetBytes((cmd.Int - 1).ToString()));
                case CommandTypeEnum.Echo:
                    return new SyncResponse(arg, cmd.Data);
            }

            throw new ArgumentException("Unknown command type.");
        }
    }
}
