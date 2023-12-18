namespace WatsonTcp
{
    using System.Text.Json.Serialization;
    using System.Runtime.Serialization;

    /// <summary>
    /// Reason why a client disconnected.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DisconnectReason
    {
        /// <summary>
        /// Normal disconnection.
        /// </summary>
        [EnumMember(Value = "Normal")]
        Normal = 0,
        /// <summary>
        /// Client connection was intentionally terminated programmatically or by the server.
        /// </summary>
        [EnumMember(Value = "Removed")]
        Removed = 1,
        /// <summary>
        /// Client connection timed out; server did not receive data within the timeout window.
        /// </summary>
        [EnumMember(Value = "Timeout")]
        Timeout = 2,
        /// <summary>
        /// Disconnect due to server shutdown.
        /// </summary>
        [EnumMember(Value = "Shutdown")]
        Shutdown = 3,
        /// <summary>
        /// Disconnect due to authentication failure.
        /// </summary>
        [EnumMember(Value = "AuthFailure")]
        AuthFailure
    }
}
