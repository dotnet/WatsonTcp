namespace WatsonTcp
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class WatsonMessageBuilder
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

        internal int MaxHeaderSize
        {
            get => _MaxHeaderSize;
            set
            {
                if (value < 25) throw new ArgumentOutOfRangeException(nameof(MaxHeaderSize));
                _MaxHeaderSize = value;
            }
        }

        #endregion

        #region Private-Members

        private ISerializationHelper _SerializationHelper = new DefaultSerializationHelper();
        private int _ReadStreamBuffer = 65536;
        private int _MaxHeaderSize = 262144;

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
#pragma warning disable CA1822 // Mark members as static - called as instance method via _MessageBuilder.ConstructNew(...)
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
#pragma warning restore CA1822

        /// <summary>
        /// Read from a stream and construct a message.
        /// </summary>
        /// <param name="stream">Stream.</param>
        /// <param name="token">Cancellation token.</param>
        internal async Task<WatsonMessage> BuildFromStream(Stream stream, CancellationToken token = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Cannot read from stream.");

            // Read header bytes until \r\n\r\n delimiter is found.
            // Uses a MemoryStream accumulator instead of array concatenation,
            // and direct byte comparison instead of LINQ, to avoid O(n^2)
            // allocations and per-iteration LINQ overhead.
            byte[] headerBuffer = new byte[1];
            int totalRead = 0;

            // Track the last 4 bytes for delimiter detection
            // Initialize to non-matching values
            byte prev3 = 0xFF, prev2 = 0xFF, prev1 = 0xFF, prev0 = 0xFF;
            bool hasNonZero = false;

            using (MemoryStream headerStream = new MemoryStream(256))
            {
                while (true)
                {
                    int read = await stream.ReadAsync(headerBuffer, 0, 1, token).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        return null;
                    }

                    byte b = headerBuffer[0];
                    headerStream.WriteByte(b);
                    totalRead++;

                    if (b != 0) hasNonZero = true;

                    // Shift the trailing 4-byte window
                    prev3 = prev2;
                    prev2 = prev1;
                    prev1 = prev0;
                    prev0 = b;

                    // Check for null header (all zeros) at byte 4
                    if (totalRead == 4 && !hasNonZero)
                    {
                        throw new IOException("Null header data indicates peer disconnected.");
                    }

                    // Check for \r\n\r\n delimiter (13, 10, 13, 10)
                    if (totalRead >= 4
                        && prev3 == 13
                        && prev2 == 10
                        && prev1 == 13
                        && prev0 == 10)
                    {
                        break;
                    }

                    // Enforce maximum header size
                    if (totalRead >= _MaxHeaderSize)
                    {
                        throw new IOException("Header size exceeds maximum allowed size of " + _MaxHeaderSize + " bytes.");
                    }
                }

                // Return header bytes without the trailing \r\n\r\n delimiter
                byte[] allBytes = headerStream.ToArray();
                int headerLength = allBytes.Length - 4;

                WatsonMessage msg = _SerializationHelper.DeserializeJson<WatsonMessage>(Encoding.UTF8.GetString(allBytes, 0, headerLength));
                msg.DataStream = stream;
                return msg;
            }
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
            byte[] result = new byte[jsonBytes.Length + 4];
            Buffer.BlockCopy(jsonBytes, 0, result, 0, jsonBytes.Length);
            result[jsonBytes.Length] = 13;     // \r
            result[jsonBytes.Length + 1] = 10; // \n
            result[jsonBytes.Length + 2] = 13; // \r
            result[jsonBytes.Length + 3] = 10; // \n
            return result;
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
