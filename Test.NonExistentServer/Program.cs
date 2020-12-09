using System;
using System.Text;
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
            Thread.Sleep(10000); 
        }

        static void ServerConnected(object sender, ConnectionEventArgs args)
        {
            Console.WriteLine(args.IpPort + " connected");
        }

        static void ServerDisconnected(object sender, DisconnectionEventArgs args)
        {
            Console.WriteLine(args.IpPort + " disconnected: " + args.Reason.ToString());
        }

        static void MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            Console.WriteLine("Message received from " + e.IpPort + ": " + Encoding.UTF8.GetString(e.Data));
        }
    } 
}
