using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

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

        public ClientMetadata(TcpClient tcp, SslStream ssl)
        {
            if (tcp == null) throw new ArgumentNullException(nameof(tcp));
            if (ssl == null) throw new ArgumentNullException(nameof(ssl));

            Tcp = tcp;
            Ssl = ssl;

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
