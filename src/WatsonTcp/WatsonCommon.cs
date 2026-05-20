namespace WatsonTcp
{
    using System;
    using System.Buffers;
    using System.IO;
    using System.Net.Security;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class WatsonCommon
    {
        private const int LargeTransferThresholdBytes = 1024 * 1024;
        private const int LargeTransferWriteBufferBytes = 1024 * 1024;

        internal static async Task<MemoryStream> DataStreamToMemoryStream(long contentLength, Stream stream, int bufferLen, CancellationToken token)
        {
            if (contentLength <= 0) return new MemoryStream(Array.Empty<byte>());
            if (bufferLen <= 0) throw new ArgumentException("Buffer must be greater than zero bytes.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            int initialCapacity = GetBufferCapacity(contentLength);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Min(bufferLen, initialCapacity));
            long bytesRemaining = contentLength;
            MemoryStream ms = new MemoryStream(initialCapacity);

            try
            {
                while (bytesRemaining > 0)
                {
                    int toRead = (int)Math.Min((long)buffer.Length, bytesRemaining);
                    int read = await stream.ReadAsync(buffer, 0, toRead, token).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        throw new IOException("Could not read from supplied stream.");
                    }

                    await ms.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                    bytesRemaining -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }

        internal static async Task<byte[]> ReadFromStreamAsync(Stream stream, long count, int bufferLen, CancellationToken token)
        {
            if (count <= 0) return null;
            if (bufferLen <= 0) throw new ArgumentException("Buffer must be greater than zero bytes.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(count), "Count must fit within a byte array.");

            byte[] data = new byte[(int)count];
            int offset = 0;
            long bytesRemaining = count;

            try
            {
                while (bytesRemaining > 0)
                {
                    int toRead = (int)Math.Min((long)bufferLen, bytesRemaining);
                    int read = await stream.ReadAsync(data, offset, toRead, token).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        throw new IOException("Could not read from supplied stream.");
                    }

                    offset += read;
                    bytesRemaining -= read;
                }
            }
            catch (Exception)
            {
                // exception may be thrown if a client disconnects
                // immediately after sending a message
                if (offset <= 0) return Array.Empty<byte>();
                if (offset < data.Length)
                {
                    byte[] partial = new byte[offset];
                    Buffer.BlockCopy(data, 0, partial, 0, offset);
                    return partial;
                }
            }

            return data;
        }

        internal static async Task<byte[]> ReadMessageDataAsync(WatsonMessage msg, int bufferLen, CancellationToken token)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (msg.ContentLength == 0) return Array.Empty<byte>();

            byte[] msgData = null;

            try
            {
                msgData = await ReadFromStreamAsync(msg.DataStream, msg.ContentLength, bufferLen, token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // exception may be thrown if a client disconnects
                // immediately after sending a message
            }

            return msgData;
        }
         
        internal static byte[] AppendBytes(byte[] head, byte[] tail)
        {
            byte[] arrayCombined = new byte[head.Length + tail.Length];
            Array.Copy(head, 0, arrayCombined, 0, head.Length);
            Array.Copy(tail, 0, arrayCombined, head.Length, tail.Length);
            return arrayCombined;
        }

        internal static string ByteArrayToHex(byte[] data)
        {
            StringBuilder hex = new StringBuilder(data.Length * 2);
            foreach (byte b in data) hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }
         
        internal static void BytesToStream(byte[] data, int start, out int contentLength, out Stream stream)
        {
            contentLength = 0;
            stream = new MemoryStream(Array.Empty<byte>(), false);

            if (data == null) return;
            if (start < 0 || start > data.Length) throw new ArgumentOutOfRangeException(nameof(start));

            if (data.Length > start)
            {
                contentLength = data.Length - start;
                stream = new MemoryStream(data, start, contentLength, false, true);
            }
        }

        internal static bool TryGetStreamBuffer(Stream stream, long contentLength, out ArraySegment<byte> segment)
        {
            segment = default(ArraySegment<byte>);
            if (stream == null) return false;
            if (contentLength < 0 || contentLength > int.MaxValue) return false;

            if (stream is MemoryStream ms && ms.TryGetBuffer(out ArraySegment<byte> buffer))
            {
                long remaining = ms.Length - ms.Position;
                if (remaining < contentLength) return false;

                int count = (int)contentLength;
                int offset = buffer.Offset + (int)ms.Position;
                segment = new ArraySegment<byte>(buffer.Array, offset, count);
                return true;
            }

            return false;
        }

        internal static byte[] SerializeJsonBytes(ISerializationHelper serializationHelper, object obj, bool pretty = true)
        {
            if (obj == null) return Array.Empty<byte>();

            if (serializationHelper is DefaultSerializationHelper defaultHelper)
            {
                return defaultHelper.SerializeJsonBytes(obj, pretty);
            }

            string json = serializationHelper.SerializeJson(obj, pretty);
            if (String.IsNullOrEmpty(json)) return Array.Empty<byte>();
            return Encoding.UTF8.GetBytes(json);
        }

        internal static async Task WriteMessageAsync(Stream destination, byte[] headerBytes, long contentLength, Stream stream, int bufferSize, CancellationToken token)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (headerBytes == null) throw new ArgumentNullException(nameof(headerBytes));
            if (bufferSize < 1) throw new ArgumentOutOfRangeException(nameof(bufferSize));

            int effectiveBufferSize = GetEffectiveWriteBufferSize(destination, contentLength, bufferSize);

            if (contentLength <= 0)
            {
                await destination.WriteAsync(headerBytes, 0, headerBytes.Length, token).ConfigureAwait(false);
                return;
            }

            if (TryGetStreamBuffer(stream, contentLength, out ArraySegment<byte> payloadSegment)
                && (headerBytes.Length + payloadSegment.Count) <= effectiveBufferSize)
            {
                byte[] combined = ArrayPool<byte>.Shared.Rent(headerBytes.Length + payloadSegment.Count);

                try
                {
                    Buffer.BlockCopy(headerBytes, 0, combined, 0, headerBytes.Length);
                    Buffer.BlockCopy(payloadSegment.Array, payloadSegment.Offset, combined, headerBytes.Length, payloadSegment.Count);
                    await destination.WriteAsync(combined, 0, headerBytes.Length + payloadSegment.Count, token).ConfigureAwait(false);

                    if (stream.CanSeek)
                    {
                        stream.Seek(contentLength, SeekOrigin.Current);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(combined);
                }

                return;
            }

            if (TryGetStreamBuffer(stream, contentLength, out payloadSegment))
            {
                int firstPayloadBytes = Math.Min(payloadSegment.Count, Math.Max(0, effectiveBufferSize - headerBytes.Length));

                if (firstPayloadBytes > 0)
                {
                    byte[] combined = ArrayPool<byte>.Shared.Rent(headerBytes.Length + firstPayloadBytes);

                    try
                    {
                        Buffer.BlockCopy(headerBytes, 0, combined, 0, headerBytes.Length);
                        Buffer.BlockCopy(payloadSegment.Array, payloadSegment.Offset, combined, headerBytes.Length, firstPayloadBytes);
                        await destination.WriteAsync(combined, 0, headerBytes.Length + firstPayloadBytes, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(combined);
                    }

                    if (firstPayloadBytes == payloadSegment.Count)
                    {
                        if (stream.CanSeek)
                        {
                            stream.Seek(contentLength, SeekOrigin.Current);
                        }

                        return;
                    }

                    payloadSegment = new ArraySegment<byte>(
                        payloadSegment.Array,
                        payloadSegment.Offset + firstPayloadBytes,
                        payloadSegment.Count - firstPayloadBytes);
                }
                else
                {
                    await destination.WriteAsync(headerBytes, 0, headerBytes.Length, token).ConfigureAwait(false);
                }

                await WriteArraySegmentAsync(destination, payloadSegment, effectiveBufferSize, token).ConfigureAwait(false);

                if (stream.CanSeek)
                {
                    stream.Seek(contentLength, SeekOrigin.Current);
                }

                return;
            }

            await destination.WriteAsync(headerBytes, 0, headerBytes.Length, token).ConfigureAwait(false);

            byte[] buffer = ArrayPool<byte>.Shared.Rent(effectiveBufferSize);

            try
            {
                long bytesRemaining = contentLength;
                while (bytesRemaining > 0)
                {
                    int toRead = (int)Math.Min((long)buffer.Length, bytesRemaining);
                    int bytesRead = await stream.ReadAsync(buffer, 0, toRead, token).ConfigureAwait(false);
                    if (bytesRead <= 0)
                    {
                        throw new IOException("Could not read from supplied stream.");
                    }

                    await destination.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                    bytesRemaining -= bytesRead;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        internal static DateTime GetExpirationTimestamp(WatsonMessage msg)
        {
            //
            // TimeSpan will be negative if sender timestamp is earlier than now or positive if sender timestamp is later than now
            // Goal #1: if sender has a later timestamp, decrease expiration by the difference between sender time and our time
            // Goal #2: if sender has an earlier timestamp, increase expiration by the difference between sender time and our time
            // 
            // E.g. If sender time is 10:40 and receiver time is 10:45 and expiration is 1 minute, so 10:41.
            // ts = 10:45 - 10:40 = 5 minutes
            // expiration = 10:41 + 5 = 10:46 which is 1 minute later than when receiver received the message
            //
            TimeSpan ts = DateTime.UtcNow - msg.TimestampUtc;
            return msg.ExpirationUtc.Value.AddMilliseconds(ts.TotalMilliseconds);
        }

        private static int GetBufferCapacity(long contentLength)
        {
            if (contentLength < 0) throw new ArgumentOutOfRangeException(nameof(contentLength));
            if (contentLength > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(contentLength), "Content length must fit within a memory buffer.");
            return (int)contentLength;
        }

        private static int GetEffectiveWriteBufferSize(Stream destination, long contentLength, int bufferSize)
        {
            if (contentLength < LargeTransferThresholdBytes)
            {
                return bufferSize;
            }

            if ((destination is SslStream) && contentLength >= LargeTransferThresholdBytes)
            {
                return Math.Max(bufferSize, LargeTransferWriteBufferBytes);
            }

            return bufferSize;
        }

        private static async Task WriteArraySegmentAsync(Stream destination, ArraySegment<byte> payloadSegment, int writeSize, CancellationToken token)
        {
            int offset = payloadSegment.Offset;
            int remaining = payloadSegment.Count;

            while (remaining > 0)
            {
                int toWrite = Math.Min(writeSize, remaining);
                await destination.WriteAsync(payloadSegment.Array, offset, toWrite, token).ConfigureAwait(false);
                offset += toWrite;
                remaining -= toWrite;
            }
        }
    }
}
