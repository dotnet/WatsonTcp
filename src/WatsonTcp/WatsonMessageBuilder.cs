namespace WatsonTcp
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    internal class WatsonMessageBuilder
    {
        #region Internal-Members

        internal ISerializationHelper SerializationHelper
        {
            get => _SerializationHelper;
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(SerializationHelper));
                _SerializationHelper = value;
            }
        }

        internal int ReadStreamBuffer
        {
            get => _ReadStreamBuffer;
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(ReadStreamBuffer));
                _ReadStreamBuffer = value;
            }
        }

        #endregion

        #region Private-Members

        private ISerializationHelper _SerializationHelper = new DefaultSerializationHelper();
        private int _ReadStreamBuffer = 65536;

        #endregion

        #region Constructors-and-Factories

        internal WatsonMessageBuilder()
        {

        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Construct a new message to send.
        /// </summary>
        /// <param name="contentLength">The number of bytes included in the stream.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <param name="syncRequest">Indicate if the message is a synchronous message request.</param>
        /// <param name="syncResponse">Indicate if the message is a synchronous message response.</param>
        /// <param name="expirationUtc">The UTC time at which the message should expire (only valid for synchronous message requests).</param>
        /// <param name="metadata">Metadata to attach to the message.</param>
        internal WatsonMessage ConstructNew(
            long contentLength,
            Stream stream,
            bool syncRequest = false,
            bool syncResponse = false,
            DateTime? expirationUtc = null,
            Dictionary<string, object> metadata = null)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (contentLength > 0)
            {
                if (stream == null || !stream.CanRead)
                {
                    throw new ArgumentException("Cannot read from supplied stream.");
                }
            }

            WatsonMessage msg = new WatsonMessage();
            msg.ContentLength = contentLength;
            msg.DataStream = stream;
            msg.SyncRequest = syncRequest;
            msg.SyncResponse = syncResponse;
            msg.ExpirationUtc = expirationUtc;
            msg.Metadata = metadata;

            return msg;
        }

        /// <summary>
        /// Read from a stream and construct a message.
        /// </summary>
        /// <param name="stream">Stream.</param>
        /// <param name="token">Cancellation token.</param>
        internal async Task<WatsonMessage> BuildFromStream(Stream stream, CancellationToken token = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Cannot read from stream.");

            WatsonMessage msg = new WatsonMessage();

            // {"len":0,"s":"Normal"}\r\n\r\n
            byte[] headerBytes = new byte[24];
            byte[] headerBuffer = new byte[1];
            int read = 0;
            int readTotal = 0;

            while (true)
            {
                #region Retrieve-First-24-Bytes

                read = await stream.ReadAsync(headerBytes, readTotal, (24 - readTotal), token).ConfigureAwait(false);

                if (read > 0)
                {
                    readTotal += read;
                    if (readTotal >= 24) break;
                }

                #endregion
            }

            while (true)
            {
                #region Read-Byte-by-Byte

                byte[] endCheck = headerBytes.Skip(headerBytes.Length - 4).Take(4).ToArray();

                if ((int)endCheck[3] == 0
                    && (int)endCheck[2] == 0
                    && (int)endCheck[1] == 0
                    && (int)endCheck[0] == 0)
                {
                    throw new IOException("Null header data indicates peer disconnected.");
                }

                if ((int)endCheck[3] == 10
                    && (int)endCheck[2] == 13
                    && (int)endCheck[1] == 10
                    && (int)endCheck[0] == 13)
                {
                    // delimiter reached
                    break;
                }

                read = await stream.ReadAsync(headerBuffer, 0, 1, token).ConfigureAwait(false);
                if (read > 0)
                    headerBytes = WatsonCommon.AppendBytes(headerBytes, headerBuffer);

                #endregion
            }

            msg = _SerializationHelper.DeserializeJson<WatsonMessage>(Encoding.UTF8.GetString(headerBytes));
            msg.DataStream = stream;

            return msg;
        }

        /// <summary>
        /// Retrieve header bytes for a message.
        /// </summary>
        /// <param name="msg">Watson message.</param>
        /// <returns>Header bytes.</returns>
        internal byte[] GetHeaderBytes(WatsonMessage msg)
        {
            string jsonStr = _SerializationHelper.SerializeJson(msg, false);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonStr);
            byte[] end = Encoding.UTF8.GetBytes("\r\n\r\n");
            byte[] final = WatsonCommon.AppendBytes(jsonBytes, end);
            return final;
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
