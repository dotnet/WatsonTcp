namespace WatsonTcp
{
    /// <summary>
    /// Connection authorization result.
    /// </summary>
    public class ConnectionAuthorizationResult
    {
        /// <summary>
        /// Indicates whether or not the connection is allowed.
        /// </summary>
        public bool Allowed { get; set; } = true;

        /// <summary>
        /// Optional rejection reason.
        /// </summary>
        public string Reason { get; set; } = null;

        /// <summary>
        /// Message status to use if the connection is rejected.
        /// </summary>
        public MessageStatus RejectionStatus { get; set; } = MessageStatus.ConnectionRejected;

        /// <summary>
        /// Create an allow result.
        /// </summary>
        public static ConnectionAuthorizationResult Allow()
        {
            return new ConnectionAuthorizationResult
            {
                Allowed = true
            };
        }

        /// <summary>
        /// Create a rejection result.
        /// </summary>
        public static ConnectionAuthorizationResult Reject(string reason, MessageStatus rejectionStatus = MessageStatus.ConnectionRejected)
        {
            return new ConnectionAuthorizationResult
            {
                Allowed = false,
                Reason = reason,
                RejectionStatus = rejectionStatus
            };
        }
    }
}
