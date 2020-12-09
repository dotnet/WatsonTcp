using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace WatsonTcp
{
    /// <summary>
    /// Message status.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MessageStatus
    {
        /// <summary>
        /// Normal
        /// </summary>
        [EnumMember(Value = "Normal")] 
        Normal = 0,
        /// <summary>
        /// Success
        /// </summary>
        [EnumMember(Value = "Success")] 
        Success = 1,
        /// <summary>
        /// Failure
        /// </summary>
        [EnumMember(Value = "Failure")] 
        Failure = 2,
        /// <summary>
        /// AuthRequired
        /// </summary>
        [EnumMember(Value = "AuthRequired")] 
        AuthRequired = 3,
        /// <summary>
        /// AuthRequested
        /// </summary>
        [EnumMember(Value = "AuthRequested")] 
        AuthRequested = 4,
        /// <summary>
        /// AuthSuccess
        /// </summary>
        [EnumMember(Value = "AuthSuccess")] 
        AuthSuccess = 5,
        /// <summary>
        /// AuthFailure
        /// </summary>
        [EnumMember(Value = "AuthFailure")] 
        AuthFailure = 6,
        /// <summary>
        /// Removed
        /// </summary>
        [EnumMember(Value = "Removed")] 
        Removed = 7,
        /// <summary>
        /// Shutdown
        /// </summary>
        [EnumMember(Value = "Shutdown")]
        Shutdown = 8,
        /// <summary>
        /// Heartbeat
        /// </summary>
        [EnumMember(Value = "Heartbeat")]
        Heartbeat = 9,
        /// <summary>
        /// Timeout
        /// </summary>
        [EnumMember(Value = "Timeout")]
        Timeout = 10
    }
}