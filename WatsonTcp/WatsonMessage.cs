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
using Microsoft.Extensions.ObjectPool;
using ProtoBuf;

namespace WatsonTcp
{
    [ProtoContract]
    internal class WatsonMessage<TMetadata>
    {
        #region Public-Members

        /// <summary>
        /// Length of the data.
        /// </summary>
        [ProtoMember(1)]
        public long ContentLength { get; set; }

        /// <summary>
        /// Preshared key for connection authentication.
        /// </summary>
        [ProtoMember(2)]
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
        /// </summary>
        [ProtoMember(3)]
        public MessageStatus Status = MessageStatus.Normal;

        /// <summary>
        /// Metadata dictionary; contains user-supplied metadata.
        /// </summary>
        [ProtoMember(4)]
        public TMetadata Metadata
        {
            get
            {
                return _Metadata;
            }
            set
            {
                _Metadata = value;
            }
        }

        /// <summary>
        /// Indicates if the message is a synchronous request.
        /// </summary>
        [ProtoMember(5)]
        public bool SyncRequest = false;

        /// <summary>
        /// Indicates if the message is a synchronous response.
        /// </summary>
        [ProtoMember(6)]
        public bool SyncResponse = false;

        /// <summary>
        /// Indicates the current time as perceived by the sender; useful for determining expiration windows.
        /// </summary>
        [ProtoMember(7)]
        public DateTime? SenderTimestamp = null;

        /// <summary>
        /// Indicates an expiration time in UTC; only applicable to synchronous requests.
        /// </summary>
        [ProtoMember(8)]
        public DateTime? Expiration = null;

        /// <summary>
        /// Indicates the conversation GUID of the message. 
        /// </summary>
        [ProtoMember(9)]
        public string ConversationGuid = null;

        /// <summary>
        /// The type of compression used in the message.
        /// </summary>
        [ProtoMember(10)]
        public CompressionType Compression = CompressionType.None;

        /// <summary>
        /// Stream containing the message data.
        /// </summary>
        public Stream DataStream
        {
            get
            {
                return _DataStream;
            }
        }

        /// <summary>
        /// Transmits the header to the destination stream
        /// </summary>
        /// <param name="target"></param>
        internal void SendHeader(Stream target)
        {
            Serializer.Serialize<WatsonMessage<TMetadata>>(target, this);
        }

        private static readonly ObjectPool<MemoryStream> MemoryStreamsPool =
            new DefaultObjectPool<MemoryStream>(new DefaultPooledObjectPolicy<MemoryStream>());

        /// <summary>
        /// Transmits the header to the destination stream asynchronously
        /// </summary>
        /// <param name="target"></param>
        internal Task SendHeaderAsync(Stream target)
        {
            var ms = MemoryStreamsPool.Get();
            ms.SetLength(0);
            Serializer.Serialize<WatsonMessage<TMetadata>>(ms, this);
            return ms.CopyToAsync(target)
                .ContinueWith(ReturnMemoryStream, ms);
        }

        private static void ReturnMemoryStream(Task job, object ms)
        {
            if (ms is MemoryStream stream)
            {
                MemoryStreamsPool.Return(stream);
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

        private string _Header = "[WatsonMessage<" + typeof(TMetadata).Name + ">] ";
        private string _DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fffzzz"; // 32 bytes

        private int _ReadStreamBuffer = 65536; 
        private byte[] _PresharedKey;
        private TMetadata _Metadata = default(TMetadata);
        private Stream _DataStream = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Do not use.
        /// </summary>
        [Obsolete("Do not use, instead use WatsonMessage<TMetadata>.Borrow();")]
        public WatsonMessage()
        { 
            Status = MessageStatus.Normal;
            Register();
        }

        #region ProtoBuf Type Registration
        // ReSharper disable once StaticMemberInGenericType
        private static bool _haveRegisteredType = false;
        private void Register()
        {
            if (!_haveRegisteredType)
            {
                ProtoBuf.Meta.RuntimeTypeModel.Default.Add(typeof(WatsonMessage<TMetadata>), true).AddSubType(100, typeof(TMetadata));
                _haveRegisteredType = true;
            }
        }
        #endregion

        /// <summary>
        /// If enabled, will use a ObjectPool to manage instances. You will need to call .Return() when done to return items to the pool.
        /// </summary>
        // ReSharper disable once StaticMemberInGenericType
        public static bool UsePooling = false;

        private static readonly ObjectPool<WatsonMessage<TMetadata>> Pool = new DefaultObjectPool<WatsonMessage<TMetadata>>(new DefaultPooledObjectPolicy<WatsonMessage<TMetadata>>());

        public void Return()
        {
            if (UsePooling)
                Pool.Return(this);
        }

        /// <summary>
        /// Do not use.
        /// </summary>
        internal static WatsonMessage<TMetadata> Borrow()
        {
            if (UsePooling)
                return Pool.Get();
            
#pragma warning disable 618
            return new WatsonMessage<TMetadata>();
#pragma warning restore 618
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
        /// <param name="convGuid">Conversation GUID.</param>
        /// <param name="logger">Logger method.</param>
        internal void Set(
            TMetadata metadata, 
            long contentLength, 
            Stream stream, 
            bool syncRequest, 
            bool syncResponse, 
            DateTime? expiration, 
            string convGuid, 
            CompressionType compression, 
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

            if (SyncRequest)
                SenderTimestamp = DateTime.Now;

            _DataStream = stream;
            _Logger = logger; 
        }

        internal void Reset()
        {
            Set(default(TMetadata), 0, null, false, false, null, null, CompressionType.None, null);
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
                
                WatsonMessage<TMetadata> msg =
                    Serializer.DeserializeWithLengthPrefix<WatsonMessage<TMetadata>>(_DataStream,  PrefixStyle.Fixed32);

                ContentLength = msg.ContentLength;
                PresharedKey = msg.PresharedKey;
                Status = msg.Status;
                Metadata = msg.Metadata;
                SyncRequest = msg.SyncRequest;
                SyncResponse = msg.SyncResponse;
                SenderTimestamp = msg.SenderTimestamp;
                Expiration = msg.Expiration;
                ConversationGuid = msg.ConversationGuid;
                Compression = msg.Compression;

                _Logger?.Invoke(_Header + "BuildFromStream header processing complete: " + this);

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
                    e +
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
            StringBuilder ret = new StringBuilder();
            ret.AppendLine("---");
            ret.AppendLine($"  Preshared key     : {(PresharedKey != null ? WatsonCommon<TMetadata>.ByteArrayToHex(PresharedKey) : "null")}");
            ret.AppendLine($"  Status            : {Status}");
            ret.AppendLine($"  SyncRequest       : {SyncRequest}");
            ret.AppendLine($"  SyncResponse      : {SyncResponse}");
            ret.AppendLine($"  ExpirationUtc     : {(Expiration != null ? Expiration.Value.ToString(_DateTimeFormat) : "null")}");
            ret.AppendLine($"  Conversation GUID : {ConversationGuid}");
            ret.AppendLine($"  Compression       : {Compression}");

            if (Metadata != null)
            {
                ret.AppendLine("  Metadata          : " + Metadata);
            }

            if (DataStream != null)
                ret.AppendLine("  DataStream        : present, " + ContentLength + " bytes");

            return ret.ToString();
        }

        #endregion Public-Methods

        #region Private-Methods

        #endregion
    }
}