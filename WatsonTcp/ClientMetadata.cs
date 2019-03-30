using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace WatsonTcp
{
    public class ClientMetadata : IDisposable
    {
        #region Public-Members

        #endregion

        #region Private-Members
         
        private bool _Disposed = false;

        private TcpClient _TcpClient;
        private NetworkStream _NetworkStream;
        private SslStream _SslStream;
        private string _IpPort;

        #endregion

        #region Constructors-and-Factories

        public ClientMetadata(TcpClient tcp)
        {
            _TcpClient = tcp ?? throw new ArgumentNullException(nameof(tcp));

            _NetworkStream = tcp.GetStream();

            _IpPort = tcp.Client.RemoteEndPoint.ToString();
        }

        #endregion

        #region Public-Methods

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

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

        #endregion

        #region Private-Methods

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

            _Disposed = true;
        }

        #endregion
    }
}
