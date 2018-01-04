using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace WatsonTcp
{
    public class ClientMetadata
    {
        #region Public-Members

        public TcpClient Tcp;
        public SslStream Ssl;

        #endregion

        #region Private-Members

        private string ipPort;

        #endregion

        #region Constructors-and-Factories

        public ClientMetadata(TcpClient tcp)
        {
            if (tcp == null) throw new ArgumentNullException(nameof(tcp));
            Tcp = tcp;

            ipPort = tcp.Client.RemoteEndPoint.ToString();
        }

        #endregion

        #region Public-Methods

        public string IpPort
        {
            get { return ipPort; }
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
