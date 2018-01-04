using System;
using System.Collections.Generic;
using System.Text;
using WatsonTcp;

namespace TestServer
{
    class TestServer
    {
        static string serverIp = "";
        static int serverPort = 0;

        static void Main(string[] args)
        {
            Console.Write("Server IP    : ");
            serverIp = Console.ReadLine();

            Console.Write("Server Port  : ");
            serverPort = Convert.ToInt32(Console.ReadLine());

            using (WatsonTcpServer server = new WatsonTcpServer(serverIp, serverPort, ClientConnected, ClientDisconnected, MessageReceived, true))
            {
                bool runForever = true;
                while (runForever)
                {
                    Console.Write("Command [? for help]: ");
                    string userInput = Console.ReadLine();

                    List<string> clients;
                    string ipPort;

                    if (String.IsNullOrEmpty(userInput)) continue;

                    switch (userInput)
                    {
                        case "?":
                            Console.WriteLine("Available commands:");
                            Console.WriteLine("  ?        help (this menu)");
                            Console.WriteLine("  q        quit");
                            Console.WriteLine("  cls      clear screen");
                            Console.WriteLine("  list     list clients");
                            Console.WriteLine("  send     send message to client");
                            Console.WriteLine("  remove   disconnect client");
                            break;

                        case "q":
                            runForever = false;
                            break;

                        case "cls":
                            Console.Clear();
                            break;

                        case "list":
                            clients = server.ListClients();
                            if (clients != null && clients.Count > 0)
                            {
                                Console.WriteLine("Clients");
                                foreach (string curr in clients)
                                {
                                    Console.WriteLine("  " + curr);
                                }
                            }
                            else
                            {
                                Console.WriteLine("None");
                            }
                            break;

                        case "send":
                            Console.Write("IP:Port: ");
                            ipPort = Console.ReadLine();
                            Console.Write("Data: ");
                            userInput = Console.ReadLine();
                            if (String.IsNullOrEmpty(userInput)) break;
                            server.Send(ipPort, Encoding.UTF8.GetBytes(userInput));
                            break;

                        case "remove":
                            Console.Write("IP:Port: ");
                            ipPort = Console.ReadLine();
                            server.DisconnectClient(ipPort);
                            break;

                        default:
                            break;
                    }
                }
            }
        }

        static bool ClientConnected(string ipPort)
        {
            Console.WriteLine("Client connected: " + ipPort);
            return true;
        }

        static bool ClientDisconnected(string ipPort)
        {
            Console.WriteLine("Client disconnected: " + ipPort);
            return true;
        }

        static bool MessageReceived(string ipPort, byte[] data)
        {
            string msg = "";
            if (data != null && data.Length > 0) msg = Encoding.UTF8.GetString(data);
            Console.WriteLine("Message received from " + ipPort + ": " + msg);
            return true;
        }
    }
}
