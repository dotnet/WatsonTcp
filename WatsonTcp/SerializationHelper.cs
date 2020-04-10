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
        internal static T DeserializeXml<T>(string xml)
        {
            if (String.IsNullOrEmpty(xml)) throw new ArgumentNullException(nameof(xml));

            // remove preamble if exists
            string byteOrderMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
            while (xml.StartsWith(byteOrderMarkUtf8, StringComparison.Ordinal))
            {
                xml = xml.Remove(0, byteOrderMarkUtf8.Length);
            }

            XmlSerializer xmls = new XmlSerializer(typeof(T));
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            {
                return (T)xmls.Deserialize(ms);
            }
        }

        internal static string SerializeXml<T>(object obj, bool pretty)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            /*
            XmlSerializer xml = new XmlSerializer(obj.GetType());
            using (MemoryStream stream = new MemoryStream())
            {
                using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    xml.Serialize(writer, obj);
                    byte[] bytes = stream.ToArray(); 
                    string ret = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                    // remove preamble if exists
                    string byteOrderMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
                    while (ret.StartsWith(byteOrderMarkUtf8, StringComparison.Ordinal))
                    {
                        ret = ret.Remove(0, byteOrderMarkUtf8.Length);
                    } 
                    return ret;
                }
            } 
            */

            XmlSerializer xmls = new XmlSerializer(typeof(T));
            using (MemoryStream ms = new MemoryStream())
            {
                XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
                XmlWriterSettings settings = new XmlWriterSettings();

                if (pretty)
                {
                    settings.Encoding = Encoding.UTF8;
                    settings.Indent = true;
                    settings.NewLineChars = "\n";
                    settings.NewLineHandling = NewLineHandling.None;
                    settings.NewLineOnAttributes = false;
                    settings.ConformanceLevel = ConformanceLevel.Document;
                    // settings.OmitXmlDeclaration = true;
                }
                else
                {
                    settings.Encoding = Encoding.UTF8;
                    settings.Indent = false;
                    settings.NewLineHandling = NewLineHandling.None;
                    settings.NewLineOnAttributes = false;
                    settings.ConformanceLevel = ConformanceLevel.Document;
                    // settings.OmitXmlDeclaration = true;
                }

                using (XmlWriter writer = XmlTextWriter.Create(ms, settings))
                {
                    xmls.Serialize(writer, obj, ns);
                }

                string xml = Encoding.UTF8.GetString(ms.ToArray());

                // remove preamble if exists
                string byteOrderMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
                while (xml.StartsWith(byteOrderMarkUtf8, StringComparison.Ordinal))
                {
                    xml = xml.Remove(0, byteOrderMarkUtf8.Length);
                }

                return xml;
            }
        }

        internal static T DeserializeJson<T>(string json)
        {
            if (String.IsNullOrEmpty(json)) throw new ArgumentNullException(nameof(json));

            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception e)
            {
                Console.WriteLine("");
                Console.WriteLine("Exception while deserializing:");
                Console.WriteLine(json);
                Console.WriteLine("");
                Console.WriteLine("Exception:");
                Console.WriteLine(SerializeJson(e, true));
                Console.WriteLine("");
                throw e;
            }
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
                  new JsonSerializerSettings
                  {
                      NullValueHandling = NullValueHandling.Ignore,
                      DateTimeZoneHandling = DateTimeZoneHandling.Local,
                  });
            }
            else
            {
                json = JsonConvert.SerializeObject(obj,
                  new JsonSerializerSettings
                  {
                      NullValueHandling = NullValueHandling.Ignore,
                      DateTimeZoneHandling = DateTimeZoneHandling.Local
                  });
            }

            return json;
        }
    }
}
