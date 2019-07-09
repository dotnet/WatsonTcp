namespace WatsonTcp
{
    using System;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Threading;

    public class ClientMetadata : IDisposable
    {
        #region Private-Fields

        private bool _Disposed = false;

        private readonly TcpClient _TcpClient;
        private readonly NetworkStream _NetworkStream;
        private readonly string _IpPort;

        private readonly SemaphoreSlim _ReadLock = new SemaphoreSlim(1);
        private readonly SemaphoreSlim _WriteLock = new SemaphoreSlim(1);
        private SslStream _SslStream;

        #endregion

        #region Constructors

        public ClientMetadata(TcpClient tcp) :
            this(tcp, false, null)
        {
        }

        public ClientMetadata(TcpClient tcp, bool useSsl) :
            this(tcp, useSsl, null)
        {
        }

        public ClientMetadata(TcpClient tcp, bool useSsl, RemoteCertificateValidationCallback remoteCertificateValidationCallback)
        {
            _TcpClient = tcp ?? throw new ArgumentNullException(nameof(tcp));
            _NetworkStream = tcp.GetStream();
            _IpPort = tcp.Client.RemoteEndPoint.ToString();

            if (useSsl)
            {
                if (remoteCertificateValidationCallback != null)
                {
                    _SslStream = new SslStream(_NetworkStream, false, remoteCertificateValidationCallback);
                }
                else
                {
                    _SslStream = new SslStream(_NetworkStream, false);
                }
            }
        }

        #endregion

        #region Internal-Properties

        internal TcpClient TcpClient => _TcpClient;

        internal NetworkStream NetworkStream => _NetworkStream;

        internal SslStream SslStream => _SslStream;

        internal string IpPort => _IpPort;

        internal SemaphoreSlim ReadLock => _ReadLock;

        internal SemaphoreSlim WriteLock => _WriteLock;

        #endregion

        #region Public-Methods

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed)
            {
                return;
            }

            if (disposing)
            {
                if (_SslStream != null)
                {
                    _SslStream.Close();
                }

                if (_NetworkStream != null)
                {
                    _NetworkStream.Close();
                }

                if (_TcpClient != null)
                {
                    _TcpClient.Close();
                }
            }

            ReadLock.Dispose();
            WriteLock.Dispose();

            _Disposed = true;
        }

        #endregion
    }
}
