namespace WatsonTcp
{
    using System;

    /// <summary>
    /// Exception thrown when a connection is rejected during initialization.
    /// </summary>
    public class ConnectionRejectedException : Exception
    {
        /// <summary>
        /// Rejection status.
        /// </summary>
        public MessageStatus Status { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ConnectionRejectedException(string message, MessageStatus status = MessageStatus.ConnectionRejected) : base(message)
        {
            Status = status;
        }
    }
}
