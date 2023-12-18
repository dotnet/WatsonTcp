namespace WatsonTcp
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Response to a synchronous request.
    /// </summary>
    public class SyncResponse
    {
        #region Public-Members

        /// <summary>
        /// Metadata to attach to the response.
        /// </summary>
        public Dictionary<string, object> Metadata { get; } = new Dictionary<string, object>();

        /// <summary>
        /// Data to attach to the response.
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// Conversation GUID.
        /// </summary>
        public Guid ConversationGuid { get; } = Guid.NewGuid();

        /// <summary>
        /// The time at which the request expires.
        /// </summary>
        public DateTime ExpirationUtc { get; }

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="req">The synchronous request for which this response is intended.</param>
        /// <param name="data">Data to send as a response.</param>
        public SyncResponse(SyncRequest req, string data)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));  
            ExpirationUtc = req.ExpirationUtc;
            ConversationGuid = req.ConversationGuid;

            if (String.IsNullOrEmpty(data)) Data = Array.Empty<byte>();
            else Data = Encoding.UTF8.GetBytes(data);
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="req">The synchronous request for which this response is intended.</param>
        /// <param name="data">Data to send as a response.</param>
        public SyncResponse(SyncRequest req, byte[] data)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ExpirationUtc = req.ExpirationUtc;
            ConversationGuid = req.ConversationGuid;
            Data = data;
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="req">The synchronous request for which this response is intended.</param>
        /// <param name="metadata">Metadata to attach to the response.</param>
        /// <param name="data">Data to send as a response.</param>
        public SyncResponse(SyncRequest req, Dictionary<string, object> metadata, string data)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ExpirationUtc = req.ExpirationUtc;
            ConversationGuid = req.ConversationGuid;

            Metadata = metadata;

            if (String.IsNullOrEmpty(data))
            {
                Data = Array.Empty<byte>();
            }
            else
            {
                Data = Encoding.UTF8.GetBytes(data);
            }
        }

        /// <summary>
        /// Instantiate.
        /// </summary> 
        /// <param name="req">The synchronous request for which this response is intended.</param>
        /// <param name="metadata">Metadata to attach to the response.</param>
        /// <param name="data">Data to send as a response.</param>
        public SyncResponse(SyncRequest req, Dictionary<string, object> metadata, byte[] data)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ExpirationUtc = req.ExpirationUtc;
            ConversationGuid = req.ConversationGuid;

            Metadata = metadata;
            Data = data;
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="convGuid"></param>
        /// <param name="expirationUtc"></param>
        /// <param name="metadata"></param>
        /// <param name="data"></param>
        public SyncResponse(Guid convGuid, DateTime expirationUtc, Dictionary<string, object> metadata, byte[] data)
        {
            ConversationGuid = convGuid;
            ExpirationUtc = expirationUtc;
            Metadata = metadata;
            Data = data;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
