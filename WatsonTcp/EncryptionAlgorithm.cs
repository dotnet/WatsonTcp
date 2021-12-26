using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace WatsonTcp
{
    /// <summary>
    /// The type of encryption.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum EncryptionAlgorithm
    {
        /// <summary>
        /// No encryption
        /// </summary>
        [EnumMember(Value = "None")]
        None,
        /// <summary>
        /// Aes256 encryption
        /// </summary>
        [EnumMember(Value = "Aes")]
        Aes,
        /// <summary>
        /// Rijndael encryption
        /// </summary>
        [EnumMember(Value = "Rijndael")]
        Rijndael,
        /// <summary>
        /// RC2 encryption
        /// </summary>
        [EnumMember(Value = "RC2")]
        Rc2,
        /// <summary>
        /// TripleDes encryption
        /// </summary>
        [EnumMember(Value = "TripleDes")]
        TripleDes
    }
}