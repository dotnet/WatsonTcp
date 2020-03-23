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

namespace WatsonTcp.Message
{
    internal class WatsonMessage
    {
        #region Public-Members

        /// <summary>
        /// Length of all header fields and payload data.
        /// </summary>
        internal long Length { get; set; }

        /// <summary>
        /// Length of the data.
        /// </summary>
        internal long ContentLength { get; set; }
         
        /// <summary>
        /// Preshared key for connection authentication.  
        /// _HeaderFields[0], 16 bytes.
        /// </summary>
        internal byte[] PresharedKey
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
                    _HeaderFields[0] = false;
                }
                else
                {
                    if (value.Length != 16) throw new ArgumentException("PresharedKey must be 16 bytes.");

                    _PresharedKey = new byte[16];
                    Buffer.BlockCopy(value, 0, _PresharedKey, 0, 16);
                    _HeaderFields[0] = true;
                }
            }
        }

        /// <summary>
        /// Status of the message.  
        /// _HeaderFields[1], 4 bytes (Int32).
        /// </summary>
        internal MessageStatus Status
        {
            get
            {
                return _Status;
            }
            set
            {
                _Status = value;
                _HeaderFields[1] = true;
            }
        }
         
        /// <summary>
        /// Bytes associated with metadata.  
        /// _HeaderFields[2], 8 bytes (Int64).
        /// </summary>
        internal byte[] MetadataBytes
        {
            get
            {
                return _MetadataBytes;
            }
        }

        /// <summary>
        /// Metadata dictionary.  
        /// _HeaderFields[2], 8 bytes (Int64).
        /// </summary>
        internal Dictionary<object, object> Metadata
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
                    _MetadataBytes = null;
                    _MetadataLength = 0;
                    _HeaderFields[2] = false;
                }
                else
                {
                    _Metadata = value;
                    _MetadataBytes = Encoding.UTF8.GetBytes(SerializationHelper.SerializeJson(value, false));
                    _MetadataLength = _MetadataBytes.Length;
                    _HeaderFields[2] = true;
                }
            }
        }

        /// <summary>
        /// Indicates if the message is a synchronous request.
        /// _HeaderFields[3], 4 bytes (Int32).
        /// </summary>
        internal bool SyncRequest
        {
            get
            {
                return _SyncRequest;
            }
            set
            {
                _SyncRequest = value;
                if (value)
                {
                    _HeaderFields[3] = true;
                }
                else
                {
                    _HeaderFields[3] = false;
                }
            }
        }

        /// <summary>
        /// Indicates if the message is a synchronous response.
        /// _HeaderFields[4], 4 bytes (Int32).
        /// </summary>
        internal bool SyncResponse
        {
            get
            {
                return _SyncResponse;
            }
            set
            {
                _SyncResponse = value;
                if (value)
                {
                    _HeaderFields[4] = true;
                }
                else
                {
                    _HeaderFields[4] = false;
                }
            }
        }

        /// <summary>
        /// Indicates an expiration time in UTC; only applicable to synchronous requests.
        /// _HeaderFields[5], 32 bytes (DateTime as string).
        /// </summary>
        internal DateTime? ExpirationUtc
        {
            get
            {
                return _ExpirationUtc;
            }
            set
            {
                if (value != null)
                {
                    _HeaderFields[5] = true;
                }
                else
                {
                    _HeaderFields[5] = false;
                }

                _ExpirationUtc = value;
            }
        }

        /// <summary>
        /// Indicates the conversation GUID of the message.
        /// _HeaderFields[6], 36 bytes (byte[36]).
        /// </summary>
        internal string ConversationGuid
        {
            get
            {
                return _ConversationGuid;
            }
            set
            {
                if (!String.IsNullOrEmpty(value))
                {
                    _HeaderFields[6] = true;
                }
                else
                {
                    _HeaderFields[6] = false;
                }

                _ConversationGuid = value;
            }
        }

        /// <summary>
        /// Message data.
        /// </summary>
        internal byte[] Data { get; set; }

        /// <summary>
        /// Stream containing the message data.
        /// </summary>
        internal Stream DataStream { get; set; }

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

        #endregion Public-Members

        #region Private-Members

        private Action<string> _Logger = null;
        private string _Header = "[WatsonMessage] ";
        //                                         1         2         3
        //                                123456789012345678901234567890
        private string _DateTimeFormat = "yyyy-MM-dd HH:mm:ss.ffffffZ"; // 32 bytes

        private int _ReadStreamBuffer = 65536;
        private byte[] _PresharedKey;

        private BitArray _HeaderFields = new BitArray(64);
        private MessageStatus _Status;
        private Dictionary<object, object> _Metadata = new Dictionary<object, object>();
        private byte[] _MetadataBytes = null;
        private long _MetadataLength = 0;

        private bool _SyncRequest = false;
        private bool _SyncResponse = false;
        private DateTime? _ExpirationUtc = null;
        private string _ConversationGuid = null;

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
        /// <param name="expirationUtc">The time at which the message should expire (only valid for synchronous message requests).</param>
        /// <param name="convGuid">Conversation GUID.</param>
        /// <param name="logger">Logger method.</param>
        internal WatsonMessage(Dictionary<object, object> metadata, byte[] data, bool syncRequest, bool syncResponse, DateTime? expirationUtc, string convGuid, Action<string> logger)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
             
            Status = MessageStatus.Normal; 
            ContentLength = data.Length;
            Metadata = metadata;
            SyncRequest = syncRequest;
            SyncResponse = syncResponse;
            ExpirationUtc = expirationUtc;
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
        /// <param name="expirationUtc">The time at which the message should expire (only valid for synchronous message requests).</param>
        /// <param name="convGuid">Conversation GUID.</param>
        /// <param name="logger">Logger method.</param>
        internal WatsonMessage(Dictionary<object, object> metadata, long contentLength, Stream stream, bool syncRequest, bool syncResponse, DateTime? expirationUtc, string convGuid, Action<string> logger)
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
            ExpirationUtc = expirationUtc;
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

        #region Public-Methods

        /// <summary>
        /// Awaitable async method to build the Message object from data that awaits in a NetworkStream or SslStream, returning the full message data.
        /// </summary>
        /// <returns>Always returns true (void cannot be a return parameter).</returns>
        internal async Task<bool> Build()
        {
            try
            {
                #region Read-Message-Length

                using (MemoryStream msgLengthMs = new MemoryStream())
                {
                    while (true)
                    {
                        byte[] data = await ReadFromNetwork(1, "MessageLength");
                        await msgLengthMs.WriteAsync(data, 0, 1);
                        if (data[0] == 58) break;
                    }

                    byte[] msgLengthBytes = msgLengthMs.ToArray();
                    if (msgLengthBytes == null || msgLengthBytes.Length < 1) return false;
                    string msgLengthString = Encoding.UTF8.GetString(msgLengthBytes).Replace(":", "");
                    long length;
                    Int64.TryParse(msgLengthString, out length);
                    Length = length;

                    _Logger?.Invoke(_Header + "Build message length: " + Length + " bytes");
                }

                #endregion Read-Message-Length

                #region Process-Header-Fields

                byte[] headerFields = await ReadFromNetwork(8, "HeaderFields");
                headerFields = ReverseByteArray(headerFields);
                _HeaderFields = new BitArray(headerFields);

                long payloadLength = Length - 8;

                for (int i = 0; i < _HeaderFields.Length; i++)
                {
                    if (_HeaderFields[i])
                    {
                        MessageField field = GetMessageField(i);
                        object val = await ReadField(field.Type, field.Length, field.Name);
                        SetMessageValue(field, val);
                        payloadLength -= field.Length;
                    }
                }

                if (_MetadataLength > 0)
                {
                    _MetadataBytes = await ReadFromNetwork(_MetadataLength, "MetadataBytes");
                    _Metadata = SerializationHelper.DeserializeJson<Dictionary<object, object>>(Encoding.UTF8.GetString(MetadataBytes));
                }

                ContentLength = payloadLength;
                Data = await ReadFromNetwork(ContentLength, "Payload");

                #endregion Process-Header-Fields

                return true;
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
        /// Awaitable async method to build the Message object from data that awaits in a NetworkStream or SslStream, returning the stream itself.
        /// </summary>
        /// <returns>Always returns true (void cannot be a return parameter).</returns>
        internal async Task<bool> BuildStream()
        {
            try
            {
                #region Read-Message-Length

                using (MemoryStream msgLengthMs = new MemoryStream())
                {
                    while (true)
                    {
                        byte[] data = await ReadFromNetwork(1, "MessageLength");
                        await msgLengthMs.WriteAsync(data, 0, 1);
                        if (data[0] == 58) break;
                    }

                    byte[] msgLengthBytes = msgLengthMs.ToArray();
                    if (msgLengthBytes == null || msgLengthBytes.Length < 1) return false;
                    string msgLengthString = Encoding.UTF8.GetString(msgLengthBytes).Replace(":", "");

                    long length;
                    Int64.TryParse(msgLengthString, out length);
                    Length = length;

                    _Logger?.Invoke(_Header + "BuildStream payload length: " + Length + " bytes");
                }

                #endregion Read-Message-Length

                #region Process-Header-Fields
                 
                byte[] headerFields = await ReadFromNetwork(8, "HeaderFields");
                headerFields = ReverseByteArray(headerFields);
                _HeaderFields = new BitArray(headerFields);

                long payloadLength = Length - 8;

                for (int i = 0; i < _HeaderFields.Length; i++)
                {
                    if (_HeaderFields[i])
                    {
                        MessageField field = GetMessageField(i); 
                        object val = await ReadField(field.Type, field.Length, field.Name);
                        SetMessageValue(field, val);
                        payloadLength -= field.Length;
                    }
                }

                if (_MetadataLength > 0)
                {
                    _MetadataBytes = await ReadFromNetwork(_MetadataLength, "MetadataBytes");
                    _Metadata = SerializationHelper.DeserializeJson<Dictionary<object, object>>(Encoding.UTF8.GetString(MetadataBytes));
                }

                ContentLength = payloadLength;
                Data = null;

                #endregion Process-Header-Fields

                return true;
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
            _HeaderFields[1] = true; // status is always set

            byte[] headerFieldsBytes = new byte[8];
            headerFieldsBytes = BitArrayToBytes(_HeaderFields);
            headerFieldsBytes = ReverseByteArray(headerFieldsBytes);

            byte[] ret = new byte[headerFieldsBytes.Length];
            Buffer.BlockCopy(headerFieldsBytes, 0, ret, 0, headerFieldsBytes.Length);

            #region Header-Fields

            for (int i = 0; i < _HeaderFields.Length; i++)
            {
                if (_HeaderFields[i])
                {
                    MessageField field = GetMessageField(i);
                    switch (i)
                    {
                        case 0: // PresharedKey
                            _Logger?.Invoke(_Header + "ToHeaderBytes PresharedKey: " + Encoding.UTF8.GetString(_PresharedKey)); 
                            ret = AppendBytes(ret, _PresharedKey);
                            break;

                        case 1: // Status
                            _Logger?.Invoke(_Header + "ToHeaderBytes Status: " + _Status.ToString() + " " + (int)Status);
                            ret = AppendBytes(ret, IntegerToBytes((int)_Status));
                            break;

                        case 2: // Metadata
                            _Logger?.Invoke(_Header + "ToHeaderBytes Metadata: [present, " + _MetadataLength + " bytes]");
                            ret = AppendBytes(ret, LongToBytes(_MetadataLength));
                            break;

                        case 3: // SyncRequest
                            _Logger?.Invoke(_Header + "ToHeaderBytes SyncRequest: " + _SyncRequest.ToString());
                            ret = AppendBytes(ret, IntegerToBytes(_SyncRequest ? 1 : 0));
                            break;

                        case 4: // SyncResponse
                            _Logger?.Invoke(_Header + "ToHeaderBytes SyncResponse: " + _SyncResponse.ToString());
                            ret = AppendBytes(ret, IntegerToBytes(_SyncResponse ? 1 : 0));
                            break;

                        case 5: // ExpirationUtc
                            _Logger?.Invoke(_Header + "ToHeaderBytes ExpirationUtc: " + _ExpirationUtc.Value.ToString(_DateTimeFormat));
                            ret = AppendBytes(ret, Encoding.UTF8.GetBytes(_ExpirationUtc.Value.ToString(_DateTimeFormat).PadRight(32)));
                            break;

                        case 6: // ConversationGuid
                            _Logger?.Invoke(_Header + "ToHeaderBytes ConversationGuid: " + _ConversationGuid.ToString());
                            ret = AppendBytes(ret, Encoding.UTF8.GetBytes(_ConversationGuid));
                            break; 

                        default:
                            throw new ArgumentException("Unknown bit number " + i + ".");
                    }
                }
            }

            #endregion Header-Fields

            #region Prepend-Message-Length

            long finalLen = ret.Length + contentLength;
            _Logger?.Invoke(_Header + "ToHeaderBytes length: " + finalLen + " bytes (" + ret.Length + " header bytes + " + contentLength + " data bytes)");

            byte[] lengthHeader = Encoding.UTF8.GetBytes(finalLen.ToString() + ":");
            byte[] final = new byte[(lengthHeader.Length + ret.Length)];
            Buffer.BlockCopy(lengthHeader, 0, final, 0, lengthHeader.Length);
            Buffer.BlockCopy(ret, 0, final, lengthHeader.Length, ret.Length);

            #endregion Prepend-Message-Length
            
            _Logger?.Invoke(_Header + "ToHeaderBytes returning: " + Encoding.UTF8.GetString(final));
            return final;
        }

        /// <summary>
        /// Human-readable string version of the object.
        /// </summary>
        /// <returns>String.</returns>
        public override string ToString()
        {
            string ret = "---" + Environment.NewLine;
            ret += "  Header fields     : " + FieldToString(FieldType.Bits, _HeaderFields) + Environment.NewLine;
            ret += "  Preshared key     : " + FieldToString(FieldType.ByteArray, PresharedKey) + Environment.NewLine;
            ret += "  Status            : " + FieldToString(FieldType.Int32, (int)Status) + Environment.NewLine;
            ret += "  SyncRequest       : " + SyncRequest.ToString() + Environment.NewLine;
            ret += "  SyncResponse      : " + SyncResponse.ToString() + Environment.NewLine;
            ret += "  ExpirationUtc     : " + (ExpirationUtc != null ? ExpirationUtc.Value.ToString(_DateTimeFormat) : "null") + Environment.NewLine;
            ret += "  Conversation GUID : " + ConversationGuid + Environment.NewLine;

            if (Metadata != null)
            {
                ret += "  Metadata          : " + Metadata.Count + " entries, " + _MetadataLength + " bytes" + Environment.NewLine;
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
         
        private async Task<object> ReadField(FieldType fieldType, int maxLength, string name)
        {
            string logMessage = "ReadField " + fieldType.ToString() + " " + maxLength + " bytes, field " + name;

            try
            {
                byte[] data = null; 

                object ret = null;

                if (fieldType == FieldType.Int32)
                {
                    data = await ReadFromNetwork(maxLength, name + " Int32 (" + maxLength + ")");
                    logMessage += " " + ByteArrayToHex(data);
                    ret = Convert.ToInt32(Encoding.UTF8.GetString(data));
                    logMessage += ": " + ret;
                }
                else if (fieldType == FieldType.Int64)
                {
                    data = await ReadFromNetwork(maxLength, name + " Int64 (" + maxLength + ")");
                    logMessage += " " + ByteArrayToHex(data);
                    ret = Convert.ToInt64(Encoding.UTF8.GetString(data));
                    logMessage += ": " + ret;
                }
                else if (fieldType == FieldType.String)
                {
                    data = await ReadFromNetwork(maxLength, name + " String (" + maxLength + ")");
                    logMessage += " " + ByteArrayToHex(data);
                    ret = Encoding.UTF8.GetString(data);
                    logMessage += ": " + ret;
                } 
                else if (fieldType == FieldType.ByteArray)
                {
                    ret = await ReadFromNetwork(maxLength, name + " ByteArray (" + maxLength + ")");
                    logMessage += ": " + ByteArrayToHex((byte[])ret);
                }
                else
                {
                    throw new ArgumentException("Unknown field type: " + fieldType.ToString());
                }

                return ret;
            }
            finally
            {
                _Logger?.Invoke(_Header + logMessage);
            }
        }

        private byte[] FieldToBytes(FieldType fieldType, object data, int maxLength)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            if (fieldType == FieldType.Int32)
            {
                int intVar = Convert.ToInt32(data);
                string lengthVar = "";
                for (int i = 0; i < maxLength; i++) lengthVar += "0";
                return Encoding.UTF8.GetBytes(intVar.ToString(lengthVar));
            }
            else if (fieldType == FieldType.Int64)
            {
                long longVar = Convert.ToInt64(data);
                string lengthVar = "";
                for (int i = 0; i < maxLength; i++) lengthVar += "0";
                return Encoding.UTF8.GetBytes(longVar.ToString(lengthVar));
            }
            else if (fieldType == FieldType.String)
            {
                string dataStr = data.ToString().ToUpper();
                if (dataStr.Length < maxLength)
                {
                    string ret = dataStr.PadRight(maxLength);
                    return Encoding.UTF8.GetBytes(ret);
                }
                else if (dataStr.Length > maxLength)
                {
                    string ret = dataStr.Substring(maxLength);
                    return Encoding.UTF8.GetBytes(ret);
                }
                else
                {
                    return Encoding.UTF8.GetBytes(dataStr);
                }
            } 
            else if (fieldType == FieldType.ByteArray)
            {
                if (((byte[])data).Length != maxLength) throw new ArgumentException("Data length does not match length supplied.");

                byte[] ret = new byte[maxLength];
                InitByteArray(ret);
                Buffer.BlockCopy((byte[])data, 0, ret, 0, maxLength);
                return ret;
            }
            else
            {
                throw new ArgumentException("Unknown field type: " + fieldType.ToString());
            }
        }

        private string FieldToString(FieldType fieldType, object data)
        {
            if (data == null) return null;

            if (fieldType == FieldType.Int32)
            {
                return "[i]" + data.ToString();
            }
            else if (fieldType == FieldType.Int64)
            {
                return "[l]" + data.ToString();
            }
            else if (fieldType == FieldType.String)
            {
                return "[s]" + data.ToString();
            } 
            else if (fieldType == FieldType.ByteArray)
            {
                return "[b]" + ByteArrayToHex((byte[])data);
            }
            else if (fieldType == FieldType.Bits)
            {
                if (data is BitArray)
                {
                    data = BitArrayToBytes((BitArray)data);
                }

                string[] s = ((byte[])data).Select(x => Convert.ToString(x, 2).PadLeft(8, '0')).ToArray();
                string ret = "[b]" + ByteArrayToHex((byte[])data) + ": ";
                foreach (string curr in s)
                {
                    char[] ca = curr.ToCharArray();
                    Array.Reverse(ca);
                    ret += new string(ca) + " ";
                }
                return ret;
            }
            else
            {
                throw new ArgumentException("Unknown field type: " + fieldType.ToString());
            }
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

            _Logger?.Invoke(_Header + "ReadFromNetwork " + count + " bytes, field " + field + ": " + ByteArrayToHex(readBuffer)); 
            return readBuffer; 
        }

        private byte[] IntegerToBytes(int i)
        {
            if (i < 0 || i > 9999) throw new ArgumentException("Value must be between 0 and 9999.");

            byte[] ret = new byte[4];
            InitByteArray(ret);

            string stringVal = i.ToString("0000");

            ret[3] = (byte)(Convert.ToInt32(stringVal[3]));
            ret[2] = (byte)(Convert.ToInt32(stringVal[2]));
            ret[1] = (byte)(Convert.ToInt32(stringVal[1]));
            ret[0] = (byte)(Convert.ToInt32(stringVal[0]));

            return ret;
        }

        private byte[] LongToBytes(long i)
        {
            if (i < 0 || i > 99999999) throw new ArgumentException("Value must be between 0 and 99,999,999.");

            byte[] ret = new byte[8];
            InitByteArray(ret);

            string stringVal = i.ToString("00000000");

            ret[7] = (byte)(Convert.ToInt32(stringVal[7]));
            ret[6] = (byte)(Convert.ToInt32(stringVal[6]));
            ret[5] = (byte)(Convert.ToInt32(stringVal[5]));
            ret[4] = (byte)(Convert.ToInt32(stringVal[4]));
            ret[3] = (byte)(Convert.ToInt32(stringVal[3]));
            ret[2] = (byte)(Convert.ToInt32(stringVal[2]));
            ret[1] = (byte)(Convert.ToInt32(stringVal[1]));
            ret[0] = (byte)(Convert.ToInt32(stringVal[0]));

            return ret;
        }

        private int BytesToInteger(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 1) throw new ArgumentNullException(nameof(bytes));

            // see https://stackoverflow.com/questions/36295952/direct-convertation-between-ascii-byte-and-int?rq=1

            int result = 0;

            for (int i = 0; i < bytes.Length; ++i)
            {
                // ASCII digits are in the range 48 <= n <= 57. This code only
                // makes sense if we are dealing exclusively with digits, so
                // throw if we encounter a non-digit character
                if (bytes[i] < 48 || bytes[i] > 57)
                {
                    throw new ArgumentException("Non-digit character present.");
                }

                // The bytes are in order from most to least significant, so
                // we need to reverse the index to get the right column number
                int exp = bytes.Length - i - 1;

                // Digits in ASCII start with 0 at 48, and move sequentially
                // to 9 at 57, so we can simply subtract 48 from a valid digit
                // to get its numeric value
                int digitValue = bytes[i] - 48;

                // Finally, add the digit value times the column value to the
                // result accumulator
                result += digitValue * (int)Math.Pow(10, exp);
            }

            return result;
        }

        private void InitByteArray(byte[] data)
        {
            if (data == null || data.Length < 1) throw new ArgumentNullException(nameof(data));
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = 0x00;
            }
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

        private void ReverseBitArray(BitArray array)
        {
            int length = array.Length;
            int mid = (length / 2);

            for (int i = 0; i < mid; i++)
            {
                bool bit = array[i];
                array[i] = array[length - i - 1];
                array[length - i - 1] = bit;
            }
        }

        private byte[] ReverseByteArray(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 1) throw new ArgumentNullException(nameof(bytes));

            byte[] ret = new byte[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                ret[i] = ReverseByte(bytes[i]);
            }

            return ret;
        }

        private byte ReverseByte(byte b)
        {
            return (byte)(((b * 0x0802u & 0x22110u) | (b * 0x8020u & 0x88440u)) * 0x10101u >> 16);
        }

        private byte[] BitArrayToBytes(BitArray bits)
        {
            if (bits == null || bits.Length < 1) throw new ArgumentNullException(nameof(bits));
            if (bits.Length % 8 != 0) throw new ArgumentException("BitArray length must be divisible by 8.");

            byte[] ret = new byte[(bits.Length - 1) / 8 + 1];
            bits.CopyTo(ret, 0);
            return ret;
        }
          
        private MessageField GetMessageField(int i)
        {
            switch (i)
            {
                case 0:
                    _Logger?.Invoke(_Header + "GetMessageField returning field PresharedKey for bit number " + i);
                    return new MessageField(0, "PresharedKey", FieldType.ByteArray, 16);

                case 1:
                    _Logger?.Invoke(_Header + "GetMessageField returning field Status for bit number " + i);
                    return new MessageField(1, "Status", FieldType.Int32, 4);

                case 2:
                    _Logger?.Invoke(_Header + "GetMessageField returning field MetadataLength for bit number " + i);
                    return new MessageField(2, "MetadataLength", FieldType.Int64, 8);

                case 3:
                    _Logger?.Invoke(_Header + "GetMessageField returning field SyncRequest for bit number " + i);
                    return new MessageField(3, "SyncRequest", FieldType.Int32, 4);

                case 4:
                    _Logger?.Invoke(_Header + "GetMessageField returning field SyncResponse for bit number " + i);
                    return new MessageField(4, "SyncResponse", FieldType.Int32, 4);

                case 5:
                    _Logger?.Invoke(_Header + "GetMessageField returning field ExpirationUtc for bit number " + i);
                    return new MessageField(5, "ExpirationUtc", FieldType.String, 32);

                case 6:
                    _Logger?.Invoke(_Header + "GetMessageField returning field ConversationGuid for bit number " + i);
                    return new MessageField(6, "ConversationGuid", FieldType.String, 36);

                default:
                    throw new ArgumentException("Unknown bit number " + i + ".");
            }
        }

        private void SetMessageValue(MessageField field, object val)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (val == null) throw new ArgumentNullException(nameof(val));
             
            switch (field.BitNumber)
            {
                case 0:
                    _PresharedKey = (byte[])val;
                    _Logger?.Invoke(_Header + "SetMessageValue field " + field.BitNumber + " PresharedKey: " + Encoding.UTF8.GetString(PresharedKey));
                    return;

                case 1:
                    _Status = (MessageStatus)((int)val);
                    _Logger?.Invoke(_Header + "SetMessageValue field " + field.BitNumber + " Status: " + _Status.ToString());
                    return;

                case 2:
                    _MetadataLength = (long)val;
                    _Logger?.Invoke(_Header + "SetMessageValue field " + field.BitNumber + " MetadataLength: " + _MetadataLength);
                    return;

                case 3:
                    _SyncRequest = Convert.ToBoolean(val);
                    _Logger?.Invoke(_Header + "SetMessageValue field " + field.BitNumber + " SyncRequest: " + _SyncRequest);
                    return;

                case 4:
                    _SyncResponse = Convert.ToBoolean(val);
                    _Logger?.Invoke(_Header + "SetMessageValue field " + field.BitNumber + " SyncResponse: " + _SyncResponse);
                    return;

                case 5: 
                    _ExpirationUtc = DateTime.ParseExact(val.ToString().Trim(), _DateTimeFormat, CultureInfo.InvariantCulture);
                    _Logger?.Invoke(_Header + "SetMessageValue field " + field.BitNumber + " ExpirationUtc: " + _ExpirationUtc.Value.ToString());
                    return;

                case 6:
                    _ConversationGuid = val.ToString();
                    _Logger?.Invoke(_Header + "SetMessageValue field " + field.BitNumber + " ConversationGuid: " + _ConversationGuid);
                    return;

                default:
                    throw new ArgumentException("Unknown bit number: " + field.BitNumber + ", length " + field.Length + " " + field.Name + ".");
            }
        }
         
        #endregion Private-Methods
    }
}