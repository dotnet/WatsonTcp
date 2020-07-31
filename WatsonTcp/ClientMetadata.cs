using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;

namespace WatsonTcp
{
    internal class ClientMetadata : IDisposable
    { 
        internal TcpClient TcpClient
        {
            get 
            { 
                return _TcpClient; 
            }
        }

        internal NetworkStream NetworkStream
        {
            get
            {
                return _NetworkStream;
            }
            set
            {
                _NetworkStream = value;
                if (_NetworkStream != null)
                { 
                    _DataStream = _NetworkStream;
                }
            }
        }

        internal SslStream SslStream
        {
            get
            {
                return _SslStream;
            }
            set
            {
                _SslStream = value;
                if (_SslStream != null)
                { 
                    _DataStream = _SslStream;
                }
            }
        }

        internal Stream DataStream
        {
            get
            {
                return _DataStream;
            }
        }
         
        internal string IpPort
        {
            get 
            { 
                return _IpPort; 
            }
        }
         
        internal CancellationTokenSource TokenSource = new CancellationTokenSource();
        internal CancellationToken Token;
         
        private TcpClient _TcpClient = null;
        private NetworkStream _NetworkStream = null;
        private SslStream _SslStream = null;
        private Stream _DataStream = null;
        private string _IpPort = null;
         
        internal ClientMetadata(TcpClient tcp)
        {
            if (tcp == null) throw new ArgumentNullException(nameof(tcp));

            _TcpClient = tcp;
            _IpPort = tcp.Client.RemoteEndPoint.ToString();

            NetworkStream = tcp.GetStream();
            Token = TokenSource.Token;
        }
          
        /// <summary>
        /// Tear down the object and dispose of resources.
        /// </summary>
        public void Dispose()
        {
            if (_SslStream != null)
            {
                _SslStream.Close(); 
            }

            if (_NetworkStream != null)
            {
                _NetworkStream.Close(); 
            }

            if (TokenSource != null)
            {
                if (!TokenSource.IsCancellationRequested) TokenSource.Cancel();
                TokenSource.Dispose();
                TokenSource = null;
            }
             
            if (_TcpClient != null)
            {
                _TcpClient.Close();
                _TcpClient.Dispose();
            }
        } 
    }
}