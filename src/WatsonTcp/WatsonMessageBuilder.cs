namespace WatsonTcp
{
    using System;
    using System.Buffers;
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
#pragma warning disable CA1822
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

            WatsonMessage msg = new WatsonMessage
            {
                ContentLength = contentLength,
                DataStream = stream,
                SyncRequest = syncRequest,
                SyncResponse = syncResponse,
                ExpirationUtc = expirationUtc,
                Metadata = metadata
            };

            return msg;
        }
#pragma warning restore CA1822

        /// <summary>
        /// Read from a stream and construct a message.
        /// </summary>
        /// <param name="stream">Stream.</param>
        /// <param name="token">Cancellation token.</param>
        internal async Task<WatsonMessage> BuildFromStream(BufferedReadStream stream, CancellationToken token = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Cannot read from stream.");

            int initialHeaderCapacity = Math.Min(Math.Max(_ReadStreamBuffer, 256), _MaxHeaderSize);
            byte[] readBuffer = ArrayPool<byte>.Shared.Rent(_ReadStreamBuffer);
            byte[] headerBuffer = ArrayPool<byte>.Shared.Rent(initialHeaderCapacity);
            int headerLength = 0;
            int searchStart = 0;

            try
            {
                while (true)
                {
                    int read = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, token).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        throw new IOException("Peer disconnected.");
                    }

                    EnsureHeaderCapacity(ref headerBuffer, headerLength + read);
                    Buffer.BlockCopy(readBuffer, 0, headerBuffer, headerLength, read);
                    int totalRead = headerLength + read;

                    if (totalRead >= 4
                        && headerBuffer[0] == 0
                        && headerBuffer[1] == 0
                        && headerBuffer[2] == 0
                        && headerBuffer[3] == 0)
                    {
                        throw new IOException("Null header data indicates peer disconnected.");
                    }

                    int delimiterIndex = FindHeaderDelimiter(headerBuffer, searchStart, totalRead);
                    if (delimiterIndex >= 0)
                    {
                        if (delimiterIndex > _MaxHeaderSize)
                        {
                            throw new IOException("Header size exceeds maximum allowed size of " + _MaxHeaderSize + " bytes.");
                        }

                        int payloadOffset = delimiterIndex + 4;
                        int bufferedPayloadBytes = totalRead - payloadOffset;
                        if (bufferedPayloadBytes > 0)
                        {
                            stream.PushBack(headerBuffer, payloadOffset, bufferedPayloadBytes);
                        }

                        WatsonMessage msg = DeserializeWatsonMessage(headerBuffer, delimiterIndex);
                        msg.DataStream = stream;
                        return msg;
                    }

                    headerLength = totalRead;
                    if (headerLength >= _MaxHeaderSize)
                    {
                        throw new IOException("Header size exceeds maximum allowed size of " + _MaxHeaderSize + " bytes.");
                    }

                    searchStart = Math.Max(0, headerLength - 3);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(readBuffer);
                ArrayPool<byte>.Shared.Return(headerBuffer);
            }
        }

        /// <summary>
        /// Retrieve header bytes for a message.
        /// </summary>
        /// <param name="msg">Watson message.</param>
        /// <returns>Header bytes.</returns>
        internal byte[] GetHeaderBytes(WatsonMessage msg)
        {
            if (_SerializationHelper is DefaultSerializationHelper defaultHelper)
            {
                byte[] jsonBytes = defaultHelper.SerializeJsonBytes(msg, false);
                byte[] result = new byte[jsonBytes.Length + 4];
                Buffer.BlockCopy(jsonBytes, 0, result, 0, jsonBytes.Length);
                result[jsonBytes.Length] = 13;
                result[jsonBytes.Length + 1] = 10;
                result[jsonBytes.Length + 2] = 13;
                result[jsonBytes.Length + 3] = 10;
                return result;
            }

            string jsonStr = _SerializationHelper.SerializeJson(msg, false);
            byte[] encodedJson = Encoding.UTF8.GetBytes(jsonStr);
            byte[] headerBytes = new byte[encodedJson.Length + 4];
            Buffer.BlockCopy(encodedJson, 0, headerBytes, 0, encodedJson.Length);
            headerBytes[encodedJson.Length] = 13;
            headerBytes[encodedJson.Length + 1] = 10;
            headerBytes[encodedJson.Length + 2] = 13;
            headerBytes[encodedJson.Length + 3] = 10;
            return headerBytes;
        }

        #endregion

        #region Private-Methods

        private WatsonMessage DeserializeWatsonMessage(byte[] headerBytes, int headerLength)
        {
            if (_SerializationHelper is DefaultSerializationHelper defaultHelper)
            {
                return defaultHelper.DeserializeJson<WatsonMessage>(new ReadOnlySpan<byte>(headerBytes, 0, headerLength));
            }

            return _SerializationHelper.DeserializeJson<WatsonMessage>(Encoding.UTF8.GetString(headerBytes, 0, headerLength));
        }

        private void EnsureHeaderCapacity(ref byte[] buffer, int requiredLength)
        {
            if (buffer.Length >= requiredLength) return;

            int newLength = buffer.Length;
            while (newLength < requiredLength)
            {
                newLength *= 2;
            }

            byte[] resized = ArrayPool<byte>.Shared.Rent(newLength);
            Buffer.BlockCopy(buffer, 0, resized, 0, buffer.Length);
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = resized;
        }

        private static int FindHeaderDelimiter(byte[] buffer, int start, int count)
        {
            if (count < 4) return -1;
            if (start < 0) start = 0;

            for (int i = start; i <= (count - 4); i++)
            {
                if (buffer[i] == 13
                    && buffer[i + 1] == 10
                    && buffer[i + 2] == 13
                    && buffer[i + 3] == 10)
                {
                    return i;
                }
            }

            return -1;
        }

        #endregion
    }
}
