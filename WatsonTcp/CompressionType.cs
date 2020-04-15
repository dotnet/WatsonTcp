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
    /// The type of compression.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum CompressionType
    {
        /// <summary>
        /// Object
        /// </summary>
        [EnumMember(Value = "None")]
        None,
        /// <summary>
        /// Object
        /// </summary>
        [EnumMember(Value = "Gzip")]
        Gzip,
        /// <summary>
        /// Object
        /// </summary>
        [EnumMember(Value = "Deflate")]
        Deflate
    }
}
