using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace WatsonTcp
{
    /// <summary>
    /// Supported TLS versions.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum TlsVersion
    {
        /// <summary>
        /// Use TLS version 1.2 (this is the minimum version).
        /// </summary>
        Tls12,
        /// <summary>
        /// Use TLS version 1.3 (only valid for .Net 5.0 or greater).
        /// </summary>
        Tls13
    }
}
