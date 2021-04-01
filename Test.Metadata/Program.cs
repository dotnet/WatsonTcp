using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WatsonTcp;

namespace Test.Metadata
{
    class Program
    {
        static void Main(string[] args)
        {
            using (WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", 8000))
            {
                server.Events.MessageReceived += ServerMessageReceived;
                server.Start();
                Task.Delay(1000).Wait();
                using (WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 8000))
                {
                    client.Events.MessageReceived += ClientMessageReceived;
                    client.Connect();

                    for (int i = 0; i < 10; i++)
                    {
                        Person p = new Person("hello", "world", i);
                        Dictionary<object, object> md = new Dictionary<object, object>();
                        md.Add("person", p);
                        client.Send(("Message " + i), md);
                        Task.Delay(1000).Wait();
                    }
                }
            }
        }

        #region Events

        private static void ServerMessageReceived(object sender, MessageReceivedEventArgs args)
        { 
            try
            {
                object o = args.Metadata["person"];
                Console.WriteLine(SerializeJson(o, true));
                Person p = (Person)o; 
                Console.WriteLine(p.FirstName + " " + p.LastName + ": " + p.Number);
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
        public int Number { get; set; }

        public Person()
        {

        }

        public Person(string first, string last, int num)
        {
            FirstName = first;
            LastName = last;
            Number = num;
        }
    }
}
