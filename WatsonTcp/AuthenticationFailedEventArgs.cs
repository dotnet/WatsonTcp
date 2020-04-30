using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Event arguments for when a client fails authentication.
    /// </summary>
    public class AuthenticationFailedEventArgs
    {
        internal AuthenticationFailedEventArgs(string ipPort)
        {
            IpPort = ipPort;
        }

        /// <summary>
        /// The IP:port of the client.
        /// </summary>
        public string IpPort { get; }
    }
}
