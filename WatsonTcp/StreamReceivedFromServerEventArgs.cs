using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Event arguments for when a stream is received from the server.
    /// </summary>
    public class StreamReceivedFromServerEventArgs
    {
        internal StreamReceivedFromServerEventArgs(Dictionary<object, object> metadata, long contentLength, Stream stream)
        {
            Metadata = metadata;
            ContentLength = contentLength;
            DataStream = stream;
        }

        /// <summary>
        /// The metadata received from the server.
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
