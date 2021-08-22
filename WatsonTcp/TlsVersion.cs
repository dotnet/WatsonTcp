using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace WatsonTcp
{
    /// <summary>
    /// Supported TLS versions
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum TlsVersion
    {
        /// <summary>
        /// Use TLS 1.2 (you can't go below this)
        /// </summary>
        Tls12,
        /// <summary>
        /// Use TLS 1.3 (only valid for .Net 5.0 or greater)
        /// </summary>
        Tls13,
    }
}
