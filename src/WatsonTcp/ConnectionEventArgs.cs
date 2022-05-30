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
        internal ConnectionEventArgs(string ipPort)
        {
            IpPort = ipPort;
        }

        /// <summary>
        /// The IP:port of the endpoint to which the connection was established.
        /// </summary>
        public string IpPort { get; }
    }
}
