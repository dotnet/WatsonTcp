namespace WatsonTcp
{
    using System;
    using System.IO;
    using System.Net.Security;
    using System.Net.Sockets;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;

    internal class ClientMetadata : IDisposable
    {
        #region Private-Fields

        private bool _Disposed = false;

        private readonly TcpClient _TcpClient;
        private readonly NetworkStream _NetworkStream;
        private readonly SslStream _SslStream;
        private readonly Stream _TrafficStream;

        private readonly string _IpPort;

        private readonly SemaphoreSlim _ReadLock = new SemaphoreSlim(1);
        private readonly SemaphoreSlim _WriteLock = new SemaphoreSlim(1);

        #endregion

        #region Constructors

        internal ClientMetadata(TcpClient tcp, Mode mode, bool acceptInvalidCertificates)
        {
            _TcpClient = tcp ?? throw new ArgumentNullException(nameof(tcp));
            _NetworkStream = tcp.GetStream();
            _IpPort = tcp.Client.RemoteEndPoint.ToString();

            if (mode == Mode.Tcp)
            {
                _TrafficStream = _NetworkStream;
            }
            else
            {
                if (acceptInvalidCertificates)
                {
                    _SslStream = new SslStream(_NetworkStream, false, new RemoteCertificateValidationCallback(AcceptCertificate));
                }
                else
                {
                    _SslStream = new SslStream(_NetworkStream, false);
                }

                _TrafficStream = _SslStream;
            }
        }

        #endregion

        #region Internal-Properties

        internal TcpClient TcpClient => _TcpClient;

        internal SslStream SslStream => _SslStream;

        internal Stream TrafficStream => _TrafficStream;

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

        #region Private-Methods

        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // Allow untrusted certificates.
            return true;
        }

        #endregion
    }
}
