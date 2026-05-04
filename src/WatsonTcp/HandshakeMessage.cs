namespace WatsonTcp
{
    using System.Collections.Generic;

    /// <summary>
    /// Framed handshake message.
    /// </summary>
    public class HandshakeMessage
    {
        /// <summary>
        /// Message type identifier.
        /// </summary>
        public string Type { get; set; } = null;

        /// <summary>
        /// Optional metadata.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = null;

        /// <summary>
        /// Optional payload data.
        /// </summary>
        public byte[] Data { get; set; } = null;
    }
}
