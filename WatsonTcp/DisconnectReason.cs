using System;
using System.Collections.Generic;
using System.Text; 
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace WatsonTcp
{
    /// <summary>
    /// Reason why a client disconnected.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
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
        Shutdown = 3
    }
}
