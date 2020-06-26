using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.Serialization;

namespace WatsonTcp
{
    /// <summary>
    /// The type of compression.
    /// </summary>
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
