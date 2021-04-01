using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WatsonTcp
{
    internal static class WatsonCommon
    {
        internal static byte[] ReadStreamFully(Stream input)
        { 
            byte[] buffer = new byte[65536];
            using (MemoryStream ms = new MemoryStream())
            {
                int read = 0;
                while (true)
                {
                    read = input.Read(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        ms.Write(buffer, 0, read);
                    }
                    else
                    {
                        break;
                    }
                }

                return ms.ToArray();
            }
        }

        internal static byte[] ReadFromStream(Stream stream, long count, int bufferLen)
        {
            if (count <= 0) return new byte[0];
            if (bufferLen <= 0) throw new ArgumentException("Buffer must be greater than zero bytes."); 
            byte[] buffer = new byte[bufferLen];

            int read = 0;
            long bytesRemaining = count;
            MemoryStream ms = new MemoryStream();

            while (bytesRemaining > 0)
            {
                if (bufferLen > bytesRemaining) buffer = new byte[bytesRemaining];

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

        internal static MemoryStream DataStreamToMemoryStream(long contentLength, Stream stream, int bufferLen)
        {
            if (contentLength <= 0) return new MemoryStream(new byte[0]);
            if (bufferLen <= 0) throw new ArgumentException("Buffer must be greater than zero bytes.");
            byte[] buffer = new byte[bufferLen];

            int read = 0;
            long bytesRemaining = contentLength;
            MemoryStream ms = new MemoryStream();

            while (bytesRemaining > 0)
            {
                if (bufferLen > bytesRemaining) buffer = new byte[bytesRemaining];

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

            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }

        internal static async Task<byte[]> ReadFromStreamAsync(Stream stream, long count, int bufferLen)
        {
            if (count <= 0) return null;
            if (bufferLen <= 0) throw new ArgumentException("Buffer must be greater than zero bytes.");
            byte[] buffer = new byte[bufferLen];

            int read = 0;
            long bytesRemaining = count; 

            using (MemoryStream ms = new MemoryStream())
            {
                while (bytesRemaining > 0)
                {
                    if (bufferLen > bytesRemaining) buffer = new byte[bytesRemaining];

                    read = await stream.ReadAsync(buffer, 0, buffer.Length);
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

                return ms.ToArray();
            }
        }

        internal static async Task<byte[]> ReadMessageDataAsync(WatsonMessage msg, int bufferLen)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (msg.ContentLength == 0) return new byte[0]; 
            return await WatsonCommon.ReadFromStreamAsync(msg.DataStream, msg.ContentLength, bufferLen); 
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
         
        internal static void BytesToStream(byte[] data, out long contentLength, out Stream stream)
        {
            contentLength = 0;
            stream = new MemoryStream(new byte[0]);

            if (data != null && data.Length > 0)
            {
                contentLength = data.Length;
                stream = new MemoryStream();
                stream.Write(data, 0, data.Length);
                stream.Seek(0, SeekOrigin.Begin);
            }
        }

        internal static DateTime GetExpirationTimestamp(WatsonMessage msg)
        {
            DateTime expiration = msg.Expiration.Value;

            if (msg.SenderTimestamp != null)
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
                TimeSpan ts = DateTime.Now - msg.SenderTimestamp.Value;
                expiration = expiration.AddMilliseconds(ts.TotalMilliseconds);
            }

            return expiration;
        }
    }
}
