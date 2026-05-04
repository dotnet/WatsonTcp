namespace WatsonTcp
{
    /// <summary>
    /// Handshake result.
    /// </summary>
    public class HandshakeResult
    {
        /// <summary>
        /// Indicates whether or not the handshake succeeded.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Optional failure reason.
        /// </summary>
        public string Reason { get; set; } = null;

        /// <summary>
        /// Message status to use if the handshake fails.
        /// </summary>
        public MessageStatus FailureStatus { get; set; } = MessageStatus.HandshakeFailure;

        /// <summary>
        /// Create a success result.
        /// </summary>
        public static HandshakeResult Succeed()
        {
            return new HandshakeResult
            {
                Success = true
            };
        }

        /// <summary>
        /// Create a failure result.
        /// </summary>
        public static HandshakeResult Fail(string reason, MessageStatus failureStatus = MessageStatus.HandshakeFailure)
        {
            return new HandshakeResult
            {
                Success = false,
                Reason = reason,
                FailureStatus = failureStatus
            };
        }
    }
}
