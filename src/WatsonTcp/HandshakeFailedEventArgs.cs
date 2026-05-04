namespace WatsonTcp
{
    using System;

    /// <summary>
    /// Event arguments for failed handshakes.
    /// </summary>
    public class HandshakeFailedEventArgs : EventArgs
    {
        /// <summary>
        /// Client metadata.
        /// </summary>
        public ClientMetadata Client { get; }

        /// <summary>
        /// Failure reason.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Failure status.
        /// </summary>
        public MessageStatus Status { get; }

        internal HandshakeFailedEventArgs(ClientMetadata client, string reason, MessageStatus status = MessageStatus.HandshakeFailure)
        {
            Client = client;
            Reason = reason;
            Status = status;
        }
    }
}
