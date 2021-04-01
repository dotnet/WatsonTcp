using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace WatsonTcp
{
    /// <summary>
    /// Mode.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    internal enum Mode
    {
        /// <summary>
        /// Tcp.
        /// </summary>
        [EnumMember(Value = "Tcp")]
        Tcp = 0,
        /// <summary>
        /// Ssl.
        /// </summary>
        [EnumMember(Value = "Ssl")]
        Ssl = 1
    }
}