namespace Test.NonExistentServer
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonTcp;

    class Program
    {
        static async Task Main(string[] args)
        { 
            await Task.Run(() =>
            {
                WatsonTcpClient client = new WatsonTcpClient("10.1.2.3", 1234); // NonExistant Server

                client.Events.ServerConnected += ServerConnected;
                client.Events.ServerDisconnected += ServerDisconnected;
                client.Events.MessageReceived += MessageReceived;

                try
                {
                    Console.WriteLine("Starting client");
                    client.Connect();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception: {0}", ex.Message);
                    client.Dispose();
                }
            });

            Console.WriteLine("Waiting on NullReferenceException");
            await Task.Delay(10000); 
        }

        static void ServerConnected(object sender, ConnectionEventArgs args)
        {
            Console.WriteLine("Server connected");
        }

        static void ServerDisconnected(object sender, DisconnectionEventArgs args)
        {
            Console.WriteLine("Server disconnected: " + args.Reason.ToString());
        }

        static void MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            Console.WriteLine("Message received from server: " + Encoding.UTF8.GetString(e.Data));
        }
    } 
}
