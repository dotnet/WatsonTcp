namespace WatsonTcp
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Event arguments for when a stream is received.
    /// </summary>
    public class StreamReceivedEventArgs : EventArgs
    {
        #region Public-Members

        /// <summary>
        /// Client metadata.
        /// </summary>
        public ClientMetadata Client { get; } = null;

        /// <summary>
        /// The metadata received from the endpoint.
        /// </summary>
        public Dictionary<string, object> Metadata
        {
            get
            {
                return _Metadata;
            }
            set
            {
                if (value == null) _Metadata = new Dictionary<string, object>();
                else _Metadata = value;
            }
        }

        /// <summary>
        /// The number of data bytes that should be read from DataStream.
        /// </summary>
        public long ContentLength { get; }

        /// <summary>
        /// The stream containing the message data.
        /// For large proxied streams, WatsonTcp owns the underlying connection stream and will drain any unread
        /// bytes after the handler or callback returns.
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

        #endregion

        #region Private-Members

        private Dictionary<string, object> _Metadata = new Dictionary<string, object>();
        private byte[] _Data = null;
        private int _BufferSize = 65536;

        #endregion

        #region Constructors-and-Factories

        internal StreamReceivedEventArgs(ClientMetadata client, Dictionary<string, object> metadata, long contentLength, Stream stream)
        {
            Client = client;
            Metadata = metadata;
            ContentLength = contentLength;
            DataStream = stream;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        private byte[] ReadFromStream(Stream stream, long count)
        {
            if (count <= 0) return Array.Empty<byte>();
            if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(count), "Content length must fit within a byte array.");

            byte[] data = new byte[(int)count];
            int offset = 0;
            long bytesRemaining = count;

            while (bytesRemaining > 0)
            {
                int toRead = (int)Math.Min((long)_BufferSize, bytesRemaining);
                int read = stream.Read(data, offset, toRead);
                if (read > 0)
                {
                    offset += read;
                    bytesRemaining -= read;
                }
                else
                {
                    throw new IOException("Could not read from supplied stream.");
                }
            }

            return data;
        }

        #endregion
    }
}
