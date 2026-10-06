namespace WatsonTcp
{
    using System.Text.Json.Serialization;
    using System.Runtime.Serialization;

    /// <summary>
    /// Mode.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Mode>))]
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