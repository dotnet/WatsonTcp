namespace WatsonTcp
{
    using System;
    using System.Buffers;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Security;
    using System.Net.Sockets;
#if NET5_0_OR_GREATER
    using System.Runtime.InteropServices;
#endif
    using System.Security.Authentication;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

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
        /// JSON serialization helper.
        /// </summary>
        public ISerializationHelper SerializationHelper
        {
            get
            {
                return _SerializationHelper;
            }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(SerializationHelper));
                _SerializationHelper = value;
                _MessageBuilder.SerializationHelper = value;
            }
        }

        /// <summary>
        /// Indicates whether or not the client is connected to the server.
        /// </summary>
        public bool Connected { get; private set; }

        #endregion

        #region Private-Members

        private string _Header = "[WatsonTcpClient] ";
        private WatsonMessageBuilder _MessageBuilder = new WatsonMessageBuilder();
        private WatsonTcpClientSettings _Settings = new WatsonTcpClientSettings();
        private WatsonTcpClientEvents _Events = new WatsonTcpClientEvents();
        private WatsonTcpClientCallbacks _Callbacks = new WatsonTcpClientCallbacks();
        private WatsonTcpStatistics _Statistics = new WatsonTcpStatistics();
        private WatsonTcpKeepaliveSettings _Keepalive = new WatsonTcpKeepaliveSettings();
        private WatsonTcpClientSslConfiguration _SslConfiguration = new WatsonTcpClientSslConfiguration();
        private ISerializationHelper _SerializationHelper = new DefaultSerializationHelper();

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
        private Task _IdleServerMonitor = null;

        private DateTime _LastActivity = DateTime.UtcNow;
        private bool _IsTimeout = false;
        private bool _TransportConnected = false;
        private bool _ServerConnectedRaised = false;
        private bool _HandshakeRequired = false;
        private TaskCompletionSource<bool> _InitializationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> _InitializationReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<Exception> _InitializationFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        private HandshakeSessionTransport _ClientHandshakeTransport = null;
        private ClientHandshakeSession _ClientHandshakeSession = null;
        private Task _ClientHandshakeTask = null;

        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<SyncResponse>> _SyncRequests = new ConcurrentDictionary<Guid, TaskCompletionSource<SyncResponse>>();

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
                X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.Exportable;

#if NET5_0_OR_GREATER
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    keyStorageFlags = X509KeyStorageFlags.EphemeralKeySet;
                }
#endif

#if NET9_0_OR_GREATER
                _SslCertificate = X509CertificateLoader.LoadPkcs12FromFile(pfxCertFile, pfxCertPass, keyStorageFlags);
#else
                if (String.IsNullOrEmpty(pfxCertPass))
                {
                    _SslCertificate = new X509Certificate2(pfxCertFile, (string)null, keyStorageFlags);
                }
                else
                {
                    _SslCertificate = new X509Certificate2(pfxCertFile, pfxCertPass, keyStorageFlags);
                }
#endif

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
            if (Connected || _TransportConnected) throw new InvalidOperationException("Already connected to the server.");

            if (_Settings.LocalPort == 0)
            {
                _Client = new TcpClient();
            }
            else
            {
                IPEndPoint ipe = new IPEndPoint(IPAddress.Any, _Settings.LocalPort);
                _Client = new TcpClient(ipe);
            }

            _Client.NoDelay = _Settings.NoDelay;
            _Statistics = new WatsonTcpStatistics();

            IAsyncResult asyncResult = null;
            WaitHandle waitHandle = null;
            bool connectSuccess = false;

            ValidateReceiveHandlerConfiguration();

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
                }
                catch (Exception e)
                {
                    _Settings.Logger?.Invoke(Severity.Error, _Header + "exception encountered: " + e.Message);
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

                    if (_Keepalive.EnableTcpKeepAlives) EnableKeepalives();
                }
                catch (Exception e)
                {
                    _Settings.Logger?.Invoke(Severity.Error, _Header + "exception encountered: " + e.Message);
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

            _TransportConnected = true;
            ResetInitializationState();

            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;
            _MessageBuilder.MaxHeaderSize = _Settings.MaxHeaderSize;

            _LastActivity = DateTime.UtcNow;
            _IsTimeout = false;

            _DataReceiver = Task.Run(() => DataReceiver(_Token), _Token);
            _IdleServerMonitor = Task.Run(() => IdleServerMonitor(_Token), _Token);

            WatsonMessage msg = new WatsonMessage();
            msg.Status = MessageStatus.RegisterClient;

            if (!SendInternalAsync(msg, 0, null, default(CancellationToken), true).Result)
            {
                Exception initException = null;
                if (_InitializationFailure.Task.IsCompleted && !_InitializationFailure.Task.IsCanceled && !_InitializationFailure.Task.IsFaulted)
                {
                    initException = _InitializationFailure.Task.Result;
                }

                CloseTransport(false);
                if (initException != null) throw initException;

                _Settings.Logger?.Invoke(Severity.Alert, _Header + "unable to register GUID " + _Settings.Guid + " with the server");
                throw new ArgumentException("Server rejected GUID " + _Settings.Guid);
            }

            CompleteConnectionInitialization();
        }

        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        /// <param name="sendNotice">Flag to indicate whether the server should be notified of the disconnect.  This message will not be sent until other send requests have been handled.</param>
        public void Disconnect(bool sendNotice = true)
        {
            if (!Connected && !_TransportConnected) throw new InvalidOperationException("Not connected to the server.");

            _Settings.Logger?.Invoke(Severity.Info, _Header + "disconnecting from " + _ServerIp + ":" + _ServerPort);

            if (_TransportConnected && sendNotice)
            {
                WatsonMessage msg = new WatsonMessage();
                msg.Status = MessageStatus.Shutdown;
                SendInternalAsync(msg, 0, null, default(CancellationToken), true).Wait();
            }

            CloseTransport(true);
            _Settings.Logger?.Invoke(Severity.Info, _Header + "disconnected from " + _ServerIp + ":" + _ServerPort);
        }

        /// <summary>
        /// Send a pre-shared key to the server to authenticate.
        /// </summary>
        /// <param name="presharedKey">Up to 16-character string.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        public async Task AuthenticateAsync(string presharedKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(presharedKey)) throw new ArgumentNullException(nameof(presharedKey));
            if (presharedKey.Length != 16) throw new ArgumentException("Preshared key length must be 16 bytes.");

            WatsonMessage msg = new WatsonMessage();
            msg.Status = MessageStatus.AuthRequested;
            msg.PresharedKey = Encoding.UTF8.GetBytes(presharedKey);
            await SendInternalAsync(msg, 0, null, token, true).ConfigureAwait(false);
        }

        #region SendAsync

        /// <summary>
        /// Send data and metadata to the server asynchronously.
        /// </summary>
        /// <param name="data">String containing data.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string data, Dictionary<string, object> metadata = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(data)) return await SendAsync(Array.Empty<byte>(), metadata, 0, token);
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
        public async Task<bool> SendAsync(byte[] data, Dictionary<string, object> metadata = null, int start = 0, CancellationToken token = default)
        {
            if (token == default(CancellationToken)) token = _Token;
            if (data == null) data = Array.Empty<byte>();
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
        public async Task<bool> SendAsync(long contentLength, Stream stream, Dictionary<string, object> metadata = null, CancellationToken token = default)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (token == default(CancellationToken)) token = _Token;
            if (stream == null) stream = new MemoryStream(Array.Empty<byte>());
            WatsonMessage msg = _MessageBuilder.ConstructNew(contentLength, stream, false, false, null, metadata);
            return await SendInternalAsync(msg, contentLength, stream, token).ConfigureAwait(false);
        }

        #endregion

        #region SendAndWaitAsync

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="data">Data to send.</param>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="start">Start position within the supplied array.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        /// <returns>SyncResponse.</returns>
        public async Task<SyncResponse> SendAndWaitAsync(int timeoutMs, string data, Dictionary<string, object> metadata = null, int start = 0, CancellationToken token = default)
        {
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (String.IsNullOrEmpty(data)) return await SendAndWaitAsync(timeoutMs, Array.Empty<byte>(), metadata, start, token);
            return await SendAndWaitAsync(timeoutMs, Encoding.UTF8.GetBytes(data), metadata, start, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="data">Data to send.</param>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="start">Start position within the supplied array.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        /// <returns>SyncResponse.</returns>
        public async Task<SyncResponse> SendAndWaitAsync(int timeoutMs, byte[] data, Dictionary<string, object> metadata = null, int start = 0, CancellationToken token = default)
        {
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (data == null) data = Array.Empty<byte>();
            WatsonCommon.BytesToStream(data, start, out int contentLength, out Stream stream);
            return await SendAndWaitAsync(timeoutMs, contentLength, stream, metadata, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="contentLength">The number of bytes to send from the supplied stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        /// <returns>SyncResponse.</returns>
        public async Task<SyncResponse> SendAndWaitAsync(int timeoutMs, long contentLength, Stream stream, Dictionary<string, object> metadata = null, CancellationToken token = default)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (stream == null) stream = new MemoryStream(Array.Empty<byte>());
            DateTime expiration = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            WatsonMessage msg = _MessageBuilder.ConstructNew(contentLength, stream, true, false, expiration, metadata);
            return await SendAndWaitInternalAsync(msg, timeoutMs, contentLength, stream, token).ConfigureAwait(false);
        }

        #endregion

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

                if (Connected || _TransportConnected) CloseTransport(true);

                if (_SslCertificate != null)
                    _SslCertificate.Dispose();

                if (_WriteLock != null)
                    _WriteLock.Dispose();

                if (_ReadLock != null)
                    _ReadLock.Dispose();

                Settings = null;
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
            }
        }

        private void ResetInitializationState()
        {
            _ServerConnectedRaised = false;
            _HandshakeRequired = false;
            _InitializationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _InitializationReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _InitializationFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

            _ClientHandshakeTransport?.Dispose();
            _ClientHandshakeTransport = null;
            _ClientHandshakeSession = null;
            _ClientHandshakeTask = null;
        }

        private void CompleteConnectionInitialization()
        {
            Task negotiationWindow = Task.Delay(50);
            int winner = Task.WaitAny(
                _InitializationFailure.Task,
                _InitializationReady.Task,
                _InitializationStarted.Task,
                negotiationWindow);

            if (winner == 0)
            {
                CloseTransport(false);
                throw _InitializationFailure.Task.Result;
            }

            if (winner == 1)
            {
                MarkConnected();
                return;
            }

            if (winner == 2)
            {
                int result = Task.WaitAny(_InitializationFailure.Task, _InitializationReady.Task, Task.Delay(_Settings.HandshakeTimeoutMs));
                if (result == 0)
                {
                    CloseTransport(false);
                    throw _InitializationFailure.Task.Result;
                }

                if (result == 1)
                {
                    MarkConnected();
                    return;
                }

                HandshakeFailedException timeoutException = new HandshakeFailedException("Connection initialization timed out.");
                _InitializationFailure.TrySetResult(timeoutException);
                CloseTransport(false);
                throw timeoutException;
            }

            MarkConnected();
        }

        private void MarkConnected()
        {
            if (Connected) return;

            Connected = true;
            _Events.HandleServerConnected(this, new ConnectionEventArgs());
            _ServerConnectedRaised = true;
            _Settings.Logger?.Invoke(Severity.Info, _Header + "connected to " + _ServerIp + ":" + _ServerPort);
        }

        private void CloseTransport(bool waitForBackgroundTasks)
        {
            _TransportConnected = false;
            Connected = false;

            if (_TokenSource != null)
            {
                try
                {
                    if (!_TokenSource.IsCancellationRequested)
                    {
                        _TokenSource.Cancel();
                    }
                }
                catch (ObjectDisposedException)
                {
                }

                _Token = default(CancellationToken);
            }

            try
            {
                _SslStream?.Close();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                _TcpStream?.Close();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                _Client?.Close();
            }
            catch (ObjectDisposedException)
            {
            }

            if (waitForBackgroundTasks)
            {
                try
                {
                    if (_DataReceiver != null && !_DataReceiver.IsCompleted)
                        _DataReceiver.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException) { }
                catch (ObjectDisposedException) { }

                try
                {
                    if (_IdleServerMonitor != null && !_IdleServerMonitor.IsCompleted)
                        _IdleServerMonitor.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException) { }
                catch (ObjectDisposedException) { }
            }
        }

        private async Task SendRegisterMessageAsync(CancellationToken token)
        {
            WatsonMessage registerMsg = new WatsonMessage();
            registerMsg.Status = MessageStatus.RegisterClient;
            await SendInternalAsync(registerMsg, 0, null, token, true).ConfigureAwait(false);
        }

        private async Task SendStatusMessageAsync(MessageStatus status, string reason, CancellationToken token)
        {
            byte[] data = Array.Empty<byte>();
            if (!String.IsNullOrEmpty(reason)) data = Encoding.UTF8.GetBytes(reason);
            WatsonCommon.BytesToStream(data, 0, out int contentLength, out Stream stream);
            WatsonMessage msg = _MessageBuilder.ConstructNew(contentLength, stream, false, false, null, null);
            msg.Status = status;
            await SendInternalAsync(msg, contentLength, stream, token, true).ConfigureAwait(false);
        }

        private async Task<string> ReadStatusMessageAsync(WatsonMessage msg, CancellationToken token)
        {
            if (msg == null || msg.ContentLength <= 0) return null;
            byte[] data = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize, token).ConfigureAwait(false);
            if (data == null || data.Length < 1) return null;
            return Encoding.UTF8.GetString(data);
        }

        private async Task SendHandshakeDataAsync(HandshakeMessage handshakeMessage, CancellationToken token)
        {
            if (handshakeMessage == null) throw new ArgumentNullException(nameof(handshakeMessage));

            byte[] data = Encoding.UTF8.GetBytes(SerializationHelper.SerializeJson(handshakeMessage, false));
            WatsonCommon.BytesToStream(data, 0, out int contentLength, out Stream stream);
            WatsonMessage msg = _MessageBuilder.ConstructNew(contentLength, stream, false, false, null, null);
            msg.Status = MessageStatus.HandshakeData;
            await SendInternalAsync(msg, contentLength, stream, token, true).ConfigureAwait(false);
        }

        private async Task<HandshakeMessage> ReadHandshakeMessageAsync(WatsonMessage msg, CancellationToken token)
        {
            if (msg == null) return null;
            if (msg.ContentLength <= 0) return new HandshakeMessage();

            byte[] data = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize, token).ConfigureAwait(false);
            if (data == null || data.Length < 1) return new HandshakeMessage();
            return SerializationHelper.DeserializeJson<HandshakeMessage>(Encoding.UTF8.GetString(data));
        }

        private void FailInitialization(Exception exception)
        {
            if (exception == null) return;
            _InitializationFailure.TrySetResult(exception);
        }

        private void StartClientHandshake(CancellationToken token)
        {
            if (_ClientHandshakeTask != null) return;

            _ClientHandshakeTransport = new HandshakeSessionTransport(
                async (msg, innerToken) => await SendHandshakeDataAsync(msg, innerToken).ConfigureAwait(false),
                async (reason, status, innerToken) => await SendStatusMessageAsync(status, reason, innerToken).ConfigureAwait(false),
                token);
            _ClientHandshakeSession = new ClientHandshakeSession(_ClientHandshakeTransport);
            _ClientHandshakeTask = Task.Run(() => RunClientHandshakeAsync(token), token);
        }

        private async Task RunClientHandshakeAsync(CancellationToken token)
        {
            HandshakeResult result = null;

            using (CancellationTokenSource timeoutCts = new CancellationTokenSource(_Settings.HandshakeTimeoutMs))
            using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
            {
                try
                {
                    result = await _Callbacks.HandshakeAsync(_ClientHandshakeSession, linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (timeoutCts.IsCancellationRequested)
                    {
                        result = HandshakeResult.Fail("Handshake timed out.");
                    }
                    else
                    {
                        result = HandshakeResult.Fail("Handshake canceled.");
                    }
                }
                catch (Exception e)
                {
                    _Settings.Logger?.Invoke(Severity.Error, _Header + "handshake exception: " + e.Message);
                    _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                    result = HandshakeResult.Fail("Handshake failed: " + e.Message);
                }
            }

            if (result == null) result = HandshakeResult.Succeed();
            if (result.Success) return;

            HandshakeFailedException exception = new HandshakeFailedException(result.Reason, result.FailureStatus);
            _Events.HandleHandshakeFailed(this, new HandshakeFailedEventArgs(null, result.Reason, result.FailureStatus));
            FailInitialization(exception);
            await SendStatusMessageAsync(result.FailureStatus, result.Reason, token).ConfigureAwait(false);
            CloseTransport(false);
        }

        #region Connection

        private void EnableKeepalives()
        {
            // issues with definitions: https://github.com/dotnet/sdk/issues/14540

            try
            {
#if NET6_0_OR_GREATER

                _Client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _Keepalive.TcpKeepAliveTime);
                _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _Keepalive.TcpKeepAliveInterval);

                // Windows 10 version 1703 or later

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    && Environment.OSVersion.Version >= new Version(10, 0, 15063))
                {
                    _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _Keepalive.TcpKeepAliveRetryCount);
                }

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

        private void ValidateReceiveHandlerConfiguration()
        {
            bool usingMessages = _Events.IsUsingMessages;
            bool usingSyncStreams = _Events.IsUsingStreams;
            bool usingAsyncStreams = _Callbacks.StreamReceivedAsync != null;

            if (!usingMessages && !usingSyncStreams && !usingAsyncStreams)
            {
                throw new InvalidOperationException("One of either 'MessageReceived', 'StreamReceived', or 'Callbacks.StreamReceivedAsync' must first be set.");
            }

            if (usingMessages && usingAsyncStreams)
            {
                _Settings.Logger?.Invoke(Severity.Warn, _Header + "MessageReceived and Callbacks.StreamReceivedAsync are both configured; MessageReceived will be used.");
            }

            if (usingMessages && usingSyncStreams)
            {
                _Settings.Logger?.Invoke(Severity.Warn, _Header + "MessageReceived and StreamReceived are both configured; MessageReceived will be used.");
            }

            if (!usingMessages && usingAsyncStreams && usingSyncStreams)
            {
                _Settings.Logger?.Invoke(Severity.Warn, _Header + "Callbacks.StreamReceivedAsync and StreamReceived are both configured; Callbacks.StreamReceivedAsync will be used.");
            }
        }

        private async Task HandleStreamPayloadAsync(WatsonMessage msg, CancellationToken token)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));

            bool useAsyncCallback = _Callbacks.StreamReceivedAsync != null;
            bool useSyncEvent = _Events.IsUsingStreams;

            if (!useAsyncCallback && !useSyncEvent)
            {
                throw new InvalidOperationException("Receive handler not set for MessageReceived, StreamReceived, or Callbacks.StreamReceivedAsync.");
            }

            bool useBufferedStream = msg.ContentLength < _Settings.MaxProxiedStreamSize;
            Stream payloadStream = msg.DataStream;

            if (useBufferedStream)
            {
                payloadStream = await WatsonCommon.DataStreamToMemoryStream(msg.ContentLength, msg.DataStream, _Settings.StreamBufferSize, token).ConfigureAwait(false);
            }

            WatsonStream watsonStream = new WatsonStream(msg.ContentLength, payloadStream);
            StreamReceivedEventArgs args = new StreamReceivedEventArgs(null, msg.Metadata, msg.ContentLength, watsonStream);
            bool preserveOriginalException = false;

            try
            {
                if (useAsyncCallback)
                {
                    await _Callbacks.StreamReceivedAsync(args, token).ConfigureAwait(false);
                }
                else if (useBufferedStream)
                {
                    await Task.Run(() => _Events.HandleStreamReceived(this, args), token).ConfigureAwait(false);
                }
                else
                {
                    _Events.HandleStreamReceived(this, args);
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                preserveOriginalException = true;
                _Settings.Logger?.Invoke(Severity.Error, _Header + "stream receive handler exception for " + _ServerIp + ":" + _ServerPort + ": " + e.Message);
                _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                throw;
            }
            finally
            {
                if (watsonStream.RemainingBytes > 0 && !token.IsCancellationRequested)
                {
                    try
                    {
                        await watsonStream.DrainAsync(_Settings.StreamBufferSize, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (preserveOriginalException)
                    {
                        _Settings.Logger?.Invoke(Severity.Error, _Header + "failed draining unread stream bytes after handler exception: " + e.Message);
                    }
                }
            }
        }

        #endregion

        #region Read

        private async Task DataReceiver(CancellationToken token)
        {
            DisconnectReason reason = DisconnectReason.Normal;

            while (true)
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    #region Check-for-Connection

                    if (_Client == null || !_Client.Connected)
                    {
                        _Settings?.Logger?.Invoke(Severity.Debug, _Header + "disconnect detected");
                        break;
                    }

                    #endregion

                    #region Read-Message

                    await _ReadLock.WaitAsync(token);
                    WatsonMessage msg = await _MessageBuilder.BuildFromStream(_DataStream, token);
                    if (msg == null)
                    {
                        await Task.Delay(30, token).ConfigureAwait(false);
                        continue;
                    }

                    _LastActivity = DateTime.UtcNow;

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
                    else if (msg.Status == MessageStatus.ConnectionRejected)
                    {
                        string rejectionReason = await ReadStatusMessageAsync(msg, token).ConfigureAwait(false);
                        if (String.IsNullOrEmpty(rejectionReason)) rejectionReason = "Connection rejected.";
                        _Settings.Logger?.Invoke(Severity.Error, _Header + rejectionReason);
                        reason = DisconnectReason.ConnectionRejected;
                        ConnectionRejectedException exception = new ConnectionRejectedException(rejectionReason, MessageStatus.ConnectionRejected);
                        _Events.HandleConnectionRejected(this, new ConnectionRejectedEventArgs(null, rejectionReason, MessageStatus.ConnectionRejected));
                        FailInitialization(exception);
                        break;
                    }
                    else if (msg.Status == MessageStatus.AuthSuccess)
                    {
                        await ReadStatusMessageAsync(msg, token).ConfigureAwait(false);
                        _Settings.Logger?.Invoke(Severity.Debug, _Header + "authentication successful");
                        _InitializationStarted.TrySetResult(true);
                        Task unawaited = Task.Run(() => _Events.HandleAuthenticationSucceeded(this, EventArgs.Empty), token);
                        await SendRegisterMessageAsync(token).ConfigureAwait(false);
                        if (!_HandshakeRequired)
                        {
                            _InitializationReady.TrySetResult(true);
                        }

                        continue;
                    }
                    else if (msg.Status == MessageStatus.AuthFailure)
                    {
                        string authFailureReason = await ReadStatusMessageAsync(msg, token).ConfigureAwait(false);
                        if (String.IsNullOrEmpty(authFailureReason)) authFailureReason = "Authentication failed.";
                        _Settings.Logger?.Invoke(Severity.Error, _Header + authFailureReason);
                        reason = DisconnectReason.AuthFailure;
                        Task unawaited = Task.Run(() => _Events.HandleAuthenticationFailure(this, EventArgs.Empty), token);
                        FailInitialization(new ConnectionRejectedException(authFailureReason, MessageStatus.AuthFailure));
                        break;
                    }
                    else if (msg.Status == MessageStatus.AuthRequired)
                    {
                        await ReadStatusMessageAsync(msg, token).ConfigureAwait(false);
                        _InitializationStarted.TrySetResult(true);
                        _Settings.Logger?.Invoke(Severity.Info, _Header + "authentication required by server; please authenticate using pre-shared key");

                        string psk = null;

                        // First check if pre-shared key is set in settings
                        if (!String.IsNullOrEmpty(_Settings.PresharedKey))
                        {
                            psk = _Settings.PresharedKey;
                            _Settings.Logger?.Invoke(Severity.Debug, _Header + "using pre-shared key from settings");
                        }
                        // Otherwise, call the callback if available
                        else
                        {
                            psk = _Callbacks.HandleAuthenticationRequested();
                            if (!String.IsNullOrEmpty(psk))
                            {
                                _Settings.Logger?.Invoke(Severity.Debug, _Header + "using pre-shared key from callback");
                            }
                        }

                        if (!String.IsNullOrEmpty(psk))
                        {
                            await AuthenticateAsync(psk, token);
                        }
                        else
                        {
                            // No pre-shared key available - neither in settings nor from callback
                            _Settings.Logger?.Invoke(Severity.Error, _Header + "authentication required by server but no pre-shared key available");
                            ConnectionRejectedException exception = new ConnectionRejectedException("Server requires authentication but no pre-shared key is configured. Set Settings.PresharedKey or implement Callbacks.AuthenticationRequested.", MessageStatus.AuthRequired);
                            FailInitialization(exception);
                            throw exception;
                        }
                        continue;
                    }
                    else if (msg.Status == MessageStatus.HandshakeBegin)
                    {
                        await ReadStatusMessageAsync(msg, token).ConfigureAwait(false);
                        _InitializationStarted.TrySetResult(true);
                        _HandshakeRequired = true;

                        if (_Callbacks.HandshakeAsync == null)
                        {
                            HandshakeFailedException exception = new HandshakeFailedException("Server requested a handshake but no handshake callback is configured.");
                            _Events.HandleHandshakeFailed(this, new HandshakeFailedEventArgs(null, exception.Message, MessageStatus.HandshakeFailure));
                            FailInitialization(exception);
                            await SendStatusMessageAsync(MessageStatus.HandshakeFailure, exception.Message, token).ConfigureAwait(false);
                            reason = DisconnectReason.HandshakeFailure;
                            break;
                        }

                        StartClientHandshake(token);
                        continue;
                    }
                    else if (msg.Status == MessageStatus.HandshakeData)
                    {
                        HandshakeMessage handshakeMsg = await ReadHandshakeMessageAsync(msg, token).ConfigureAwait(false);
                        _ClientHandshakeTransport?.Enqueue(handshakeMsg);
                        continue;
                    }
                    else if (msg.Status == MessageStatus.HandshakeSuccess)
                    {
                        await ReadStatusMessageAsync(msg, token).ConfigureAwait(false);
                        _Settings.Logger?.Invoke(Severity.Debug, _Header + "handshake successful");
                        _Events.HandleHandshakeSucceeded(this, new HandshakeSucceededEventArgs());
                        await SendRegisterMessageAsync(token).ConfigureAwait(false);
                        _InitializationReady.TrySetResult(true);
                        continue;
                    }
                    else if (msg.Status == MessageStatus.HandshakeFailure)
                    {
                        string handshakeFailureReason = await ReadStatusMessageAsync(msg, token).ConfigureAwait(false);
                        if (String.IsNullOrEmpty(handshakeFailureReason)) handshakeFailureReason = "Handshake failed.";
                        _Settings.Logger?.Invoke(Severity.Error, _Header + handshakeFailureReason);
                        reason = DisconnectReason.HandshakeFailure;
                        HandshakeFailedException exception = new HandshakeFailedException(handshakeFailureReason, MessageStatus.HandshakeFailure);
                        _Events.HandleHandshakeFailed(this, new HandshakeFailedEventArgs(null, handshakeFailureReason, MessageStatus.HandshakeFailure));
                        FailInitialization(exception);
                        break;
                    }

                    #endregion

                    #region Process-Message

                    if (msg.SyncRequest)
                    {
                        _Settings.Logger?.Invoke(Severity.Debug, _Header + "synchronous request received: " + msg.ConversationGuid.ToString());

                        DateTime expiration = WatsonCommon.GetExpirationTimestamp(msg);
                        byte[] msgData = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize, token).ConfigureAwait(false);

                        if (DateTime.UtcNow < expiration)
                        {
                            Task unawaited = Task.Run(async () =>
                            {
                                SyncRequest syncReq = new SyncRequest(
                                null,
                                msg.ConversationGuid,
                                msg.ExpirationUtc.Value,
                                msg.Metadata,
                                msgData);

                                SyncResponse syncResp = null;

#pragma warning disable CS0618 // Type or member is obsolete
                                if (_Callbacks.SyncRequestReceivedAsync != null)
                                {
                                    syncResp = await _Callbacks.HandleSyncRequestReceivedAsync(syncReq);
                                }
                                else if (_Callbacks.SyncRequestReceived != null)
                                {
                                    syncResp = _Callbacks.HandleSyncRequestReceived(syncReq);
                                }
#pragma warning restore CS0618 // Type or member is obsolete

                                if (syncResp != null)
                                {
                                    WatsonCommon.BytesToStream(syncResp.Data, 0, out int contentLength, out Stream stream);

                                    WatsonMessage respMsg = _MessageBuilder.ConstructNew(
                                        contentLength,
                                        stream,
                                        false,
                                        true,
                                        msg.ExpirationUtc.Value,
                                        syncResp.Metadata);

                                    respMsg.ConversationGuid = msg.ConversationGuid;
                                    await SendInternalAsync(respMsg, contentLength, stream, token).ConfigureAwait(false);
                                }
                            }, _Token);
                        }
                        else
                        {
                            _Settings.Logger?.Invoke(Severity.Debug, _Header + "expired synchronous request received and discarded");
                        }
                    }
                    else if (msg.SyncResponse)
                    {
                        // No need to amend message expiration; it is copied from the request, which was set by this node
                        // DateTime expiration = WatsonCommon.GetExpirationTimestamp(msg);
                        _Settings.Logger?.Invoke(Severity.Debug, _Header + "synchronous response received: " + msg.ConversationGuid.ToString());
                        byte[] msgData = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize, token).ConfigureAwait(false);

                        if (DateTime.UtcNow < msg.ExpirationUtc.Value)
                        {
                            TaskCompletionSource<SyncResponse> tcs;
                            if (_SyncRequests.TryRemove(msg.ConversationGuid, out tcs))
                            {
                                SyncResponse syncResp = new SyncResponse(msg.ConversationGuid, msg.ExpirationUtc.Value, msg.Metadata, msgData);
                                tcs.TrySetResult(syncResp);
                            }
                            else
                            {
                                _Settings.Logger?.Invoke(Severity.Warn, _Header + "synchronous response received for unknown conversation: " + msg.ConversationGuid.ToString());
                            }
                        }
                        else
                        {
                            _Settings.Logger?.Invoke(Severity.Debug, _Header + "expired synchronous response received and discarded");
                            TaskCompletionSource<SyncResponse> tcs;
                            _SyncRequests.TryRemove(msg.ConversationGuid, out tcs);
                        }
                    }
                    else
                    {
                        byte[] msgData = null;

                        if (_Events.IsUsingMessages)
                        {
                            msgData = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize, token).ConfigureAwait(false);
                            MessageReceivedEventArgs args = new MessageReceivedEventArgs(null, msg.Metadata, msgData);
                            await Task.Run(() => _Events.HandleMessageReceived(this, args), token);
                        }
                        else if (_Callbacks.StreamReceivedAsync != null || _Events.IsUsingStreams)
                        {
                            await HandleStreamPayloadAsync(msg, token).ConfigureAwait(false);
                        }
                        else
                        {
                            _Settings.Logger?.Invoke(Severity.Error, _Header + "receive handler not set for MessageReceived, StreamReceived, or Callbacks.StreamReceivedAsync");
                            break;
                        }
                    }

                    #endregion

                    _Statistics.IncrementReceivedMessages();
                    _Statistics.AddReceivedBytes(msg.ContentLength);
                }
                catch (ObjectDisposedException ode)
                {
                    _Settings?.Logger?.Invoke(Severity.Debug, _Header + "object disposed exception encountered");
                    _Events?.HandleExceptionEncountered(this, new ExceptionEventArgs(ode));
                    break;
                }
                catch (TaskCanceledException tce)
                {
                    _Settings?.Logger?.Invoke(Severity.Debug, _Header + "task canceled exception encountered");
                    _Events?.HandleExceptionEncountered(this, new ExceptionEventArgs(tce));
                    break;
                }
                catch (OperationCanceledException oce)
                {
                    _Settings?.Logger?.Invoke(Severity.Debug, _Header + "operation canceled exception encountered");
                    _Events?.HandleExceptionEncountered(this, new ExceptionEventArgs(oce));
                    break;
                }
                catch (IOException ioe)
                {
                    _Settings?.Logger?.Invoke(Severity.Debug, _Header + "IO exception encountered");
                    _Events?.HandleExceptionEncountered(this, new ExceptionEventArgs(ioe));
                    break;
                }
                catch (Exception e)
                {
                    _Settings?.Logger?.Invoke(Severity.Error,
                        _Header + "data receiver exception for " + _ServerIp + ":" + _ServerPort + ": " + e.Message + Environment.NewLine);
                    _Events?.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                    break;
                }
                finally
                {
                    if (_ReadLock != null) _ReadLock.Release();
                }
            }

            try
            {
                _SslStream?.Close();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                _TcpStream?.Close();
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                _Client?.Close();
            }
            catch (ObjectDisposedException)
            {
            }

            _TransportConnected = false;
            Connected = false;

            if (_IsTimeout) reason = DisconnectReason.Timeout;

            _Settings?.Logger?.Invoke(Severity.Debug, _Header + "data receiver terminated for " + _ServerIp + ":" + _ServerPort);
            if (_ServerConnectedRaised)
            {
                _Events?.HandleServerDisconnected(this, new DisconnectionEventArgs(null, reason));
            }
        }

        #endregion

        #region Send

        private async Task<bool> SendInternalAsync(WatsonMessage msg, long contentLength, Stream stream, CancellationToken token, bool allowWhilePending = false)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (!Connected && !(allowWhilePending && _TransportConnected)) return false;

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
                return false;
            }

            await _WriteLock.WaitAsync(token).ConfigureAwait(false);

            try
            {
                await SendHeadersAsync(msg, token).ConfigureAwait(false);
                await SendDataStreamAsync(contentLength, stream, token).ConfigureAwait(false);

                _Statistics.IncrementSentMessages();
                _Statistics.AddSentBytes(contentLength);
                return true;
            }
            catch (TaskCanceledException)
            {
                _Settings?.Logger?.Invoke(Severity.Debug, _Header + "send canceled");
                return false;
            }
            catch (OperationCanceledException)
            {
                _Settings?.Logger?.Invoke(Severity.Debug, _Header + "send operation canceled");
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
                    _TransportConnected = false;
                    Connected = false;
                    CloseTransport(false);
                }
            }
        }

        private async Task<SyncResponse> SendAndWaitInternalAsync(WatsonMessage msg, int timeoutMs, long contentLength, Stream stream, CancellationToken token)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (!Connected) throw new InvalidOperationException("Client is not connected to the server.");

            if (contentLength > 0 && (stream == null || !stream.CanRead))
                throw new ArgumentException("Cannot read from supplied stream.");

            bool disconnectDetected = false;

            if (_Client == null || !_Client.Connected)
            {
                disconnectDetected = true;
                throw new InvalidOperationException("Client is not connected to the server.");
            }

            // Register a TaskCompletionSource for this conversation before sending
            TaskCompletionSource<SyncResponse> tcs = new TaskCompletionSource<SyncResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _SyncRequests[msg.ConversationGuid] = tcs;

            await _WriteLock.WaitAsync(token).ConfigureAwait(false);

            try
            {
                await SendHeadersAsync(msg, token).ConfigureAwait(false);
                await SendDataStreamAsync(contentLength, stream, token).ConfigureAwait(false);
                _Settings.Logger?.Invoke(Severity.Debug, _Header + "synchronous request sent: " + msg.ConversationGuid);

                _Statistics.IncrementSentMessages();
                _Statistics.AddSentBytes(contentLength);
            }
            catch (TaskCanceledException)
            {
                _SyncRequests.TryRemove(msg.ConversationGuid, out _);
                return null;
            }
            catch (OperationCanceledException)
            {
                _SyncRequests.TryRemove(msg.ConversationGuid, out _);
                return null;
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(Severity.Error, _Header + "failed to write message to " + _ServerIp + ":" + _ServerPort + ": " + e.Message);
                _SyncRequests.TryRemove(msg.ConversationGuid, out _);
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

            // Wait for the response with timeout
            using (CancellationTokenSource timeoutCts = new CancellationTokenSource(timeoutMs))
            {
                using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
                {
                    try
                    {
                        linkedCts.Token.Register(() => tcs.TrySetCanceled());
                        SyncResponse ret = await tcs.Task.ConfigureAwait(false);
                        return ret;
                    }
                    catch (TaskCanceledException)
                    {
                        _SyncRequests.TryRemove(msg.ConversationGuid, out _);

                        if (timeoutCts.IsCancellationRequested)
                        {
                            _Settings.Logger?.Invoke(Severity.Error, _Header + "synchronous response not received within the timeout window");
                            throw new TimeoutException("A response to a synchronous request was not received within the timeout window.");
                        }

                        throw;
                    }
                }
            }
        }

        private async Task SendHeadersAsync(WatsonMessage msg, CancellationToken token)
        {
            msg.SenderGuid = _Settings.Guid;
            byte[] headerBytes = _MessageBuilder.GetHeaderBytes(msg);
            await _DataStream.WriteAsync(headerBytes, 0, headerBytes.Length, token).ConfigureAwait(false);
            await _DataStream.FlushAsync(token).ConfigureAwait(false);
        }

        private async Task SendDataStreamAsync(long contentLength, Stream stream, CancellationToken token)
        {
            if (contentLength <= 0) return;

            long bytesRemaining = contentLength;
            int bytesRead = 0;
            int bufferSize = _Settings.StreamBufferSize;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);

            try
            {
                while (bytesRemaining > 0)
                {
                    int toRead = (int)Math.Min(bufferSize, bytesRemaining);
                    bytesRead = await stream.ReadAsync(buffer, 0, toRead, token).ConfigureAwait(false);
                    if (bytesRead > 0)
                    {
                        await _DataStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                        bytesRemaining -= bytesRead;
                    }
                }

                await _DataStream.FlushAsync(token).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        #endregion

        #region Tasks

        private async Task IdleServerMonitor(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_Settings.IdleServerEvaluationIntervalMs, token).ConfigureAwait(false);

                    if (_Settings.IdleServerTimeoutMs == 0) continue;

                    DateTime timeoutTime = _LastActivity.AddMilliseconds(_Settings.IdleServerTimeoutMs);

                    if (DateTime.UtcNow > timeoutTime)
                    {
                        _Settings.Logger?.Invoke(Severity.Warn, _Header + "disconnecting from " + _ServerIp + ":" + _ServerPort + " due to timeout");
                        _IsTimeout = true;
                        Disconnect();
                    }
                }
                catch (TaskCanceledException)
                {
                    _Settings?.Logger?.Invoke(Severity.Debug, _Header + "idle server monitor task canceled");
                }
                catch (OperationCanceledException)
                {
                    _Settings?.Logger?.Invoke(Severity.Debug, _Header + "idle server monitor operation canceled");
                }
                catch (Exception e)
                {
                    _Settings.Logger?.Invoke(Severity.Warn, _Header + "exception encountered while monitoring for idle server connection: " + e.Message);
                    _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                }
            }
        }

        #endregion

        #endregion
    }
}
