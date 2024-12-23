namespace TestThroughput
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonTcp;

    class Program
    {
        static bool _RunForever = true;
        static Random _Random = new Random();

        static async Task Main(string[] args)
        {
            Console.WriteLine("");

            while (_RunForever)
            {
                Console.Clear();
                Console.WriteLine(); 
                Console.WriteLine("Available tests");
                Console.WriteLine("  0) Quit");
                Console.WriteLine("  1) Single client, using async APIs");
                Console.WriteLine("  2) Multiple clients, using async APIs");
                Console.WriteLine("");
                Console.Write("Test: ");
                int testNumber = Convert.ToInt32(Console.ReadLine());

                if (testNumber == 0) return;
                else if (testNumber == 1)
                {
                    Console.Write("Message size: ");
                    int messageSize = Convert.ToInt32(Console.ReadLine());
                    Console.Write("Number of messages: ");
                    int numMessages = Convert.ToInt32(Console.ReadLine());

                    Test1 test1 = new Test1(messageSize, numMessages);
                    await test1.RunTest();
                }
                else if (testNumber == 2)
                {
                    Console.Write("Message size: ");
                    int messageSize = Convert.ToInt32(Console.ReadLine());
                    Console.Write("Number of messages: ");
                    int numMessages = Convert.ToInt32(Console.ReadLine());
                    Console.Write("Number of clients: ");
                    int numClients = Convert.ToInt32(Console.ReadLine());

                    Test2 test2 = new Test2(messageSize, numMessages, numClients);
                    await test2.RunTest();
                }

                Console.WriteLine("");
                Console.Write("Press ENTER to continue");
                Console.ReadLine();
            }
        }
    }
}
