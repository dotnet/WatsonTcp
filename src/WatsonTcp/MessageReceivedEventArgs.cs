namespace WatsonTcp
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Event arguments for when a message is received.
    /// </summary>
    public class MessageReceivedEventArgs : EventArgs
    {
        #region Public-Members

        /// <summary>
        /// Client metadata.
        /// </summary>
        public ClientMetadata Client { get; } = null;

        /// <summary>
        /// The metadata received from the endpoint.
        /// </summary>
        public Dictionary<string, object> Metadata { get; }

        /// <summary>
        /// The data received from the endpoint.
        /// </summary>
        public byte[] Data { get; }

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal MessageReceivedEventArgs(ClientMetadata client, Dictionary<string, object> metadata, byte[] data)
        {
            Client = client;
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
