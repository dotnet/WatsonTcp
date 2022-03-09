using System;
using System.Text;
using WatsonTcp;

namespace Test.Reconnect
{
    class Program
    {
        static WatsonTcpClient _Client = null;
        static readonly Random _Random = new Random();

        static void Main(string[] args)
        {
            _Client = new WatsonTcpClient("127.0.0.1", 9000);
            _Client.Events.MessageReceived += (s, e) =>
            {
                Console.WriteLine(e.IpPort + ": " + Encoding.UTF8.GetString(e.Data));
            };

            while (true)
            {
                try
                {
                    _Client.Connect();
                    Console.WriteLine(DateTime.UtcNow.ToString() + " Connected");
                    Task.Delay(_Random.Next(1000, 3000)).Wait();
                    _Client.Disconnect();
                }
                catch (Exception e)
                {
                    Console.WriteLine(DateTime.UtcNow.ToString() + " Failed: " + e.ToString());
                }
            }
        }
    }
}