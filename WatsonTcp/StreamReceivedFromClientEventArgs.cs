using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Event arguments for when a stream is received from a client.
    /// </summary>
    public class StreamReceivedFromClientEventArgs
    {
        internal StreamReceivedFromClientEventArgs(string ipPort, Dictionary<object, object> metadata, long contentLength, Stream stream)
        {
            IpPort = ipPort;
            Metadata = metadata;
            ContentLength = contentLength;
            DataStream = stream;
        }

        /// <summary>
        /// The IP:port of the client.
        /// </summary>
        public string IpPort { get; }

        /// <summary>
        /// The metadata received from the client.
        /// </summary>
        public Dictionary<object, object> Metadata { get; }

        /// <summary>
        /// The number of data bytes that should be read from DataStream.
        /// </summary>
        public long ContentLength { get; }

        /// <summary>
        /// The stream containing the message data.
        /// </summary>
        public Stream DataStream { get; }
    }
}
