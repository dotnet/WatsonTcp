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
        #region Public-Members

        /// <summary>
        /// Client metadata.
        /// </summary>
        public ClientMetadata Client { get; }
         
        /// <summary>
        /// The time at which the request expires.
        /// </summary>
        public DateTime ExpirationUtc { get; }

        /// <summary>
        /// Metadata attached to the request.
        /// </summary>
        public Dictionary<string, object> Metadata { get; }

        /// <summary>
        /// Request data.
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// Conversation GUID.
        /// </summary>
        public Guid ConversationGuid { get; } = Guid.NewGuid();

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="client">Client metadata.</param>
        /// <param name="convGuid">Conversation GUID.</param>
        /// <param name="expirationUtc">Expiration UTC timestamp.</param>
        /// <param name="metadata">Metadata.</param>
        /// <param name="data">Data.</param>
        public SyncRequest(ClientMetadata client, Guid convGuid, DateTime expirationUtc, Dictionary<string, object> metadata, byte[] data)
        {
            Client = client;
            ConversationGuid = convGuid;
            ExpirationUtc = expirationUtc;
            Metadata = metadata;

            if (data != null)
            {
                Data = new byte[data.Length];
                Buffer.BlockCopy(data, 0, Data, 0, data.Length);
            }
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
