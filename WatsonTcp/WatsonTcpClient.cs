using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json; 

namespace WatsonTcp
{
    /// <summary>
    /// Watson TCP client, with or without SSL.
    /// </summary>
    public class WatsonTcpClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Watson TCP client settings.
        /// </summary>
        public WatsonTcpClientSettings Settings
        {
            get
            {
                return _Settings;
            }
            set
            {
                if (value == null) _Settings = new WatsonTcpClientSettings();
                else _Settings = value;
            }
        }

        /// <summary>
        /// Watson TCP client events.
        /// </summary>
        public WatsonTcpClientEvents Events
        {
            get
            {
                return _Events;
            }
            set
            {
                if (value == null) _Events = new WatsonTcpClientEvents();
                else _Events = value;
            }
        }

        /// <summary>
        /// Watson TCP client callbacks.
        /// </summary>
        public WatsonTcpClientCallbacks Callbacks
        {
            get
            {
                return _Callbacks;
            }
            set
            {
                if (value == null) _Callbacks = new WatsonTcpClientCallbacks();
                else _Callbacks = value;
            }
        }

        /// <summary>
        /// Watson TCP statistics.
        /// </summary>
        public WatsonTcpStatistics Statistics
        {
            get
            {
                return _Statistics;
            }
        }

        /// <summary>
        /// Watson TCP keepalive settings.
        /// </summary>
        public WatsonTcpKeepaliveSettings Keepalive
        {
            get
            {
                return _Keepalive;
            }
            set
            {
                if (value == null) _Keepalive = new WatsonTcpKeepaliveSettings();
                else _Keepalive = value;
            }
        }

        /// <summary>
        /// Watson TCP client SSL configuration.
        /// </summary>
        public WatsonTcpClientSslConfiguration SslConfiguration
        {
            get
            {
                return _SslConfiguration;
            }
            set
            {
                if (value == null) _SslConfiguration = new WatsonTcpClientSslConfiguration();
                else _SslConfiguration = value;
            }
        }

        /// <summary>
        /// Indicates whether or not the client is connected to the server.
        /// </summary>
        public bool Connected { get; private set; }

        #endregion

        #region Private-Members

        private string _Header = "[WatsonTcpClient] ";
        private WatsonTcpClientSettings _Settings = new WatsonTcpClientSettings();
        private WatsonTcpClientEvents _Events = new WatsonTcpClientEvents();
        private WatsonTcpClientCallbacks _Callbacks = new WatsonTcpClientCallbacks();
        private WatsonTcpStatistics _Statistics = new WatsonTcpStatistics();
        private WatsonTcpKeepaliveSettings _Keepalive = new WatsonTcpKeepaliveSettings();
        private WatsonTcpClientSslConfiguration _SslConfiguration = new WatsonTcpClientSslConfiguration();

        private Mode _Mode = Mode.Tcp;
        private TlsVersion _TlsVersion = TlsVersion.Tls12;
        private string _SourceIp = null;
        private int _SourcePort = 0;
        private string _ServerIp = null;
        private int _ServerPort = 0; 

        private TcpClient _Client = null;
        private Stream _DataStream = null;
        private NetworkStream _TcpStream = null;
        private SslStream _SslStream = null;

        private X509Certificate2 _SslCertificate = null;
        private X509Certificate2Collection _SslCertificateCollection = null;

        private SemaphoreSlim _WriteLock = new SemaphoreSlim(1, 1);
        private SemaphoreSlim _ReadLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private CancellationToken _Token;
        private Task _DataReceiver = null;
        private Task _MonitorSyncResponses = null;
        private Task _IdleServerMonitor = null;

        private DateTime _LastActivity = DateTime.Now;
        private bool _IsTimeout = false;

        private readonly object _SyncResponseLock = new object();
        private Dictionary<string, SyncResponse> _SyncResponses = new Dictionary<string, SyncResponse>(); 

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize the Watson TCP client without SSL.  Call Start() afterward to connect to the server.
        /// </summary>
        /// <param name="serverIp">The IP address or hostname of the server.</param>
        /// <param name="serverPort">The TCP port on which the server is listening.</param>
        public WatsonTcpClient(
            string serverIp,
            int serverPort)
        {
            if (String.IsNullOrEmpty(serverIp)) throw new ArgumentNullException(nameof(serverIp));
            if (serverPort < 0) throw new ArgumentOutOfRangeException(nameof(serverPort));
              
            _Mode = Mode.Tcp;
            _ServerIp = serverIp;
            _ServerPort = serverPort;

            SerializationHelper.InstantiateConverter(); // Unity fix
        }

        /// <summary>
        /// Initialize the Watson TCP client with SSL.  Call Start() afterward to connect to the server.
        /// </summary>
        /// <param name="serverIp">The IP address or hostname of the server.</param>
        /// <param name="serverPort">The TCP port on which the server is listening.</param>
        /// <param name="pfxCertFile">The file containing the SSL certificate.</param>
        /// <param name="pfxCertPass">The password for the SSL certificate.</param>
        /// <param name="tlsVersion">The TLS version used for this connection.</param>
        public WatsonTcpClient(
            string serverIp,
            int serverPort,
            string pfxCertFile,
            string pfxCertPass,
            TlsVersion tlsVersion = TlsVersion.Tls12)
        {
            if (String.IsNullOrEmpty(serverIp)) throw new ArgumentNullException(nameof(serverIp));
            if (serverPort < 0) throw new ArgumentOutOfRangeException(nameof(serverPort));
              
            _Mode = Mode.Ssl;
            _TlsVersion = tlsVersion;
            _ServerIp = serverIp;
            _ServerPort = serverPort;

            if (!String.IsNullOrEmpty(pfxCertFile))
            {
                if (String.IsNullOrEmpty(pfxCertPass))
                {
                    _SslCertificate = new X509Certificate2(pfxCertFile);
                }
                else
                {
                    _SslCertificate = new X509Certificate2(pfxCertFile, pfxCertPass);
                }

                _SslCertificateCollection = new X509Certificate2Collection
                {
                    _SslCertificate
                };
            }
            else
            {
                _SslCertificateCollection = new X509Certificate2Collection();
            }

            SerializationHelper.InstantiateConverter(); // Unity fix
        }

        /// <summary>
        /// Initialize the Watson TCP client with SSL.  Call Start() afterward to connect to the server.
        /// </summary>
        /// <param name="serverIp">The IP address or hostname of the server.</param>
        /// <param name="serverPort">The TCP port on which the server is listening.</param>
        /// <param name="cert">The SSL certificate</param>
        /// <param name="tlsVersion">The TLS version used for this conenction.</param>
        public WatsonTcpClient(
            string serverIp, 
            int serverPort, 
            X509Certificate2 cert,
            TlsVersion tlsVersion = TlsVersion.Tls12)
        {
            if (String.IsNullOrEmpty(serverIp)) throw new ArgumentNullException(nameof(serverIp));
            if (serverPort < 0) throw new ArgumentOutOfRangeException(nameof(serverPort));
            if (cert == null) throw new ArgumentNullException(nameof(cert));
             
            _Mode = Mode.Ssl;
            _TlsVersion = tlsVersion;
            _SslCertificate = cert;
            _ServerIp = serverIp;
            _ServerPort = serverPort;

            _SslCertificateCollection = new X509Certificate2Collection
            {
                _SslCertificate
            };

            SerializationHelper.InstantiateConverter(); // Unity fix
        }

        #endregion

        #region Public-Methods

        /// <summary>        
        /// Disconnect the client and dispose of background workers.
        /// Do not reuse the object after disposal.
        /// </summary>
        public void Dispose()
        {
            Dispose(true); 
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Connect to the server.
        /// </summary>
        public void Connect()
        {
            if (Connected) throw new InvalidOperationException("Already connected to the server.");

            if (_Settings.LocalPort == 0)
            {
                _Client = new TcpClient();
            }
            else
            {
                IPEndPoint ipe = new IPEndPoint(IPAddress.Any, _Settings.LocalPort);
                _Client = new TcpClient(ipe);
            }

            _Statistics = new WatsonTcpStatistics();

            IAsyncResult asyncResult = null;
            WaitHandle waitHandle = null;
            bool connectSuccess = false;

            if (!_Events.IsUsingMessages && !_Events.IsUsingStreams) 
                throw new InvalidOperationException("One of either 'MessageReceived' or 'StreamReceived' events must first be set.");

            if (_Mode == Mode.Tcp)
            {
                #region TCP

                _Settings.Logger?.Invoke(Severity.Info, _Header + "connecting to " + _ServerIp + ":" + _ServerPort);

                _Client.LingerState = new LingerOption(true, 0);
                asyncResult = _Client.BeginConnect(_ServerIp, _ServerPort, null, null);
                waitHandle = asyncResult.AsyncWaitHandle;

                try
                {
                    connectSuccess = waitHandle.WaitOne(TimeSpan.FromSeconds(_Settings.ConnectTimeoutSeconds), false);
                    if (!connectSuccess)
                    {
                        _Client.Close();
                        _Settings.Logger?.Invoke(Severity.Error, _Header + "timeout connecting to " + _ServerIp + ":" + _ServerPort);
                        throw new TimeoutException("Timeout connecting to " + _ServerIp + ":" + _ServerPort);
                    }

                    _Client.EndConnect(asyncResult);
                    
                    _SourceIp = ((IPEndPoint)_Client.Client.LocalEndPoint).Address.ToString();
                    _SourcePort = ((IPEndPoint)_Client.Client.LocalEndPoint).Port;
                    _TcpStream = _Client.GetStream();
                    _DataStream = _TcpStream;
                    _SslStream = null;

                    if (_Keepalive.EnableTcpKeepAlives) EnableKeepalives();

                    Connected = true;
                }
                catch (Exception e)
                {
                    _Settings.Logger?.Invoke(Severity.Error, _Header + "exception encountered: " + Environment.NewLine + SerializationHelper.SerializeJson(e, true));
                    _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                    throw;
                }
                finally
                {
                    waitHandle.Close();
                }

                #endregion TCP
            }
            else if (_Mode == Mode.Ssl)
            {
                #region SSL

                _Settings.Logger?.Invoke(Severity.Info, _Header + "connecting with SSL to " + _ServerIp + ":" + _ServerPort);

                _Client.LingerState = new LingerOption(true, 0);
                asyncResult = _Client.BeginConnect(_ServerIp, _ServerPort, null, null);
                waitHandle = asyncResult.AsyncWaitHandle;

                try
                {
                    connectSuccess = waitHandle.WaitOne(TimeSpan.FromSeconds(_Settings.ConnectTimeoutSeconds), false);
                    if (!connectSuccess)
                    {
                        _Client.Close();
                        _Settings.Logger?.Invoke(Severity.Error, _Header + "timeout connecting to " + _ServerIp + ":" + _ServerPort);
                        throw new TimeoutException("Timeout connecting to " + _ServerIp + ":" + _ServerPort);
                    }

                    _Client.EndConnect(asyncResult);

                    _SourceIp = ((IPEndPoint)_Client.Client.LocalEndPoint).Address.ToString();
                    _SourcePort = ((IPEndPoint)_Client.Client.LocalEndPoint).Port;

                    if (_Settings.AcceptInvalidCertificates)
                        _SslStream = new SslStream(_Client.GetStream(), false, _SslConfiguration.ServerCertificateValidationCallback, _SslConfiguration.ClientCertificateSelectionCallback); 
                    else
                        _SslStream = new SslStream(_Client.GetStream(), false);

                    _SslStream.AuthenticateAsClient(_ServerIp, _SslCertificateCollection, _TlsVersion.ToSslProtocols(), !_Settings.AcceptInvalidCertificates);

                    if (!_SslStream.IsEncrypted)
                    {
                        _Settings.Logger?.Invoke(Severity.Error, _Header + "stream to " + _ServerIp + ":" + _ServerPort + " is not encrypted");
                        throw new AuthenticationException("Stream is not encrypted");
                    }

                    if (!_SslStream.IsAuthenticated)
                    {
                        _Settings.Logger?.Invoke(Severity.Error, _Header + "stream to " + _ServerIp + ":" + _ServerPort + " is not authenticated");
                        throw new AuthenticationException("Stream is not authenticated");
                    }

                    if (_Settings.MutuallyAuthenticate && !_SslStream.IsMutuallyAuthenticated)
                    {
                        _Settings.Logger?.Invoke(Severity.Error, _Header + "mutual authentication with " + _ServerIp + ":" + _ServerPort + " failed");
                        throw new AuthenticationException("Mutual authentication failed");
                    }

                    _DataStream = _SslStream;

                    Connected = true;
                }
                catch (Exception e)
                {
                    _Settings.Logger?.Invoke(Severity.Error, _Header + "exception encountered: " + Environment.NewLine + SerializationHelper.SerializeJson(e, true));
                    _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                    throw;
                }
                finally
                {
                    waitHandle.Close();
                }

                #endregion SSL
            }
            else
            {
                throw new ArgumentException("Unknown mode: " + _Mode.ToString());
            }

            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;

            _LastActivity = DateTime.Now;
            _IsTimeout = false;

            _DataReceiver = Task.Run(() => DataReceiver(), _Token);
            _IdleServerMonitor = Task.Run(() => IdleServerMonitor(), _Token);
            _MonitorSyncResponses = Task.Run(() => MonitorForExpiredSyncResponses(), _Token);
            _Events.HandleServerConnected(this, new ConnectionEventArgs((_ServerIp + ":" + _ServerPort)));
            _Settings.Logger?.Invoke(Severity.Info, _Header + "connected to " + _ServerIp + ":" + _ServerPort);
        }
         
        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public void Disconnect()
        {
            if (!Connected) throw new InvalidOperationException("Not connected to the server.");

            _Settings.Logger?.Invoke(Severity.Info, _Header + "disconnecting from " + _ServerIp + ":" + _ServerPort);

            if (Connected)
            {
                WatsonMessage msg = new WatsonMessage();
                msg.Status = MessageStatus.Shutdown;
                SendInternal(msg, 0, null);
            }

            if (_TokenSource != null)
            {
                // stop background tasks
                if (!_TokenSource.IsCancellationRequested)
                {
                    _TokenSource.Cancel();
                    _TokenSource.Dispose();
                }
            }
             
            if (_SslStream != null)
            {
                _SslStream.Close();
            }

            if (_TcpStream != null)
            {
                _TcpStream.Close();
            }

            if (_Client != null)
            {
                _Client.Close();
            }

            while (_DataReceiver?.Status == TaskStatus.Running)
            {
                Task.Delay(1).Wait();
            }

            while (_IdleServerMonitor?.Status == TaskStatus.Running)
            {
                Task.Delay(1).Wait();
            }

            Connected = false;

            _Settings.Logger?.Invoke(Severity.Info, _Header + "disconnected from " + _ServerIp + ":" + _ServerPort);
        }

        /// <summary>
        /// Send a pre-shared key to the server to authenticate.
        /// </summary>
        /// <param name="presharedKey">Up to 16-character string.</param>
        public void Authenticate(string presharedKey)
        {
            if (String.IsNullOrEmpty(presharedKey)) throw new ArgumentNullException(nameof(presharedKey));
            if (presharedKey.Length != 16) throw new ArgumentException("Preshared key length must be 16 bytes.");

            WatsonMessage msg = new WatsonMessage();
            msg.Status = MessageStatus.AuthRequested;
            msg.PresharedKey = Encoding.UTF8.GetBytes(presharedKey); 
            SendInternal(msg, 0, null);
        }
         
        /// <summary>
        /// Send data and metadata to the server.
        /// </summary>
        /// <param name="data">String containing data.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(string data, Dictionary<object, object> metadata = null)
        {
            if (String.IsNullOrEmpty(data)) return Send(new byte[0], metadata);
            else return Send(Encoding.UTF8.GetBytes(data), metadata);
        }
         
        /// <summary>
        /// Send data and metadata to the server.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="start">Start position within the supplied array.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(byte[] data, Dictionary<object, object> metadata = null, int start = 0)
        {
            if (data == null) data = new byte[0];
            WatsonCommon.BytesToStream(data, start, out int contentLength, out Stream stream);
            return Send(contentLength, stream, metadata);
        }

        /// <summary>
        /// Send data and metadata to the server using a stream.
        /// </summary>
        /// <param name="contentLength">The number of bytes in the stream.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(long contentLength, Stream stream, Dictionary<object, object> metadata = null)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (stream == null) stream = new MemoryStream(new byte[0]); 
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
            return SendInternal(msg, contentLength, stream);
        }
        
        /// <summary>
        /// Send data and metadata to the server asynchronously.
        /// </summary>
        /// <param name="data">String containing data.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string data, Dictionary<object, object> metadata = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(data)) return await SendAsync(new byte[0], metadata);
            if (token == default(CancellationToken)) token = _Token;
            return await SendAsync(Encoding.UTF8.GetBytes(data), metadata, 0, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data and metadata to the server asynchronously.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="start">Start position within the supplied array.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(byte[] data, Dictionary<object, object> metadata = null, int start = 0, CancellationToken token = default)
        {
            if (token == default(CancellationToken)) token = _Token;
            if (data == null) data = new byte[0];
            WatsonCommon.BytesToStream(data, start, out int contentLength, out Stream stream);
            return await SendAsync(contentLength, stream, metadata, token).ConfigureAwait(false);
        }
         
        /// <summary>
        /// Send data and metadata to the server from a stream asynchronously.
        /// </summary>
        /// <param name="contentLength">The number of bytes to send.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(long contentLength, Stream stream, Dictionary<object, object> metadata = null, CancellationToken token = default)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (token == default(CancellationToken)) token = _Token;
            if (stream == null) stream = new MemoryStream(new byte[0]);
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
            return await SendInternalAsync(msg, contentLength, stream, token).ConfigureAwait(false);
        }
         
        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="data">Data to send.</param>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(int timeoutMs, string data, Dictionary<object, object> metadata = null)
        {
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (String.IsNullOrEmpty(data)) return SendAndWait(timeoutMs, new byte[0], metadata);
            return SendAndWait(timeoutMs, Encoding.UTF8.GetBytes(data), metadata); 
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="data">Data to send.</param>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="start">Start position within the supplied array.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(int timeoutMs, byte[] data, Dictionary<object, object> metadata = null, int start = 0)
        {
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (data == null) data = new byte[0];
            DateTime expiration = DateTime.Now.AddMilliseconds(timeoutMs);
            WatsonCommon.BytesToStream(data, start, out int contentLength, out Stream stream);
            return SendAndWait(timeoutMs, contentLength, stream, metadata);
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="contentLength">The number of bytes to send from the supplied stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(int timeoutMs, long contentLength, Stream stream, Dictionary<object, object> metadata = null)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (stream == null) stream = new MemoryStream(new byte[0]);
            DateTime expiration = DateTime.Now.AddMilliseconds(timeoutMs);
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, true, false, expiration, Guid.NewGuid().ToString(), (_Settings.DebugMessages ? _Settings.Logger : null));
            return SendAndWaitInternal(msg, timeoutMs, contentLength, stream);
        }
         
        #endregion

        #region Private-Methods

        /// <summary>
        /// Disconnect the client and dispose of background workers.
        /// Do not reuse the object after disposal.
        /// </summary>
        /// <param name="disposing">Indicate if resources should be disposed.</param>
        protected virtual void Dispose(bool disposing)
        { 
            if (disposing)
            {
                _Settings.Logger?.Invoke(Severity.Info, _Header + "disposing");

                if (Connected) Disconnect();

                if (_WriteLock != null)
                {
                    _WriteLock.Dispose();
                }

                if (_ReadLock != null)
                {
                    _ReadLock.Dispose();
                }

                _Settings = null;
                _Events = null;
                _Callbacks = null;
                _Statistics = null;
                _Keepalive = null;
                _SslConfiguration = null;

                _SourceIp = null;
                _ServerIp = null;

                _Client = null;
                _DataStream = null;
                _TcpStream = null;
                _SslStream = null;

                _SslCertificate = null;
                _SslCertificateCollection = null;
                _WriteLock = null;
                _ReadLock = null;

                _DataReceiver = null;
                _MonitorSyncResponses = null; 
            } 
        }

        #region Connection

        private void EnableKeepalives()
        {
            try
            {
#if NETCOREAPP || NET5_0

                _Client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _Keepalive.TcpKeepAliveTime);
                _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _Keepalive.TcpKeepAliveInterval);
                _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _Keepalive.TcpKeepAliveRetryCount);

#elif NETFRAMEWORK

                // .NET Framework expects values in milliseconds
                byte[] keepAlive = new byte[12]; 
                Buffer.BlockCopy(BitConverter.GetBytes((uint)1), 0, keepAlive, 0, 4); 
                Buffer.BlockCopy(BitConverter.GetBytes((uint)(_Keepalive.TcpKeepAliveTime * 1000)), 0, keepAlive, 4, 4);  
                Buffer.BlockCopy(BitConverter.GetBytes((uint)(_Keepalive.TcpKeepAliveInterval * 1000)), 0, keepAlive, 8, 4);  
                _Client.Client.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);

#elif NETSTANDARD

#endif
            }
            catch (Exception)
            {
                _Settings.Logger?.Invoke(Severity.Error, _Header + "keepalives not supported on this platform, disabled");
                _Keepalive.EnableTcpKeepAlives = false;
            }
        }

        #endregion

        #region Read

        private async Task DataReceiver()
        {
            DisconnectReason reason = DisconnectReason.Normal;

            while (true)
            {
                try
                {
                    #region Check-for-Connection

                    if (_Client == null || !_Client.Connected)
                    {
                        _Settings?.Logger?.Invoke(Severity.Debug, _Header + "disconnect detected");
                        break;
                    }

                    #endregion

                    #region Read-Message

                    await _ReadLock.WaitAsync(_Token);
                    WatsonMessage msg = new WatsonMessage(_DataStream, (_Settings.DebugMessages ? _Settings.Logger : null));
                    bool buildSuccess = await msg.BuildFromStream(_Token).ConfigureAwait(false);
                    if (!buildSuccess)
                    {
                        _Settings?.Logger?.Invoke(Severity.Debug, _Header + "disconnect detected");
                        break;
                    }

                    if (msg == null)
                    { 
                        await Task.Delay(30, _Token).ConfigureAwait(false);
                        continue;
                    }

                    _LastActivity = DateTime.Now;

                    #endregion

                    #region Process-by-Status

                    if (msg.Status == MessageStatus.Removed)
                    {
                        _Settings?.Logger?.Invoke(Severity.Info, _Header + "disconnect due to server-side removal");
                        reason = DisconnectReason.Removed;
                        break;
                    }
                    else if (msg.Status == MessageStatus.Shutdown)
                    {
                        _Settings?.Logger?.Invoke(Severity.Info, _Header + "disconnect due to server shutdown");
                        reason = DisconnectReason.Shutdown;
                        break;
                    }
                    else if (msg.Status == MessageStatus.Timeout)
                    {
                        _Settings?.Logger?.Invoke(Severity.Info, _Header + "disconnect due to timeout");
                        reason = DisconnectReason.Timeout;
                        break;
                    }
                    else if (msg.Status == MessageStatus.AuthSuccess)
                    {
                        _Settings.Logger?.Invoke(Severity.Debug, _Header + "authentication successful");
                        Task unawaited = Task.Run(() => _Events.HandleAuthenticationSucceeded(this, EventArgs.Empty), _Token);
                        continue;
                    }
                    else if (msg.Status == MessageStatus.AuthFailure)
                    {
                        _Settings.Logger?.Invoke(Severity.Error, _Header + "authentication failed");
                        reason = DisconnectReason.AuthFailure;
                        Task unawaited = Task.Run(() => _Events.HandleAuthenticationFailure(this, EventArgs.Empty), _Token);
                        break;
                    }
                    else if (msg.Status == MessageStatus.AuthRequired)
                    {
                        _Settings.Logger?.Invoke(Severity.Info, _Header + "authentication required by server; please authenticate using pre-shared key"); 
                        string psk = _Callbacks.HandleAuthenticationRequested();
                        if (!String.IsNullOrEmpty(psk)) Authenticate(psk);
                        continue;
                    }

                    #endregion

                    #region Process-Message

                    if (msg.SyncRequest != null && msg.SyncRequest.Value)
                    { 
                        DateTime expiration = WatsonCommon.GetExpirationTimestamp(msg);
                        byte[] msgData = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize).ConfigureAwait(false); 
                         
                        if (DateTime.Now < expiration)
                        { 
                            SyncRequest syncReq = new SyncRequest(
                                _ServerIp + ":" + _ServerPort,
                                msg.ConversationGuid,
                                msg.Expiration.Value,
                                msg.Metadata,
                                msgData);
                                 
                            SyncResponse syncResp = _Callbacks.HandleSyncRequestReceived(syncReq);
                            if (syncResp != null)
                            { 
                                WatsonCommon.BytesToStream(syncResp.Data, 0, out int contentLength, out Stream stream);
                                WatsonMessage respMsg = new WatsonMessage( 
                                    syncResp.Metadata,
                                    contentLength,
                                    stream,
                                    false,
                                    true,
                                    msg.Expiration.Value,
                                    msg.ConversationGuid,  
                                    (_Settings.DebugMessages ? _Settings.Logger : null)); 
                                SendInternal(respMsg, contentLength, stream);
                            }
                        }
                        else
                        { 
                            _Settings.Logger?.Invoke(Severity.Debug, _Header + "expired synchronous request received and discarded");
                        } 
                    }
                    else if (msg.SyncResponse != null && msg.SyncResponse.Value)
                    { 
                        // No need to amend message expiration; it is copied from the request, which was set by this node
                        // DateTime expiration = WatsonCommon.GetExpirationTimestamp(msg); 
                        byte[] msgData = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize).ConfigureAwait(false);

                        if (DateTime.Now < msg.Expiration.Value)
                        {
                            lock (_SyncResponseLock)
                            {
                                _SyncResponses.Add(msg.ConversationGuid, new SyncResponse(msg.Expiration.Value, msg.Metadata, msgData));
                            }
                        }
                        else
                        {
                            _Settings.Logger?.Invoke(Severity.Debug, _Header + "expired synchronous response received and discarded");
                        }
                    }
                    else
                    {
                        byte[] msgData = null;

                        if (_Events.IsUsingMessages)
                        { 
                            msgData = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize).ConfigureAwait(false); 
                            MessageReceivedEventArgs args = new MessageReceivedEventArgs((_ServerIp + ":" + _ServerPort), msg.Metadata, msgData);
                            await Task.Run(() => _Events.HandleMessageReceived(this, args));
                        }
                        else if (_Events.IsUsingStreams)
                        {
                            StreamReceivedEventArgs sr = null;
                            WatsonStream ws = null;

                            if (msg.ContentLength >= _Settings.MaxProxiedStreamSize)
                            {
                                ws = new WatsonStream(msg.ContentLength, msg.DataStream);
                                sr = new StreamReceivedEventArgs((_ServerIp + ":" + _ServerPort), msg.Metadata, msg.ContentLength, ws); 
                                _Events.HandleStreamReceived(this, sr);
                            }
                            else
                            {
                                MemoryStream ms = WatsonCommon.DataStreamToMemoryStream(msg.ContentLength, msg.DataStream, _Settings.StreamBufferSize);
                                ws = new WatsonStream(msg.ContentLength, ms);
                                sr = new StreamReceivedEventArgs((_ServerIp + ":" + _ServerPort), msg.Metadata, msg.ContentLength, ws); 
                                Task unawaited = Task.Run(() => _Events.HandleStreamReceived(this, sr), _Token);
                            } 
                        }
                        else
                        {
                            _Settings.Logger?.Invoke(Severity.Error, _Header + "event handler not set for either MessageReceived or StreamReceived");
                            break;
                        }
                    }

                    #endregion

                    _Statistics.IncrementReceivedMessages();
                    _Statistics.AddReceivedBytes(msg.ContentLength);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Settings?.Logger?.Invoke(Severity.Error,
                        _Header + "data receiver exception for " + _ServerIp + ":" + _ServerPort + ":" + Environment.NewLine + SerializationHelper.SerializeJson(e, true) + Environment.NewLine);
                    _Events?.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                    break;
                } 
                finally
                {
                    if (_ReadLock != null) _ReadLock.Release();
                }
            }

            Connected = false;

            if (_IsTimeout) reason = DisconnectReason.Timeout;

            _Settings?.Logger?.Invoke(Severity.Debug, _Header + "data receiver terminated for " + _ServerIp + ":" + _ServerPort);
            _Events?.HandleServerDisconnected(this, new DisconnectionEventArgs((_ServerIp + ":" + _ServerPort), reason));
        }

        #endregion

        #region Send

        private bool SendInternal(WatsonMessage msg, long contentLength, Stream stream)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (!Connected) return false;

            if (contentLength > 0 && (stream == null || !stream.CanRead))
            {
                throw new ArgumentException("Cannot read from supplied stream.");
            }

            bool disconnectDetected = false;
             
            if (_Client == null
                || !_Client.Connected)
            {
                disconnectDetected = true;
                return false;
            }
                  
            _WriteLock.Wait();

            try
            { 
                //SendHeaders(msg); 
                SendDataStream(contentLength, stream);

                _Statistics.IncrementSentMessages();
                _Statistics.AddSentBytes(contentLength);
                return true;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(Severity.Error,
                    _Header + "failed to write message to " + _ServerIp + ":" + _ServerPort + ":" +
                    Environment.NewLine +
                    SerializationHelper.SerializeJson(e, true));

                _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));

                disconnectDetected = true;
                return false;
            }
            finally
            {
                _WriteLock.Release();

                if (disconnectDetected)
                {
                    Connected = false;
                    Dispose();
                }
            } 
        }

        private async Task<bool> SendInternalAsync(WatsonMessage msg, long contentLength, Stream stream, CancellationToken token)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (!Connected) return false;
            
            if (contentLength > 0  && (stream == null || !stream.CanRead))
            {
                throw new ArgumentException("Cannot read from supplied stream.");
            }

            if (token == default(CancellationToken))
            {
                CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _Token);
                token = linkedCts.Token;
            }

            bool disconnectDetected = false;
             
            if (_Client == null || !_Client.Connected)
            {
                disconnectDetected = true;
                return false;
            }
                 
            await _WriteLock.WaitAsync(token).ConfigureAwait(false);

            try
            { 
                //await SendHeadersAsync(msg, token).ConfigureAwait(false); 
                await SendDataStreamAsync(contentLength, stream, token).ConfigureAwait(false);

                _Statistics.IncrementSentMessages();
                _Statistics.AddSentBytes(contentLength);
                return true;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(Severity.Error,
                    _Header + "failed to write message to " + _ServerIp + ":" + _ServerPort + ":" +
                    Environment.NewLine +
                    e.ToString() +
                    Environment.NewLine);

                disconnectDetected = true;
                return false;
            }
            finally
            {
                _WriteLock.Release(); 

                if (disconnectDetected)
                {
                    Connected = false;
                    Dispose();
                }
            }
        }
         
        private SyncResponse SendAndWaitInternal(WatsonMessage msg, int timeoutMs, long contentLength, Stream stream)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg)); 
            if (!Connected) throw new InvalidOperationException("Client is not connected to the server.");

            if (contentLength > 0 && (stream == null || !stream.CanRead))
            {
                throw new ArgumentException("Cannot read from supplied stream.");
            }

            bool disconnectDetected = false;
            
            if (_Client == null || !_Client.Connected)
            {
                disconnectDetected = true;
                throw new InvalidOperationException("Client is not connected to the server.");
            }
              
            _WriteLock.Wait(); 

            try
            {
                //SendHeaders(msg);
                SendDataStream(contentLength, stream);

                _Statistics.IncrementSentMessages();
                _Statistics.AddSentBytes(contentLength);
            }
            catch (TaskCanceledException)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(Severity.Error,
                    _Header + "failed to write message to " + _ServerIp + ":" + _ServerPort + ":" +
                    Environment.NewLine +
                    e.ToString() +
                    Environment.NewLine);

                disconnectDetected = true;
                throw;
            }
            finally
            {
                _WriteLock.Release();

                if (disconnectDetected)
                {
                    Connected = false;
                    Dispose();
                }
            } 

            SyncResponse ret = GetSyncResponse(msg.ConversationGuid, msg.Expiration.Value); 
            return ret;
        }

        private void SendHeaders(WatsonMessage msg)
        {
            byte[] headerBytes = msg.HeaderBytes; 
            _DataStream.Write(headerBytes, 0, headerBytes.Length);
            _DataStream.Flush();
        }

        private async Task SendHeadersAsync(WatsonMessage msg, CancellationToken token)
        {
            byte[] headerBytes = msg.HeaderBytes; 
            await _DataStream.WriteAsync(headerBytes, 0, headerBytes.Length, token).ConfigureAwait(false);
            await _DataStream.FlushAsync(token).ConfigureAwait(false);
        }
         
        private void SendDataStream(long contentLength, Stream stream)
        {
            if (contentLength <= 0) return;
             
            long bytesRemaining = contentLength;
            int bytesRead = 0;
            byte[] buffer = new byte[_Settings.StreamBufferSize];
              
            while (bytesRemaining > 0)
            { 
                bytesRead = stream.Read(buffer, 0, buffer.Length);  
                if (bytesRead > 0)
                {
                    _DataStream.Write(buffer, 0, bytesRead);
                    bytesRemaining -= bytesRead;
                } 
            } 

            _DataStream.Flush(); 
        }

        private async Task SendDataStreamAsync(long contentLength, Stream stream, CancellationToken token)
        {
            if (contentLength <= 0) return;

            long bytesRemaining = contentLength;
            int bytesRead = 0;
            byte[] buffer = new byte[_Settings.StreamBufferSize];
             
            while (bytesRemaining > 0)
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                if (bytesRead > 0)
                {
                    await _DataStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                    bytesRemaining -= bytesRead;
                }
            }  

            await _DataStream.FlushAsync(token).ConfigureAwait(false);
        }

        #endregion

        #region Tasks

        private async Task MonitorForExpiredSyncResponses()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(1000, _Token).ConfigureAwait(false);

                    lock (_SyncResponseLock)
                    {
                        if (_SyncResponses.Any(s =>
                            s.Value.ExpirationUtc < DateTime.Now
                            ))
                        {
                            Dictionary<string, SyncResponse> expired = _SyncResponses.Where(s =>
                                s.Value.ExpirationUtc < DateTime.Now
                                ).ToDictionary(dict => dict.Key, dict => dict.Value);

                            foreach (KeyValuePair<string, SyncResponse> curr in expired)
                            {
                                _Settings.Logger?.Invoke(Severity.Debug, _Header + "expiring response " + curr.Key.ToString());
                                _SyncResponses.Remove(curr.Key);
                            }
                        }
                    } 
                }
            }
            catch (TaskCanceledException)
            {

            }
            catch (OperationCanceledException)
            {

            }
        }

        private SyncResponse GetSyncResponse(string guid, DateTime expirationUtc)
        {
            SyncResponse ret = null;

            try
            {
                while (true)
                {
                    lock (_SyncResponseLock)
                    {
                        if (_SyncResponses.ContainsKey(guid))
                        {
                            ret = _SyncResponses[guid];
                            _SyncResponses.Remove(guid);
                            break;
                        }
                    }

                    if (DateTime.Now >= expirationUtc) break;
                    Task.Delay(50).Wait(_Token);
                }

                if (ret != null)
                {
                    return ret;
                }
                else
                {
                    _Settings.Logger?.Invoke(Severity.Error, _Header + "synchronous response not received within the timeout window");
                    throw new TimeoutException("A response to a synchronous request was not received within the timeout window.");
                }
            }
            catch (TaskCanceledException)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        private async Task IdleServerMonitor()
        {
            while (!_Token.IsCancellationRequested)
            {
                await Task.Delay(_Settings.IdleServerEvaluationIntervalMs, _Token).ConfigureAwait(false);

                if (_Settings.IdleServerTimeoutMs == 0) continue;

                DateTime timeoutTime = _LastActivity.AddMilliseconds(_Settings.IdleServerTimeoutMs);

                if (DateTime.Now > timeoutTime)
                {
                    _Settings.Logger?.Invoke(Severity.Warn, _Header + "disconnecting from " + _ServerIp + ":" + _ServerPort + " due to timeout");
                    _IsTimeout = true;
                    Disconnect();
                }
            }
        }

        #endregion

        #endregion
    }
}
