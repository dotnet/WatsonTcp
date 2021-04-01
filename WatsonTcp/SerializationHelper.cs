using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WatsonTcp
{
    internal static class SerializationHelper
    { 
        private static readonly JsonSerializerSettings HardenedSerializerSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None // Prevents CS2328 style attacks if a project is allowing automatic type resolution elsewhere.
        };

        internal static T DeserializeJson<T>(string json)
        {
            if (String.IsNullOrEmpty(json)) throw new ArgumentNullException(nameof(json));
            return JsonConvert.DeserializeObject<T>(json, HardenedSerializerSettings);
        }

        private static readonly JsonSerializerSettings SerializerDefaults = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.Local,
        };

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
    }
}