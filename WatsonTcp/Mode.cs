using System.Runtime.Serialization;

namespace WatsonTcp
{
    /// <summary>
    /// Mode.
    /// </summary>
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