namespace WatsonTcp
{
    using System;

    /// <summary>
    /// Event arguments for rejected connections.
    /// </summary>
    public class ConnectionRejectedEventArgs : EventArgs
    {
        /// <summary>
        /// Client metadata.
        /// </summary>
        public ClientMetadata Client { get; }

        /// <summary>
        /// Rejection reason.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Rejection status.
        /// </summary>
        public MessageStatus Status { get; }

        internal ConnectionRejectedEventArgs(ClientMetadata client, string reason, MessageStatus status = MessageStatus.ConnectionRejected)
        {
            Client = client;
            Reason = reason;
            Status = status;
        }
    }
}
