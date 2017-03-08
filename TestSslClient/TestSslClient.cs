using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WatsonTcp;

namespace TestSslClient
{
    class TestSslClient
    {
        static string serverIp = "";
        static int serverPort = 0;
        static string certFile = "";
        static string certPass = "";

        static void Main(string[] args)
        {
            Console.Write("Server IP        : ");
            serverIp = Console.ReadLine();

            Console.Write("Server Port      : ");
            serverPort = Convert.ToInt32(Console.ReadLine());

            Console.Write("Certificate File : ");
            certFile = Console.ReadLine();

            Console.Write("Certificate Pass : ");
            certPass = Console.ReadLine();

            WatsonTcpSslClient client = new WatsonTcpSslClient(serverIp, serverPort, certFile, certPass, true, ServerConnected, ServerDisconnected, MessageReceived, true);

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
                        Console.WriteLine("  ?          help (this menu)");
                        Console.WriteLine("  q          quit");
                        Console.WriteLine("  cls        clear screen");
                        Console.WriteLine("  send       send message to server");
                        Console.WriteLine("  sendasync  send message to server asynchronously");
                        Console.WriteLine("  status     show if client connected");
                        Console.WriteLine("  dispose    dispose of the connection");
                        Console.WriteLine("  connect    connect to the server if not connected");
                        Console.WriteLine("  reconnect  disconnect if connected, then reconnect");
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

                    case "sendasync":
                        Console.Write("Data: ");
                        userInput = Console.ReadLine();
                        if (String.IsNullOrEmpty(userInput)) break;
                        client.SendAsync(Encoding.UTF8.GetBytes(userInput));
                        break;

                    case "status":
                        if (client == null) Console.WriteLine("Connected: False (null)");
                        else Console.WriteLine("Connected: " + client.IsConnected());
                        break;

                    case "dispose":
                        client.Dispose();
                        break;

                    case "connect":
                        if (client != null && client.IsConnected())
                        {
                            Console.WriteLine("Already connected");
                        }
                        else
                        {
                            client = new WatsonTcpSslClient(serverIp, serverPort, certFile, certPass, true, ServerConnected, ServerDisconnected, MessageReceived, true);
                        }
                        break;

                    case "reconnect":
                        if (client != null) client.Dispose();
                        client = new WatsonTcpSslClient(serverIp, serverPort, certFile, certPass, true, ServerConnected, ServerDisconnected, MessageReceived, true);
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
