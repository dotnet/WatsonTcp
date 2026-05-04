namespace WatsonTcp
{
    using System;

    /// <summary>
    /// Exception thrown when a handshake fails during initialization.
    /// </summary>
    public class HandshakeFailedException : Exception
    {
        /// <summary>
        /// Failure status.
        /// </summary>
        public MessageStatus Status { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HandshakeFailedException(string message, MessageStatus status = MessageStatus.HandshakeFailure) : base(message)
        {
            Status = status;
        }
    }
}
