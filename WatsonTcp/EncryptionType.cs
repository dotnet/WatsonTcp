using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace WatsonTcp
{
    /// <summary>
    /// The type of encryption.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum EncryptionType
    {
        /// <summary>
        /// No encryption
        /// </summary>
        [EnumMember(Value = "None")]
        None,
        /// <summary>
        /// AES256 encryption
        /// </summary>
        [EnumMember(Value = "Aes")]
        Aes,
        /// <summary>
        /// TripleDes encryption
        /// </summary>
        [EnumMember(Value = "TripleDes")]
        TripleDes
    }
}