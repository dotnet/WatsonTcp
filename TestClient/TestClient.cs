using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WatsonTcp;

namespace TestClient
{
    class TestClient
    {
        static string serverIp = "";
        static int serverPort = 0;

        static void Main(string[] args)
        {
            Console.Write("Server IP    : ");
            serverIp = Console.ReadLine();

            Console.Write("Server Port  : ");
            serverPort = Convert.ToInt32(Console.ReadLine());

            WatsonTcpClient client = new WatsonTcpClient(serverIp, serverPort, true, MessageReceived, ServerConnected, ServerDisconnected);

            bool runForever = true;
            while (runForever)
            {
                Console.Write("Command [? for help]: ");
                string userInput = Console.ReadLine();
                if (String.IsNullOrEmpty(userInput)) continue;

                switch (userInput)
                {
                    case "?":
                        Console.WriteLine("Available commands:");
                        Console.WriteLine("  ?      help (this menu)");
                        Console.WriteLine("  q      quit");
                        Console.WriteLine("  cls    clear screen");
                        Console.WriteLine("  send   send message to server");
                        break;
                        
                    case "q":
                        runForever = false;
                        break;

                    case "cls":
                        Console.Clear();
                        break;

                    case "send":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        client.Send(Encoding.UTF8.GetBytes(userInput));
                        break;

                    default:
                        break;
                }
            }
        }

        static bool MessageReceived(byte[] data)
        {
            Console.WriteLine("Message from server: " + Encoding.UTF8.GetString(data));
            return true;
        }

        static bool ServerConnected()
        {
            Console.WriteLine("Server connected");
            return true;
        }

        static bool ServerDisconnected()
        {
            Console.WriteLine("Server disconnected");
            return true;
        }
    }
}
