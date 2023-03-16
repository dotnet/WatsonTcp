using System;
using WatsonTcp;

namespace Test.FastDisconnect
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                using (WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 9000))
                {
                    client.Events.MessageReceived += MessageReceived;
                    client.Connect();
                    client.Send("Hello!");
                }
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
