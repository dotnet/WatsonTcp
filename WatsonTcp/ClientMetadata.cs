using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace WatsonTcp
{
    public class ClientMetadata
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private TcpClient tcpClient;
        private NetworkStream networkStream;
        private SslStream sslStream;
        private string ipPort;

        #endregion

        #region Constructors-and-Factories

        public ClientMetadata(TcpClient tcp)
        {
            if (tcp == null) throw new ArgumentNullException(nameof(tcp));
            tcpClient = tcp;

            networkStream = tcp.GetStream();

            ipPort = tcp.Client.RemoteEndPoint.ToString();
        }

        #endregion

        #region Public-Methods

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

        #endregion
    }
}
