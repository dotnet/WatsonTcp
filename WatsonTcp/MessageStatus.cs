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
        /// Object
        /// </summary>
        [EnumMember(Value = "Normal")] 
        Normal = 0,
        /// <summary>
        /// Object
        /// </summary>
        [EnumMember(Value = "Success")] 
        Success = 1,
        /// <summary>
        /// Object
        /// </summary>
        [EnumMember(Value = "Failure")] 
        Failure = 2,
        /// <summary>
        /// Object
        /// </summary>
        [EnumMember(Value = "AuthRequired")] 
        AuthRequired = 3,
        /// <summary>
        /// Object
        /// </summary>
        [EnumMember(Value = "AuthRequested")] 
        AuthRequested = 4,
        /// <summary>
        /// Object
        /// </summary>
        [EnumMember(Value = "AuthSuccess")] 
        AuthSuccess = 5,
        /// <summary>
        /// Object
        /// </summary>
        [EnumMember(Value = "AuthFailure")] 
        AuthFailure = 6,
        /// <summary>
        /// Object
        /// </summary>
        [EnumMember(Value = "Removed")] 
        Removed = 7,
        /// <summary>
        /// Object
        /// </summary>
        [EnumMember(Value = "Disconnecting")] 
        Disconnecting = 8
    }
}