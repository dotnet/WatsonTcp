using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Event arguments for when a client disconnects from the server.
    /// </summary>
    public class ClientDisconnectedEventArgs
    {
        internal ClientDisconnectedEventArgs(string ipPort, DisconnectReason reason)
        {
            IpPort = ipPort;
            Reason = reason;
        }

        /// <summary>
        /// The IP:port of the client.
        /// </summary>
        public string IpPort { get; }

        /// <summary>
        /// The reason for the disconnection.
        /// </summary>
        public DisconnectReason Reason { get; }
    }
}
