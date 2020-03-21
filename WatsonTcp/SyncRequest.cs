using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Request that demands a response within a specific timeout.
    /// </summary>
    public class SyncRequest
    {
        /// <summary>
        /// IP:port from which the request was received.
        /// </summary>
        public string IpPort { get; }
         
        /// <summary>
        /// The time at which the request expires.
        /// </summary>
        public DateTime ExpirationUtc { get; }

        /// <summary>
        /// Metadata attached to the request.
        /// </summary>
        public Dictionary<object, object> Metadata { get; }

        /// <summary>
        /// Request data.
        /// </summary>
        public byte[] Data { get; }

        internal string ConversationGuid { get; }

        internal SyncRequest(string ipPort, string convGuid, DateTime expirationUtc, Dictionary<object, object> metadata, byte[] data)
        {
            IpPort = ipPort;
            ConversationGuid = convGuid;
            ExpirationUtc = expirationUtc;
            Metadata = metadata;

            if (data != null)
            {
                Data = new byte[data.Length];
                Buffer.BlockCopy(data, 0, Data, 0, data.Length);
            }
        }
    }
}
