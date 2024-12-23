namespace Test.FastDisconnect
{
    using System;
    using System.Threading.Tasks;
    using WatsonTcp;

    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                using (WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 9000))
                {
                    client.Events.MessageReceived += MessageReceived;
                    client.Connect();
                    await client.SendAsync("Hello!");
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
