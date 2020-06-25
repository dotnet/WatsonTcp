using System;
using System.Threading;
using System.Threading.Tasks;
using WatsonTcp;

namespace Test.NonExistentServer
{
    class Program
    {
        static void Main(string[] args)
        { 
            Task.Run(() =>
            {
                WatsonTcpClient client = new WatsonTcpClient("10.1.2.3", 1234); // NonExistant Server

                client.ServerConnected += HandleServerConnected;
                client.ServerDisconnected += HandleServerDisconnected;
                client.MessageReceived += HandleMessageReceived;

                try
                {
                    Console.WriteLine("Starting Client");
                    client.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception: {0}", ex.Message);
                    client.Dispose();
                }
            });

            Console.WriteLine("Waiting on NullReferenceException");
            Thread.Sleep(10000); 
        }

        static void HandleServerConnected(object sender, EventArgs e)
        {
            Console.WriteLine("Server Connected");
        }
        static void HandleServerDisconnected(object sender, EventArgs e)
        {
            Console.WriteLine("Server Disconnected");
        }
        static void HandleMessageReceived(object sender, MessageReceivedFromServerEventArgs e)
        {
            Console.WriteLine("Message Recieved");
        }
    } 
}
