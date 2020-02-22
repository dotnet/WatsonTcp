using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Event arguments for when a message is received from the server.
    /// </summary>
    public class MessageReceivedFromServerEventArgs
    {
        internal MessageReceivedFromServerEventArgs(Dictionary<object, object> metadata, byte[] data)
        {
            Metadata = metadata;
            Data = data;
        }

        /// <summary>
        /// The metadata received from the server.
        /// </summary>
        public Dictionary<object, object> Metadata { get; }

        /// <summary>
        /// The data received from the server.
        /// </summary>
        public byte[] Data { get; }
    }
}
