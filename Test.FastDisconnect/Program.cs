using System;
using WatsonTcp;

namespace Test.FastDisconnect
{
    class Program
    {
        static WatsonTcpClient _Client = null;

        static void Main(string[] args)
        {
            try
            {
                _Client = new WatsonTcpClient("127.0.0.1", 9000);
                _Client.MessageReceived += MessageReceived;
                _Client.Start();
                _Client.Send("Hello!");
                _Client.Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        static void MessageReceived(object sender, MessageReceivedFromServerEventArgs args)
        {

        }
    }
}
