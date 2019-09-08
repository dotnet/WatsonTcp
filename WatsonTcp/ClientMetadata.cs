using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;

namespace WatsonTcp
{
    public class ClientMetadata : IDisposable
    {
        #region Public-Members

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

        public SemaphoreSlim ReadLock { get; set; }

        public SemaphoreSlim WriteLock { get; set; }

        public CancellationTokenSource TokenSource { get; set; }

        public CancellationToken Token { get; set; }

        #endregion Public-Members

        #region Private-Members

        private bool _Disposed = false;

        private TcpClient _TcpClient;
        private NetworkStream _NetworkStream;
        private SslStream _SslStream;
        private string _IpPort;

        #endregion Private-Members

        #region Constructors-and-Factories

        public ClientMetadata(TcpClient tcp)
        {
            _TcpClient = tcp ?? throw new ArgumentNullException(nameof(tcp));
            _NetworkStream = tcp.GetStream();
            _IpPort = tcp.Client.RemoteEndPoint.ToString();

            ReadLock = new SemaphoreSlim(1);
            WriteLock = new SemaphoreSlim(1);

            TokenSource = new CancellationTokenSource();
            Token = TokenSource.Token;
        }

        #endregion Constructors-and-Factories

        #region Public-Methods

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion Public-Methods

        #region Private-Methods

        protected virtual void Dispose(bool disposing)
        { 
            if (_Disposed)
            {
                return;
            }

            if (disposing)
            {
                #region Cancellation-Token

                TokenSource.Cancel();
                TokenSource.Dispose();

                #endregion

                #region Locks

                if (WriteLock != null) WriteLock.Dispose();
                if (ReadLock != null) ReadLock.Dispose();
                
                #endregion

                #region SslStream

                if (_SslStream != null)
                {
                    try
                    {
                        _SslStream.Close();
                        _SslStream.Dispose(); 
                    }
                    catch (Exception)
                    { 
                    }
                }

                #endregion

                #region TcpStream

                if (_NetworkStream != null)
                {
                    try
                    {
                        _NetworkStream.Close();
                        _NetworkStream.Dispose(); 
                    }
                    catch (Exception)
                    { 
                    }
                }

                #endregion

                #region TcpClient

                if (_TcpClient != null)
                {
                    if (_TcpClient.Client != null)
                    {
                        try
                        {
                            // if (_Client.Client.Connected) _Client.Client.Disconnect(false);
                            // _Client.Client.Shutdown(SocketShutdown.Both);
                            _TcpClient.Client.Close(0);
                            _TcpClient.Client.Dispose(); 
                        }
                        catch (Exception)
                        {
                        }
                    }

                    try
                    {
                        _TcpClient.Close();
                        _TcpClient = null; 
                    }
                    catch (Exception)
                    { 
                    }
                }

                #endregion 
            }

            _Disposed = true;
        }

        #endregion Private-Methods
    }
}