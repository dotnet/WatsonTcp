using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WatsonTcp
{
    /// <summary>
    /// WatsonTcp message.
    /// </summary>
    public class WatsonMessage
    {
        #region Public-Members

        /// <summary>
        /// Length of the data.
        /// </summary>
        [JsonPropertyName("len")]
        public long ContentLength { get; set; }
         
        /// <summary>
        /// Preshared key for connection authentication.
        /// </summary>
        [JsonPropertyName("psk")]
        public byte[] PresharedKey
        {
            get
            {
                return _PresharedKey;
            }
            set
            {
                if (value == null)
                {
                    _PresharedKey = null; 
                }
                else
                {
                    if (value.Length != 16) throw new ArgumentException("PresharedKey must be 16 bytes.");

                    _PresharedKey = new byte[16];
                    Buffer.BlockCopy(value, 0, _PresharedKey, 0, 16); 
                }
            }
        }

        /// <summary>
        /// Status of the message.   
        /// </summary>
        [JsonPropertyName("status")]
        public MessageStatus Status { get; set; } = MessageStatus.Normal;

        /// <summary>
        /// Metadata dictionary; contains user-supplied metadata.
        /// </summary>
        [JsonPropertyName("md")]
        public Dictionary<string, object> Metadata { get; set; } = null;

        /// <summary>
        /// Indicates if the message is a synchronous request.
        /// </summary>
        [JsonPropertyName("syncreq")]
        public bool SyncRequest { get; set; } = false;

        /// <summary>
        /// Indicates if the message is a synchronous response.
        /// </summary>
        [JsonPropertyName("syncresp")]
        public bool SyncResponse { get; set; } = false;

        /// <summary>
        /// Indicates the current time as perceived by the sender; useful for determining expiration windows.
        /// </summary>
        [JsonPropertyName("ts")]
        public DateTime TimestampUtc = DateTime.UtcNow;

        /// <summary>
        /// Indicates an expiration time in UTC; only applicable to synchronous requests.
        /// </summary>
        [JsonPropertyName("exp")]
        public DateTime? ExpirationUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Indicates the conversation GUID of the message. 
        /// </summary>
        [JsonPropertyName("convguid")]
        public Guid ConversationGuid { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Stream containing the message data.
        /// </summary>
        [JsonIgnore]
        public Stream DataStream { get; set; } = null;

        #endregion

        #region Internal-Members

        #endregion

        #region Private-Members

        //                                         1         2         3
        //                                12345678901234567890123456789012
        private string _DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffzzz"; // 32 bytes
        private byte[] _PresharedKey = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public WatsonMessage()
        {

        }
         
        #endregion
        
        #region Public-Methods
         
        /// <summary>
        /// Human-readable string version of the object.
        /// </summary>
        /// <returns>String.</returns>
        public override string ToString()
        {
            string ret = "---" + Environment.NewLine; 
            ret += "  Preshared key     : " + (PresharedKey != null ? WatsonCommon.ByteArrayToHex(PresharedKey) : "null") + Environment.NewLine;
            ret += "  Status            : " + Status.ToString() + Environment.NewLine;
            ret += "  SyncRequest       : " + SyncRequest.ToString() + Environment.NewLine;
            ret += "  SyncResponse      : " + SyncResponse.ToString() + Environment.NewLine;
            ret += "  ExpirationUtc     : " + (ExpirationUtc != null ? ExpirationUtc.Value.ToString(_DateTimeFormat) : "null") + Environment.NewLine;
            ret += "  ConversationGuid  : " + ConversationGuid.ToString() + Environment.NewLine; 

            if (Metadata != null && Metadata.Count > 0)
            {
                ret += "  Metadata          : " + Metadata.Count + " entries" + Environment.NewLine;
            }
             
            if (DataStream != null)
                ret += "  DataStream        : present, " + ContentLength + " bytes" + Environment.NewLine;

            return ret;
        }

        #endregion Public-Methods

        #region Private-Methods

        #endregion
    }
}