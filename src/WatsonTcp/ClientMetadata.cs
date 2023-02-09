using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WatsonTcp
{
    /// <summary>
    /// Client metadata.
    /// </summary>
    public class ClientMetadata : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// GUID.
        /// </summary>
        public Guid Guid { get; } = Guid.NewGuid();

        /// <summary>
        /// IP:port for the connection.
        /// </summary>
        public string IpPort
        {
            get
            {
                return _IpPort;
            }
        }

        /// <summary>
        /// Name for the client, managed by the developer (you).
        /// </summary>
        public string Name { get; set; } = null;

        /// <summary>
        /// Metadata for the client, managed by the developer (you).
        /// </summary>
        public object Metadata { get; set; } = null; 

        #endregion

        #region Internal-Members

        internal TcpClient TcpClient
        {
            get 
            { 
                return _TcpClient; 
            }
        }

        internal NetworkStream NetworkStream
        {
            get
            {
                return _NetworkStream;
            }
            set
            {
                _NetworkStream = value;
                if (_NetworkStream != null)
                { 
                    _DataStream = _NetworkStream;
                }
            }
        }

        internal SslStream SslStream
        {
            get
            {
                return _SslStream;
            }
            set
            {
                _SslStream = value;
                if (_SslStream != null)
                { 
                    _DataStream = _SslStream;
                }
            }
        }

        internal Stream DataStream
        {
            get
            {
                return _DataStream;
            }
        }
         
        internal byte[] SendBuffer { get; set; } = new byte[65536];
        internal Task DataReceiver { get; set; } = null;

        internal SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);
        internal SemaphoreSlim ReadLock = new SemaphoreSlim(1, 1);

        internal CancellationTokenSource TokenSource = new CancellationTokenSource();
        internal CancellationToken Token;

        #endregion

        #region Private-Members

        private TcpClient _TcpClient = null;
        private NetworkStream _NetworkStream = null;
        private SslStream _SslStream = null;
        private Stream _DataStream = null;
        private string _IpPort = null;

        #endregion

        #region Constructors-and-Factories

        internal ClientMetadata(TcpClient tcp)
        {
            _TcpClient = tcp ?? throw new ArgumentNullException(nameof(tcp));

            _IpPort = tcp.Client.RemoteEndPoint.ToString();

            NetworkStream = tcp.GetStream();
            Token = TokenSource.Token;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tear down the object and dispose of resources.
        /// </summary>
        public void Dispose()
        {
            if (TokenSource != null)
            {
                if (!TokenSource.IsCancellationRequested)
                {
                    TokenSource.Cancel();
                    TokenSource.Dispose();
                }
            }

            _SslStream?.Close();
            _NetworkStream?.Close();

            if (_TcpClient != null)
            {
                _TcpClient.Close();
                _TcpClient.Dispose();
            }

            while (DataReceiver?.Status == TaskStatus.Running)
            {
                Task.Delay(30).Wait();
            }
        }

        /// <summary>
        /// Human-readable representation of the object.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string ret = "[";
            ret += Guid.ToString() + "|" + IpPort;
            if (!String.IsNullOrEmpty(Name)) ret += "|" + Name;
            ret += "]";
            return ret;
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}