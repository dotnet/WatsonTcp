namespace WatsonTcp
{
    using System;

    /// <summary>
    /// Event arguments for successful handshakes.
    /// </summary>
    public class HandshakeSucceededEventArgs : EventArgs
    {
        /// <summary>
        /// Client metadata.
        /// </summary>
        public ClientMetadata Client { get; }

        internal HandshakeSucceededEventArgs(ClientMetadata client = null)
        {
            Client = client;
        }
    }
}
