namespace TestPartialStream
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using WatsonTcp;

    internal class TestPartialStream
    {

        public static void Main(string[] args)
        {
            if (args != null && args[0].ToLower().Equals("server"))
                StartServer();
            else
                StartClient();

            Console.ReadLine();
        }

        static void StartServer()
        {
            WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", 9001);

            server.Events.ClientConnected += (sender, args) =>
                Console.WriteLine("Server: a client has connected.");

            server.Events.ClientDisconnected += (sender, args) =>
                Console.WriteLine("Server: a client has disconnected: " + args.Reason);

            server.Events.MessageReceived += (sender, args) =>
                Console.WriteLine($"Server: received {args.Data.Length} bytes from client");

            server.Start();

            Console.WriteLine("Server has started.");
        }

        static void StartClient()
        {
            WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 9001);

            client.Events.ServerConnected += async (sender, args) => 
            {
                Console.WriteLine("Client: connected to server. Will send a message...");

                //                       VVVVVVVVV Only want to send the first 5 bytes
                var buffer = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

                using (var stream = new MemoryStream(buffer))
                {
                    await client.SendAsync(5, stream); //    <-- will disconnect unless you use 10
                }
            };

            client.Events.ServerDisconnected += (sender, args) =>
                Console.WriteLine("Client: disconnected from server.");

            client.Events.MessageReceived += (sender, args) =>
                Console.WriteLine($"Client: received {args.Data.Length} bytes from server.");

            Console.WriteLine("Client attempting connection...");
            client.Connect();
        }
    }
}