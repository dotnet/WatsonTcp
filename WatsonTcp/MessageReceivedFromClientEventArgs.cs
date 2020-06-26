using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Event arguments for when a message is received from a client.
    /// </summary>
    public class MessageReceivedFromClientEventArgs<TMetadata>
    {
        internal MessageReceivedFromClientEventArgs(string ipPort, TMetadata metadata, byte[] data)
        {
            IpPort = ipPort;
            Metadata = metadata;
            Data = data;
        }

        /// <summary>
        /// The IP:port of the client.
        /// </summary>
        public string IpPort { get; }

        /// <summary>
        /// The metadata received from the client.
        /// </summary>
        public TMetadata Metadata { get; }

        /// <summary>
        /// The data received from the client.
        /// </summary>
        public byte[] Data { get; }
    }
}
