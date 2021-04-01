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
                _Client.Events.MessageReceived += MessageReceived;
                _Client.Connect();
                _Client.Send("Hello!");
                _Client.Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        static void MessageReceived(object sender, MessageReceivedEventArgs args)
        {

        }
    }
}
