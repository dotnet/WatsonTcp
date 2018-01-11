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

        // Flag: Has Dispose already been called?
        private bool disposed = false;

        private TcpClient tcpClient;
        private NetworkStream networkStream;
        private SslStream sslStream;
        private string ipPort;

        #endregion

        #region Constructors-and-Factories

        public ClientMetadata(TcpClient tcp)
        {
            tcpClient = tcp ?? throw new ArgumentNullException(nameof(tcp));

            networkStream = tcp.GetStream();

            ipPort = tcp.Client.RemoteEndPoint.ToString();
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
            get { return tcpClient; }
        }

        public NetworkStream NetworkStream
        {
            get { return networkStream; }
        }

        public SslStream SslStream
        {
            get { return sslStream; }
            set { sslStream = value; }
        }

        public string IpPort
        {
            get { return ipPort; }
        }

        #endregion

        #region Private-Methods

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }

            if (disposing)
            {
                if (sslStream != null)
                {
                    sslStream.Close();
                }

                if (networkStream != null)
                {
                    networkStream.Close();
                }

                if (tcpClient != null)
                {
                    tcpClient.Close();
                }
            }

            disposed = true;
        }

        #endregion
    }
}
