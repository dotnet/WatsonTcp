using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Event arguments for when authentication is requested from a client.
    /// </summary>
    public class AuthenticationRequestedEventArgs
    {
        internal AuthenticationRequestedEventArgs(string ipPort)
        {
            IpPort = ipPort;
        }

        /// <summary>
        /// The IP:port of the client.
        /// </summary>
        public string IpPort { get; }
    }
}
