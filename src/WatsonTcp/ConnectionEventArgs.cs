using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Event arguments for when a connection is established.
    /// </summary>
    public class ConnectionEventArgs : EventArgs
    {
        #region Public-Members

        /// <summary>
        /// Client metadata.
        /// </summary>
        public ClientMetadata Client { get; } = null;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        internal ConnectionEventArgs(ClientMetadata client = null)
        {
            Client = client;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
