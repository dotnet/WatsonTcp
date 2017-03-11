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

        public string Ip;
        public int Port;
        public TcpClient Tcp;
        public SslStream Ssl;

        #endregion

        #region Private-Members
        
        #endregion

        #region Constructors-and-Factories

        public ClientMetadata()
        {

        }

        public ClientMetadata(TcpClient tcp)
        {
            if (tcp == null) throw new ArgumentNullException(nameof(tcp));
            Tcp = tcp;

            Ip = ((IPEndPoint)Tcp.Client.RemoteEndPoint).Address.ToString();
            Port = ((IPEndPoint)Tcp.Client.RemoteEndPoint).Port;
        }

        public ClientMetadata(TcpClient tcp, SslStream ssl)
        {
            if (tcp == null) throw new ArgumentNullException(nameof(tcp));
            if (ssl == null) throw new ArgumentNullException(nameof(ssl));

            Tcp = tcp;
            Ssl = ssl;

            Ip = ((IPEndPoint)Tcp.Client.RemoteEndPoint).Address.ToString();
            Port = ((IPEndPoint)Tcp.Client.RemoteEndPoint).Port;
        }

        #endregion

        #region Public-Methods

        public string IpPort()
        {
            return Ip + ":" + Port;
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
