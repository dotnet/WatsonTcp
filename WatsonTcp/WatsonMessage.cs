using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
        /// Message data.
        /// </summary>
        [JsonIgnore]
        public byte[] Data { get; set; }

        /// <summary>
        /// Stream containing the message data.
        /// </summary>
        [JsonIgnore]
        public Stream DataStream { get; set; }

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
         
        #endregion Internal-Members

        #region Private-Members

        private Action<string> _Logger = null;
        private string _Header = "[WatsonMessage] ";
        //                                         1         2         3
        //                                12345678901234567890123456789012
        private string _DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffzzz"; // 32 bytes

        private int _ReadStreamBuffer = 65536; 
        private byte[] _PresharedKey; 
        private Dictionary<object, object> _Metadata = new Dictionary<object, object>();  

        #endregion Private-Members

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
        /// <param name="data">The data to send.</param>
        /// <param name="syncRequest">Indicate if the message is a synchronous message request.</param>
        /// <param name="syncResponse">Indicate if the message is a synchronous message response.</param>
        /// <param name="expiration">The time at which the message should expire (only valid for synchronous message requests).</param>
        /// <param name="convGuid">Conversation GUID.</param>
        /// <param name="logger">Logger method.</param>
        internal WatsonMessage(Dictionary<object, object> metadata, byte[] data, bool syncRequest, bool syncResponse, DateTime? expiration, string convGuid, Action<string> logger)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
             
            Status = MessageStatus.Normal; 
            ContentLength = data.Length;
            Metadata = metadata;
            SyncRequest = syncRequest;
            SyncResponse = syncResponse;
            Expiration = expiration;
            ConversationGuid = convGuid;
            Data = new byte[data.Length];
            Buffer.BlockCopy(data, 0, Data, 0, data.Length);
            DataStream = null;

            _Logger = logger; 
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
        /// <param name="convGuid">Conversation GUID.</param>
        /// <param name="logger">Logger method.</param>
        internal WatsonMessage(Dictionary<object, object> metadata, long contentLength, Stream stream, bool syncRequest, bool syncResponse, DateTime? expiration, string convGuid, Action<string> logger)
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
            Data = null;
            DataStream = stream;

            _Logger = logger; 
        }

        /// <summary>
        /// Read from a stream and construct a message.  Call Build() to populate.
        /// </summary>
        /// <param name="stream">NetworkStream.</param>
        /// <param name="logger">Logger method.</param>
        internal WatsonMessage(NetworkStream stream, Action<string> logger)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Cannot read from stream.");
             
            Status = MessageStatus.Normal; 
            DataStream = stream;

            _Logger = logger; 
        }

        /// <summary>
        /// Read from an SSL-based stream and construct a message.  Call Build() to populate.
        /// </summary>
        /// <param name="stream">SslStream.</param>
        /// <param name="logger">Logger method.</param>
        internal WatsonMessage(SslStream stream, Action<string> logger)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Cannot read from stream.");

            Status = MessageStatus.Normal;
            DataStream = stream;

            _Logger = logger; 
        }

        #endregion Constructors-and-Factories

        #region Internal-Methods

        /// <summary>
        /// Build the Message object from data that awaits in a NetworkStream or SslStream.
        /// Returns the payload data in the 'Data' field.
        /// </summary>
        /// <returns>True if successful.</returns>
        internal async Task<bool> Build()
        {
            try
            {
                #region Read-Headers

                bool success = await ReadHeaders();
                if (!success)
                { 
                    _Logger?.Invoke(_Header + "BuildStream timeout reading headers");
                    return false;
                }

                #endregion

                #region Read-Payload

                Data = await ReadFromNetwork(ContentLength, "Payload");

                #endregion Read-Payload

                return true;
            } 
            catch (IOException ioe)
            {
                _Logger?.Invoke(_Header + "Build IOexception, disconnect assumed: " + ioe.Message);
                return false;
            }
            catch (SocketException se)
            {
                _Logger?.Invoke(_Header + "Build SocketException, disconnect assumed: " + se.Message);
                return false;
            }
            catch (Exception e)
            {
                _Logger?.Invoke(_Header + "Build exception: " + 
                    Environment.NewLine +
                    e.ToString() +
                    Environment.NewLine); 
                return false;
            } 
        }

        /// <summary>
        /// Build the Message object from data that awaits in a NetworkStream or SslStream.
        /// </summary>
        /// <returns>True if successful.</returns>
        internal async Task<bool> BuildStream()
        {
            try
            {
                #region Read-Headers

                bool success = await ReadHeaders();
                if (!success)
                {
                    _Logger?.Invoke(_Header + "BuildStream timeout reading headers");
                    return false;
                }

                #endregion
                 
                return true;
            }
            catch (IOException ioe)
            {
                _Logger?.Invoke(_Header + "BuildStream IOexception, disconnect assumed: " + ioe.Message);
                return false;
            }
            catch (SocketException se)
            {
                _Logger?.Invoke(_Header + "BuildStream SocketException, disconnect assumed: " + se.Message);
                return false;
            }
            catch (Exception e)
            {
                _Logger?.Invoke(_Header + "BuildStream exception: " +
                    Environment.NewLine +
                    e.ToString() +
                    Environment.NewLine);
                return false;
            }
        }

        /// <summary>
        /// Creates a byte array useful for transmission, without packaging the data.
        /// </summary>
        /// <returns>Byte array.</returns>
        internal byte[] ToHeaderBytes(long contentLength)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            ContentLength = contentLength;

            string jsonStr = SerializationHelper.SerializeJson(this, false);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonStr);
            byte[] end = AppendBytes(Encoding.UTF8.GetBytes(Environment.NewLine), Encoding.UTF8.GetBytes(Environment.NewLine));
            byte[] final = AppendBytes(jsonBytes, end);
            return final; 
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
        
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task<bool> ReadHeaders()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        { 
            byte[] buffer = new byte[0];
            byte[] end = AppendBytes(Encoding.UTF8.GetBytes(Environment.NewLine), Encoding.UTF8.GetBytes(Environment.NewLine));

            while (true)
            {
                byte[] data = await ReadFromNetwork(1, "Headers");
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

            WatsonMessage deserialized = SerializationHelper.DeserializeJson<WatsonMessage>(Encoding.UTF8.GetString(buffer));
            ContentLength = deserialized.ContentLength;
            PresharedKey = deserialized.PresharedKey;
            Status = deserialized.Status;
            Metadata = deserialized.Metadata;
            SyncRequest = deserialized.SyncRequest;
            SyncResponse = deserialized.SyncResponse;
            Expiration = deserialized.Expiration;
            ConversationGuid = deserialized.ConversationGuid;

            _Logger?.Invoke(_Header + "ReadHeaders header processing complete" + Environment.NewLine + Encoding.UTF8.GetString(buffer)); 
            return true; 
        }

        private async Task<byte[]> ReadFromNetwork(long count, string field)
        {  
            if (count <= 0) return null;
            int bytesRead = 0;
            byte[] readBuffer = new byte[count];
                 
            if (DataStream != null)
            { 
                while (bytesRead < count)
                {
                    int read = await DataStream.ReadAsync(readBuffer, bytesRead, readBuffer.Length - bytesRead);
                    if (read == 0) throw new SocketException();
                    bytesRead += read;
                }
            }
            else
            {
                throw new IOException("No suitable input stream found.");
            }

            // _Logger?.Invoke(_Header + "ReadFromNetwork " + count + " bytes, field " + field + ": " + ByteArrayToHex(readBuffer)); 
            return readBuffer; 
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
         
        #endregion Private-Methods
    }
}