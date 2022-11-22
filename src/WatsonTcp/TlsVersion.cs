using System.Text.Json;
using System.Text.Json.Serialization;

namespace WatsonTcp
{
    /// <summary>
    /// Supported TLS versions.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
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
