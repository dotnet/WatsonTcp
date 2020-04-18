using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WatsonTcp
{
    internal class WatsonMessage
    {
        #region Public-Members

        /// <summary>
        /// Length of the data.
        /// </summary>
        public long ContentLength { get; set; }
         
        /// <summary>
        /// Preshared key for connection authentication.  
        /// _HeaderFields[0], 16 bytes.
        /// </summary>
        public byte[] PresharedKey
        {
            get
            {
                return _PresharedKey;
            }
            set
            {
                if (value == null)
                {
                    _PresharedKey = null; 
                }
                else
                {
                    if (value.Length != 16) throw new ArgumentException("PresharedKey must be 16 bytes.");

                    _PresharedKey = new byte[16];
                    Buffer.BlockCopy(value, 0, _PresharedKey, 0, 16); 
                }
            }
        }

        /// <summary>
        /// Status of the message.  
        /// _HeaderFields[1], 4 bytes (Int32).
        /// </summary>
        public MessageStatus Status = MessageStatus.Normal;
          
        /// <summary>
        /// Metadata dictionary.  
        /// _HeaderFields[2], 8 bytes (Int64).
        /// </summary>
        public Dictionary<object, object> Metadata
        {
            get
            {
                return _Metadata;
            }
            set
            {
                if (value == null || value.Count < 1)
                {
                    _Metadata = new Dictionary<object, object>(); 
                }
                else
                {
                    _Metadata = value; 
                }
            }
        }

        /// <summary>
        /// Indicates if the message is a synchronous request.
        /// _HeaderFields[3], 4 bytes (Int32).
        /// </summary>
        public bool SyncRequest = false;

        /// <summary>
        /// Indicates if the message is a synchronous response.
        /// _HeaderFields[4], 4 bytes (Int32).
        /// </summary>
        public bool SyncResponse = false;

        /// <summary>
        /// Indicates an expiration time in UTC; only applicable to synchronous requests.
        /// _HeaderFields[5], 32 bytes (DateTime as string).
        /// </summary>
        public DateTime? Expiration = null;

        /// <summary>
        /// Indicates the conversation GUID of the message.
        /// _HeaderFields[6], 36 bytes (byte[36]).
        /// </summary>
        public string ConversationGuid = null;

        /// <summary>
        /// The type of compression used in the message.
        /// </summary>
        public CompressionType Compression = CompressionType.None;

        /// <summary>
        /// Encryption info used in the message.
        /// </summary>
        public EncryptionInfo Encryption = null;

        /// <summary>
        /// Message data from the stream.  Using 'Data' will fully read 'DataStream'.
        /// </summary>
        [JsonIgnore]
        public byte[] Data
        {
            get
            {
                if (_Data != null)
                { 
                    return _Data;
                }
                 
                if (ContentLength > 0 && _DataStream != null)
                { 
                    _Data = ReadFromStream(_DataStream, ContentLength);

                    if (_DataStream is GZipStream || _DataStream is DeflateStream)
                    {
                        // 
                        // It is necessary to close the compression stream; when it was opened
                        // it was instructed to leave the underlying stream open
                        //
                        _DataStream.Flush();
                        _DataStream.Dispose();
                    }
                }

                return _Data;
            }
        }

        /// <summary>
        /// Stream containing the message data.
        /// </summary>
        [JsonIgnore]
        public Stream DataStream
        {
            get
            {
                return _DataStream;
            }
        }

        /// <summary>
        /// Message headers in byte-array form ready to send.
        /// </summary>
        [JsonIgnore]
        public byte[] HeaderBytes
        {
            get
            {
                string jsonStr = SerializationHelper.SerializeJson(this, false);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonStr);
                byte[] end = AppendBytes(Encoding.UTF8.GetBytes(Environment.NewLine), Encoding.UTF8.GetBytes(Environment.NewLine));
                byte[] final = AppendBytes(jsonBytes, end);
                return final;
            }
        }

        #endregion

        #region Internal-Members

        /// <summary>
        /// Size of buffer to use while reading message payload.  Default is 64KB.
        /// </summary>
        internal int ReadStreamBuffer
        {
            get
            {
                return _ReadStreamBuffer;
            }
            set
            {
                if (value < 1) throw new ArgumentException("ReadStreamBuffer must be greater than zero bytes.");
                _ReadStreamBuffer = value;
            }
        }
         
        #endregion

        #region Private-Members

        private Action<string> _Logger = null;
        private string _Header = "[WatsonMessage] ";
        //                                         1         2         3
        //                                12345678901234567890123456789012
        private string _DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffzzz"; // 32 bytes

        private int _ReadStreamBuffer = 65536; 
        private byte[] _PresharedKey; 
        private Dictionary<object, object> _Metadata = new Dictionary<object, object>();
        private byte[] _Data = null;
        private Stream _DataStream = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Do not use.
        /// </summary>
        internal WatsonMessage()
        { 
            Status = MessageStatus.Normal;
        }
         
        /// <summary>
        /// Construct a new message to send.
        /// </summary>
        /// <param name="metadata">Metadata to attach to the message.</param>
        /// <param name="contentLength">The number of bytes included in the stream.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <param name="syncRequest">Indicate if the message is a synchronous message request.</param>
        /// <param name="syncResponse">Indicate if the message is a synchronous message response.</param>
        /// <param name="expiration">The time at which the message should expire (only valid for synchronous message requests).</param>
        /// <param name="compression">The type of compression to use.</param>
        /// <param name="encryption">The type of encryption to use.</param>
        /// <param name="convGuid">Conversation GUID.</param>
        /// <param name="logger">Logger method.</param>
        internal WatsonMessage(
            Dictionary<object, object> metadata, 
            long contentLength, 
            Stream stream, 
            bool syncRequest, 
            bool syncResponse, 
            DateTime? expiration, 
            string convGuid, 
            CompressionType compression, 
            EncryptionType encryption, 
            Action<string> logger)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (contentLength > 0)
            {
                if (stream == null || !stream.CanRead)
                {
                    throw new ArgumentException("Cannot read from supplied stream.");
                }
            } 

            Status = MessageStatus.Normal; 
            ContentLength = contentLength;
            Metadata = metadata;
            SyncRequest = syncRequest;
            SyncResponse = syncResponse;
            Expiration = expiration;
            ConversationGuid = convGuid;
            Compression = compression;
            Encryption = new EncryptionInfo(encryption);
            
            _DataStream = stream;
            _Logger = logger; 
        }

        /// <summary>
        /// Read from a stream and construct a message.  Call Build() to populate.
        /// </summary>
        /// <param name="stream">Stream.</param>
        /// <param name="logger">Logger method.</param>
        internal WatsonMessage(Stream stream, Action<string> logger)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Cannot read from stream.");
             
            Status = MessageStatus.Normal; 
            
            _DataStream = stream;
            _Logger = logger; 
        }
         
        #endregion
        
        #region Internal-Methods
         
        /// <summary>
        /// Build the Message object from data that awaits in a NetworkStream or SslStream.
        /// </summary>
        /// <returns>True if successful.</returns>
        internal async Task<bool> BuildFromStream()
        {
            try
            {
                #region Read-Headers

                byte[] buffer = new byte[0];
                byte[] end = AppendBytes(Encoding.UTF8.GetBytes(Environment.NewLine), Encoding.UTF8.GetBytes(Environment.NewLine));

                while (true)
                {
                    byte[] data = await ReadFromStreamAsync(_DataStream, 1);
                    if (data != null && data.Length == 1)
                    {
                        buffer = AppendBytes(buffer, data);
                        if (buffer.Length >= 4)
                        {
                            byte[] endCheck = buffer.Skip(buffer.Length - 4).Take(4).ToArray();
                            if (endCheck.SequenceEqual(end))
                            {
                                _Logger?.Invoke(_Header + "ReadHeaders found header demarcation");
                                break;
                            }
                        }
                    }
                }

                WatsonMessage msg = SerializationHelper.DeserializeJson<WatsonMessage>(Encoding.UTF8.GetString(buffer));
                ContentLength = msg.ContentLength;
                PresharedKey = msg.PresharedKey;
                Status = msg.Status;
                Metadata = msg.Metadata;
                SyncRequest = msg.SyncRequest;
                SyncResponse = msg.SyncResponse;
                Expiration = msg.Expiration;
                ConversationGuid = msg.ConversationGuid;
                Compression = msg.Compression;
                Encryption = msg.Encryption;

                _Logger?.Invoke(_Header + "BuildFromStream header processing complete" + Environment.NewLine + Encoding.UTF8.GetString(buffer).Trim()); 

                #endregion

                #region Setup-Stream

                if (Compression == CompressionType.None)
                { 
                    // do nothing
                }
                else
                {  
                    if (Compression == CompressionType.Deflate)
                    {
                        _DataStream = new DeflateStream(_DataStream, CompressionMode.Decompress, true);
                    }
                    else if (Compression == CompressionType.Gzip)
                    {
                        _DataStream = new GZipStream(_DataStream, CompressionMode.Decompress, true);
                    }
                    else
                    {
                        throw new InvalidOperationException("Unknown compression type: " + Compression.ToString());
                    } 
                }

                #endregion

                return true;
            }
            catch (IOException)
            {
                _Logger?.Invoke(_Header + "BuildStream IOexception, disconnect assumed");
                return false;
            }
            catch (SocketException)
            {
                _Logger?.Invoke(_Header + "BuildStream SocketException, disconnect assumed");
                return false;
            }
            catch (ObjectDisposedException)
            {
                _Logger?.Invoke(_Header + "BuildStream ObjectDisposedException, disconnect assumed");
                return false;
            }
            catch (Exception e)
            {
                _Logger?.Invoke(_Header + "BuildStream exception: " +
                    Environment.NewLine +
                    SerializationHelper.SerializeJson(e, true) +
                    Environment.NewLine);
                return false;
            }
        }
         
        /// <summary>
        /// Human-readable string version of the object.
        /// </summary>
        /// <returns>String.</returns>
        public override string ToString()
        {
            string ret = "---" + Environment.NewLine; 
            ret += "  Preshared key     : " + (PresharedKey != null ? ByteArrayToHex(PresharedKey) : "null") + Environment.NewLine;
            ret += "  Status            : " + Status.ToString() + Environment.NewLine;
            ret += "  SyncRequest       : " + SyncRequest.ToString() + Environment.NewLine;
            ret += "  SyncResponse      : " + SyncResponse.ToString() + Environment.NewLine;
            ret += "  ExpirationUtc     : " + (Expiration != null ? Expiration.Value.ToString(_DateTimeFormat) : "null") + Environment.NewLine;
            ret += "  Conversation GUID : " + ConversationGuid + Environment.NewLine;

            if (Metadata != null)
            {
                ret += "  Metadata          : " + Metadata.Count + " entries" + Environment.NewLine;
            }

            if (Data != null)
            {
                ret += "  Data              : " + Data.Length + " bytes" + Environment.NewLine;
                if (Data.Length > 0) 
                    ret += Encoding.UTF8.GetString(Data); 
            }

            if (DataStream != null)
                ret += "  DataStream        : present, " + ContentLength + " bytes" + Environment.NewLine;

            return ret;
        }

        #endregion Public-Methods

        #region Private-Methods

        private byte[] ReadStreamFully(Stream input)
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
                        if (read < buffer.Length) break;
                    }
                    else
                    {
                        break;
                    }
                }
                 
                return ms.ToArray();
            }
        }

        private byte[] ReadFromStream(Stream stream, long count)
        {
            if (count <= 0) return null;
            byte[] buffer = new byte[_ReadStreamBuffer];

            int read = 0;
            long bytesRemaining = count;
            MemoryStream ms = new MemoryStream();

            while (bytesRemaining > 0)
            {
                if (_ReadStreamBuffer > bytesRemaining) buffer = new byte[bytesRemaining];

                read = stream.Read(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    ms.Write(buffer, 0, read); 
                    bytesRemaining -= read;
                }
                else
                {
                    throw new SocketException();
                }
            }

            byte[] data = ms.ToArray(); 
            return data;
        }

        private async Task<byte[]> ReadFromStreamAsync(Stream stream, long count)
        {
            if (count <= 0) return null;
            byte[] buffer = new byte[_ReadStreamBuffer];

            int read = 0;
            long bytesRemaining = count;
            MemoryStream ms = new MemoryStream();

            while (bytesRemaining > 0)
            {
                if (_ReadStreamBuffer > bytesRemaining) buffer = new byte[bytesRemaining];

                read = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    ms.Write(buffer, 0, read);
                    bytesRemaining -= read;
                }
                else
                {
                    throw new SocketException();
                }
            }

            byte[] data = ms.ToArray();
            return data;
        }

        private byte[] AppendBytes(byte[] head, byte[] tail)
        {
            byte[] arrayCombined = new byte[head.Length + tail.Length];
            Array.Copy(head, 0, arrayCombined, 0, head.Length);
            Array.Copy(tail, 0, arrayCombined, head.Length, tail.Length);
            return arrayCombined;
        }

        private string ByteArrayToHex(byte[] data)
        {
            StringBuilder hex = new StringBuilder(data.Length * 2);
            foreach (byte b in data) hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        #endregion
    }
}