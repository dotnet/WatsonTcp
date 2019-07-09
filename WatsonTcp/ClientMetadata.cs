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

        private readonly SemaphoreSlim _ReadLock;
        private readonly SemaphoreSlim _WriteLock;
        private SslStream _SslStream;

        #endregion

        #region Constructors

        public ClientMetadata(TcpClient tcp)
        {
            _TcpClient = tcp ?? throw new ArgumentNullException(nameof(tcp));
            _NetworkStream = tcp.GetStream();
            _IpPort = tcp.Client.RemoteEndPoint.ToString();

            _ReadLock = new SemaphoreSlim(1);
            _WriteLock = new SemaphoreSlim(1);
        }

        #endregion

        #region Internal-Properties

        internal TcpClient TcpClient => _TcpClient;

        internal NetworkStream NetworkStream => _NetworkStream;

        internal SslStream SslStream
        {
            get => _SslStream;
            set => _SslStream = value;
        }

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
