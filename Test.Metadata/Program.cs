using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WatsonTcp;

namespace Test.Metadata
{
    class Program
    {
        static int _ServerPort = 8000;
        static int _MessageCount = 1;
        static int _EntryCount = 500;
        static Stopwatch[] _Stopwatches = null;

        static void Main(string[] args)
        {
            if (args != null && args.Length == 2)
            {
                _MessageCount = Convert.ToInt32(args[0]);
                _EntryCount = Convert.ToInt32(args[1]);
            }

            _Stopwatches = new Stopwatch[_MessageCount];

            using (WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", _ServerPort))
            {
                // server.Settings.DebugMessages = true;
                // server.Settings.Logger = ServerLogger;
                server.Events.MessageReceived += ServerMessageReceived;
                server.Start();
                Console.WriteLine("Server started");
                Task.Delay(1000).Wait();

                using (WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", _ServerPort))
                {
                    client.Events.MessageReceived += ClientMessageReceived;
                    client.Connect();
                    Console.WriteLine("Client connected to server");

                    for (int i = 0; i < _MessageCount; i++)
                    {
                        Dictionary<object, object> md = new Dictionary<object, object>();

                        for (int j = 0; j < _EntryCount; j++)
                        {
                            Person p = new Person("hello", "world", i.ToString() + "." + j.ToString());
                            md.Add("person." + i.ToString() + "." + j.ToString(), p);
                        }

                        client.Send(i.ToString(), md);

                        _Stopwatches[i] = new Stopwatch();
                        _Stopwatches[i].Start();

                        Console.WriteLine("Client sent message " + i);
                    }
                }

                Console.WriteLine("Press ENTER to exit");
                Console.ReadLine();
            }
        }

        private static void ServerLogger(Severity arg1, string arg2)
        {
            Console.WriteLine(arg1.ToString() + ": " + arg2);
        }

        #region Events

        private static void ServerMessageReceived(object sender, MessageReceivedEventArgs args)
        { 
            try
            {
                int msgNum = Convert.ToInt32(Encoding.UTF8.GetString(args.Data));
                Dictionary<object, object> md = args.Metadata;
                _Stopwatches[msgNum].Stop();
                Console.WriteLine("Server received message " + msgNum.ToString() + " with " + md.Count + " metadata entries: " + _Stopwatches[msgNum].ElapsedMilliseconds + "ms");

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private static void ClientMessageReceived(object sender, MessageReceivedEventArgs args)
        {

        }

        #endregion

        #region Serialization

        private static readonly JsonSerializerSettings HardenedSerializerSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None // Prevents CS2328 style attacks if a project is allowing automatic type resolution elsewhere.
        };

        private static readonly JsonSerializerSettings SerializerDefaults = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.Local,
        };

        internal static T DeserializeJson<T>(string json)
        {
            if (String.IsNullOrEmpty(json)) throw new ArgumentNullException(nameof(json));
            return JsonConvert.DeserializeObject<T>(json, HardenedSerializerSettings);
        }

        internal static string SerializeJson(object obj, bool pretty)
        {
            if (obj == null) return null;
            string json;

            if (pretty)
            {
                json = JsonConvert.SerializeObject(
                    obj,
                    Newtonsoft.Json.Formatting.Indented,
                    SerializerDefaults
                );
            }
            else
            {
                json = JsonConvert.SerializeObject(
                    obj,
                    SerializerDefaults
                );
            }

            return json;
        }

        #endregion
    }

    public class Person
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Identifier { get; set; }

        public Person()
        {

        }

        public Person(string first, string last, string id)
        {
            FirstName = first;
            LastName = last;
            Identifier = id;
        }
    }
}
