using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Internal EventArgs for passing arguments for SyncResponseReceived event.
    /// </summary>
    internal class SyncResponseReceivedEventArgs
    {
        #region Public-Members

        /// <summary>
        /// Message.
        /// </summary>
        public WatsonMessage Message { get; set; } = null;

        /// <summary>
        /// Data.
        /// </summary>
        public byte[] Data { get; set; } = null;

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="msg">Message.</param>
        /// <param name="data">Data.</param>
        public SyncResponseReceivedEventArgs(WatsonMessage msg, byte[] data)
        {
            Message = msg;
            Data = data;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
