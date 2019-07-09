namespace WatsonTcp
{
    using System;
    using System.Collections;
    using System.Text;
    using Newtonsoft.Json;

    public static class Common
    {
        /// <summary>
        /// Serialize an object to JSON.
        /// </summary>
        /// <param name="obj">Object to serialize.</param>
        /// <returns>JSON string.</returns>
        public static string SerializeJson(object obj)
        {
            if (obj == null)
            {
                return null;
            }

            string json = JsonConvert.SerializeObject(
                obj,
                Newtonsoft.Json.Formatting.Indented,
                new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                });

            return json;
        }

        /// <summary>
        /// Deserialize JSON string to an object using Newtonsoft JSON.NET.
        /// </summary>
        /// <typeparam name="T">The type of object.</typeparam>
        /// <param name="json">JSON string.</param>
        /// <returns>An object of the specified type.</returns>
        public static T DeserializeJson<T>(string json)
        {
            if (String.IsNullOrEmpty(json))
            {
                throw new ArgumentNullException(nameof(json));
            }

            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception e)
            {
                Console.WriteLine(String.Empty);
                Console.WriteLine("Exception while deserializing:");
                Console.WriteLine(json);
                Console.WriteLine(String.Empty);
                throw;
            }
        }

        /// <summary>
        /// Deserialize JSON string to an object using Newtonsoft JSON.NET.
        /// </summary>
        /// <typeparam name="T">The type of object.</typeparam>
        /// <param name="data">Byte array containing the JSON string.</param>
        /// <returns>An object of the specified type.</returns>
        public static T DeserializeJson<T>(byte[] data)
        {
            if (data == null || data.Length < 1)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return DeserializeJson<T>(Encoding.UTF8.GetString(data));
        }

        public static bool InputBoolean(string question, bool yesDefault)
        {
            Console.Write(question);

            if (yesDefault)
            {
                Console.Write(" [Y/n]? ");
            }
            else
            {
                Console.Write(" [y/N]? ");
            }

            string userInput = Console.ReadLine();

            if (String.IsNullOrEmpty(userInput))
            {
                if (yesDefault)
                {
                    return true;
                }

                return false;
            }

            userInput = userInput.ToLower();

            if (yesDefault)
            {
                if (
                    (String.Compare(userInput, "n") == 0)
                    || (String.Compare(userInput, "no") == 0)
                   )
                {
                    return false;
                }

                return true;
            }
            else
            {
                if (
                    (String.Compare(userInput, "y") == 0)
                    || (String.Compare(userInput, "yes") == 0)
                   )
                {
                    return true;
                }

                return false;
            }
        }

        public static string InputString(string question, string defaultAnswer, bool allowNull)
        {
            while (true)
            {
                Console.Write(question);

                if (!String.IsNullOrEmpty(defaultAnswer))
                {
                    Console.Write(" [" + defaultAnswer + "]");
                }

                Console.Write(" ");

                string userInput = Console.ReadLine();

                if (String.IsNullOrEmpty(userInput))
                {
                    if (!String.IsNullOrEmpty(defaultAnswer))
                    {
                        return defaultAnswer;
                    }

                    if (allowNull)
                    {
                        return null;
                    }
                    else
                    {
                        continue;
                    }
                }

                return userInput;
            }
        }

        public static int InputInteger(string question, int defaultAnswer, bool positiveOnly, bool allowZero)
        {
            while (true)
            {
                Console.Write(question);
                Console.Write(" [" + defaultAnswer + "] ");

                string userInput = Console.ReadLine();

                if (String.IsNullOrEmpty(userInput))
                {
                    return defaultAnswer;
                }

                if (!Int32.TryParse(userInput, out int ret))
                {
                    Console.WriteLine("Please enter a valid integer.");
                    continue;
                }

                if (ret == 0)
                {
                    if (allowZero)
                    {
                        return 0;
                    }
                }

                if (ret < 0)
                {
                    if (positiveOnly)
                    {
                        Console.WriteLine("Please enter a value greater than zero.");
                        continue;
                    }
                }

                return ret;
            }
        }

        public static void LogException(String method, Exception e)
        {
            Console.WriteLine("================================================================================");
            Console.WriteLine(" = Method: " + method);
            Console.WriteLine(" = Exception Type: " + e.GetType().ToString());
            Console.WriteLine(" = Exception Data: " + e.Data);
            Console.WriteLine(" = Inner Exception: " + e.InnerException);
            Console.WriteLine(" = Exception Message: " + e.Message);
            Console.WriteLine(" = Exception Source: " + e.Source);
            Console.WriteLine(" = Exception StackTrace: " + e.StackTrace);
            Console.WriteLine("================================================================================");
        }
    }
}
