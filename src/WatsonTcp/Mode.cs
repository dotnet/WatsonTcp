using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.Serialization;

namespace WatsonTcp
{
    /// <summary>
    /// Mode.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
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