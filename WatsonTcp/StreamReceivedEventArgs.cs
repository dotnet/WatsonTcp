using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Event arguments for when a stream is received.
    /// </summary>
    public class StreamReceivedEventArgs : EventArgs
    {
        internal StreamReceivedEventArgs(string ipPort, Dictionary<object, object> metadata, long contentLength, Stream stream)
        {
            IpPort = ipPort;
            Metadata = metadata;
            ContentLength = contentLength;
            DataStream = stream;
        }

        /// <summary>
        /// The IP:port of the endpoint.
        /// </summary>
        public string IpPort { get; }

        /// <summary>
        /// The metadata received from the endpoint.
        /// </summary>
        public Dictionary<object, object> Metadata
        {
            get
            {
                return _Metadata;
            }
            set
            {
                if (value == null) _Metadata = new Dictionary<object, object>();
                else _Metadata = value;
            }
        }

        /// <summary>
        /// The number of data bytes that should be read from DataStream.
        /// </summary>
        public long ContentLength { get; }

        /// <summary>
        /// The stream containing the message data.
        /// </summary>
        public Stream DataStream { get; }

        /// <summary>
        /// The data from DataStream.
        /// Using Data will fully read the contents of DataStream.
        /// </summary>
        public byte[] Data
        {
            get
            {
                if (_Data != null) return _Data;
                if (ContentLength <= 0) return null;
                _Data = ReadFromStream(DataStream, ContentLength);
                return _Data;
            }
        }

        private Dictionary<object, object> _Metadata = new Dictionary<object, object>();
        private byte[] _Data = null;
        private int _BufferSize = 65536;

        private byte[] ReadFromStream(Stream stream, long count)
        {
            if (count <= 0) return new byte[0]; 
            byte[] buffer = new byte[_BufferSize];

            int read = 0;
            long bytesRemaining = count;
            MemoryStream ms = new MemoryStream();

            while (bytesRemaining > 0)
            {
                if (_BufferSize > bytesRemaining) buffer = new byte[bytesRemaining];

                read = stream.Read(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    ms.Write(buffer, 0, read);
                    bytesRemaining -= read;
                }
                else
                {
                    throw new IOException("Could not read from supplied stream.");
                }
            }

            byte[] data = ms.ToArray();
            return data;
        } 
    }
}
