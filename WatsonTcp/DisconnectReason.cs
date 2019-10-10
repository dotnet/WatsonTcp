using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Reason why a client disconnected.
    /// </summary>
    public enum DisconnectReason
    {
        /// <summary>
        /// Normal disconnection.
        /// </summary>
        Normal = 0,
        /// <summary>
        /// Client connection was intentionally terminated programmatically or by the server.
        /// </summary>
        Kicked = 1,
        /// <summary>
        /// Client connection timed out; server did not receive data within the timeout window.
        /// </summary>
        Timeout = 2
    }
}
