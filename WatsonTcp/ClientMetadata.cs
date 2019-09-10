using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;

namespace WatsonTcp
{
    public class ClientMetadata : IDisposable
    {
        #region Public-Members

        public TcpClient TcpClient
        {
            get { return _TcpClient; }
        }

        public NetworkStream NetworkStream
        {
            get { return _NetworkStream; }
        }

        public SslStream SslStream
        {
            get { return _SslStream; }
            set { _SslStream = value; }
        }

        public string IpPort
        {
            get { return _IpPort; }
        }

        public SemaphoreSlim ReadLock { get; set; }

        public SemaphoreSlim WriteLock { get; set; }

        public CancellationTokenSource TokenSource { get; set; }

        public CancellationToken Token { get; set; }

        #endregion Public-Members

        #region Private-Members
         
        private TcpClient _TcpClient;
        private NetworkStream _NetworkStream;
        private SslStream _SslStream;
        private string _IpPort;

        #endregion Private-Members

        #region Constructors-and-Factories

        public ClientMetadata(TcpClient tcp)
        {
            _TcpClient = tcp ?? throw new ArgumentNullException(nameof(tcp));
            _NetworkStream = tcp.GetStream();
            _IpPort = tcp.Client.RemoteEndPoint.ToString();

            ReadLock = new SemaphoreSlim(1);
            WriteLock = new SemaphoreSlim(1);

            TokenSource = new CancellationTokenSource();
            Token = TokenSource.Token;
        }

        #endregion Constructors-and-Factories

        #region Public-Methods

        public void Dispose()
        {
            if (_SslStream != null)
            {
                _SslStream.Close();
                _SslStream.Dispose();
                _SslStream = null;
            }

            if (_NetworkStream != null)
            {
                _NetworkStream.Close();
                _NetworkStream.Dispose();
                _NetworkStream = null;
            }

            if (TokenSource != null)
            {
                if (!TokenSource.IsCancellationRequested) TokenSource.Cancel();
                TokenSource.Dispose();
                TokenSource = null;
            }

            if (WriteLock != null)
            {
                WriteLock.Dispose();
                WriteLock = null;
            }

            if (ReadLock != null)
            {
                ReadLock.Dispose();
                ReadLock = null;
            }
             
            if (_TcpClient != null)
            {
                _TcpClient.Close();
                _TcpClient.Dispose();
                _TcpClient = null;
            }
        }

        #endregion Public-Methods

        #region Private-Methods
         
        #endregion Private-Methods
    }
}