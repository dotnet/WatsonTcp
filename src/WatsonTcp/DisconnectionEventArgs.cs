using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Event arguments for when a disconnection is encountered.
    /// </summary>
    public class DisconnectionEventArgs : EventArgs
    {
        #region Public-Members

        /// <summary>
        /// Client metadata.
        /// </summary>
        public ClientMetadata Client { get; } = null;

        /// <summary>
        /// The reason for the disconnection.
        /// </summary>
        public DisconnectReason Reason { get; }

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal DisconnectionEventArgs(ClientMetadata client = null, DisconnectReason reason = DisconnectReason.Normal)
        {
            Client = client;
            Reason = reason;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
