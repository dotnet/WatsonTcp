namespace WatsonTcp
{
    using System;
    using System.Buffers;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Net.Security;
    using System.Net.Sockets;
#if NET5_0_OR_GREATER
    using System.Runtime.InteropServices;
#endif
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Watson TCP server, with or without SSL.
    /// </summary>
    public class WatsonTcpServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Watson TCP server settings.
        /// </summary>
        public WatsonTcpServerSettings Settings
        {
            get
            {
                return _Settings;
            }
            set
            {
                if (value == null) _Settings = new WatsonTcpServerSettings();
                else _Settings = value;
            }
        }

        /// <summary>
        /// Watson TCP server events.
        /// </summary>
        public WatsonTcpServerEvents Events
        {
            get
            {
                return _Events;
            }
            set
            {
                if (value == null) _Events = new WatsonTcpServerEvents();
                else _Events = value;
            }
        }

        /// <summary>
        /// Watson TCP server callbacks.
        /// </summary>
        public WatsonTcpServerCallbacks Callbacks
        {
            get
            {
                return _Callbacks;
            }
            set
            {
                if (value == null) _Callbacks = new WatsonTcpServerCallbacks();
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
        /// Watson TCP server SSL configuration.
        /// </summary>
        public WatsonTcpServerSslConfiguration SslConfiguration
        {
            get
            {
                return _SslConfiguration;
            }
            set
            {
                if (value == null) _SslConfiguration = new WatsonTcpServerSslConfiguration();
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
        /// Retrieve the number of current connected clients.
        /// </summary>
        public int Connections
        {
            get
            {
                return _Connections;
            }
        }

        /// <summary>
        /// Retrieve the number of pending clients that have not yet completed admission.
        /// </summary>
        public int PendingConnections
        {
            get
            {
                return _ClientManager?.PendingClientCount() ?? 0;
            }
        }

        /// <summary>
        /// Flag to indicate if Watson TCP is listening for incoming TCP connections.
        /// </summary>
        public bool IsListening
        {
            get
            {
                return _IsListening;
            }
        }

        #endregion

        #region Private-Members

        private string _Header = "[WatsonTcpServer] ";
        private WatsonMessageBuilder _MessageBuilder = new WatsonMessageBuilder();
        private WatsonTcpServerSettings _Settings = new WatsonTcpServerSettings();
        private WatsonTcpServerEvents _Events = new WatsonTcpServerEvents();
        private WatsonTcpServerCallbacks _Callbacks = new WatsonTcpServerCallbacks();
        private WatsonTcpStatistics _Statistics = new WatsonTcpStatistics();
        private WatsonTcpKeepaliveSettings _Keepalive = new WatsonTcpKeepaliveSettings();
        private WatsonTcpServerSslConfiguration _SslConfiguration = new WatsonTcpServerSslConfiguration();
        private ClientMetadataManager _ClientManager = new ClientMetadataManager();
        private ISerializationHelper _SerializationHelper = new DefaultSerializationHelper();

        private int _Connections = 0;
        private bool _IsListening = false;

        private Mode _Mode;
        private TlsVersion _TlsVersion = TlsVersion.Tls12;
        private string _ListenerIp;
        private int _ListenerPort;
        private IPAddress _ListenerIpAddress;
        private TcpListener _Listener;

        private X509Certificate2 _SslCertificate;

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private CancellationToken _Token;
        private Task _AcceptConnections = null;
        private Task _MonitorClients = null;

        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<SyncResponse>> _SyncRequests = new ConcurrentDictionary<Guid, TaskCompletionSource<SyncResponse>>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize the Watson TCP server without SSL.
        /// Supply a specific IP address on which to listen.  Otherwise, use 'null' for the IP address to listen on any IP address.
        /// If you do not specify an IP address, you may have to run WatsonTcp with administrative privileges.
        /// Call Start() afterward to start the server.
        /// </summary>
        /// <param name="listenerIp">The IP address on which the server should listen.  If null, listen on any IP address (may require administrative privileges).</param>
        /// <param name="listenerPort">The TCP port on which the server should listen.</param>
        public WatsonTcpServer(
            string listenerIp,
            int listenerPort)
        {
            if (listenerPort < 1) throw new ArgumentOutOfRangeException(nameof(listenerPort));

            _Mode = Mode.Tcp;

             // According to the https://github.com/dotnet/WatsonTcp?tab=readme-ov-file#local-vs-external-connections
            if (string.IsNullOrEmpty(listenerIp) || listenerIp.Equals("*", StringComparison.OrdinalIgnoreCase) || listenerIp.Equals("+", StringComparison.OrdinalIgnoreCase) || listenerIp.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase))
            {
                _ListenerIpAddress = IPAddress.Any;
                _ListenerIp = _ListenerIpAddress.ToString();
            }
            else if (listenerIp.Equals("localhost", StringComparison.OrdinalIgnoreCase) || listenerIp.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) || listenerIp.Equals("::1", StringComparison.OrdinalIgnoreCase))
            {
                _ListenerIpAddress = IPAddress.Loopback;
                _ListenerIp = _ListenerIpAddress.ToString();
            }
            else
            {
                _ListenerIpAddress = IPAddress.Parse(listenerIp);
                _ListenerIp = listenerIp;
            }

            _ListenerPort = listenerPort;

            SerializationHelper.InstantiateConverter(); // Unity fix
        }

        /// <summary>
        /// Initialize the Watson TCP server with SSL.
        /// Supply a specific IP address on which to listen.  Otherwise, use 'null' for the IP address to listen on any IP address.
        /// If you do not specify an IP address, you may have to run WatsonTcp with administrative privileges.
        /// Call Start() afterward to start the server.
        /// </summary>
        /// <param name="listenerIp">The IP address on which the server should listen.  If null, listen on any IP address (may require administrative privileges).</param>
        /// <param name="listenerPort">The TCP port on which the server should listen.</param>
        /// <param name="pfxCertFile">The file containing the SSL certificate.</param>
        /// <param name="pfxCertPass">The password for the SSL certificate.</param>
        /// <param name="tlsVersion">The TLS version required for client connections.</param>
        public WatsonTcpServer(
            string listenerIp,
            int listenerPort,
            string pfxCertFile,
            string pfxCertPass,
            TlsVersion tlsVersion = TlsVersion.Tls12)
        {
            if (listenerPort < 1) throw new ArgumentOutOfRangeException(nameof(listenerPort));
            if (String.IsNullOrEmpty(pfxCertFile)) throw new ArgumentNullException(nameof(pfxCertFile));

            _Mode = Mode.Ssl;
            _TlsVersion = tlsVersion;

            if (String.IsNullOrEmpty(listenerIp))
            {
                _ListenerIpAddress = IPAddress.Any;
                _ListenerIp = _ListenerIpAddress.ToString();
            }
            else if (listenerIp.Equals("localhost", StringComparison.OrdinalIgnoreCase) || listenerIp.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) || listenerIp.Equals("::1", StringComparison.OrdinalIgnoreCase))
            {
                _ListenerIpAddress = IPAddress.Loopback;
                _ListenerIp = _ListenerIpAddress.ToString();
            }
            else
            {
                _ListenerIpAddress = IPAddress.Parse(listenerIp);
                _ListenerIp = listenerIp;
            }

            _SslCertificate = null;
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

            _ListenerPort = listenerPort;

            SerializationHelper.InstantiateConverter(); // Unity fix
        }

        /// <summary>
        /// Initialize the Watson TCP server with SSL.
        /// Supply a specific IP address on which to listen.  Otherwise, use 'null' for the IP address to listen on any IP address.
        /// If you do not specify an IP address, you may have to run WatsonTcp with administrative privileges.
        /// Call Start() afterward to start the server.
        /// </summary>
        /// <param name="listenerIp">The IP address on which the server should listen.  If null, listen on any IP address (may require administrative privileges).</param>
        /// <param name="listenerPort">The TCP port on which the server should listen.</param>
        /// <param name="cert">The SSL certificate.</param>
        /// <param name="tlsVersion">The TLS version required for client connections.</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public WatsonTcpServer(
            string listenerIp,
            int listenerPort,
            X509Certificate2 cert,
            TlsVersion tlsVersion = TlsVersion.Tls12)
        {
            if (listenerPort < 1) throw new ArgumentOutOfRangeException(nameof(listenerPort));
            if (cert == null) throw new ArgumentNullException(nameof(cert));

            _Mode = Mode.Ssl;
            _TlsVersion = tlsVersion;
            _SslCertificate = cert;

            if (String.IsNullOrEmpty(listenerIp))
            {
                _ListenerIpAddress = IPAddress.Any;
                _ListenerIp = _ListenerIpAddress.ToString();
            }
            else if (listenerIp.Equals("localhost", StringComparison.OrdinalIgnoreCase) || listenerIp.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) || listenerIp.Equals("::1", StringComparison.OrdinalIgnoreCase))
            {
                _ListenerIpAddress = IPAddress.Loopback;
                _ListenerIp = _ListenerIpAddress.ToString();
            }
            else
            {
                _ListenerIpAddress = IPAddress.Parse(listenerIp);
                _ListenerIp = listenerIp;
            }

            _ListenerPort = listenerPort;

            SerializationHelper.InstantiateConverter(); // Unity fix
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tear down the server and dispose of background workers.
        /// Do not reuse the object after disposal.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Start accepting connections.
        /// </summary>
        public void Start()
        {
            if (_IsListening) throw new InvalidOperationException("WatsonTcpServer is already running.");

            _ClientManager = new ClientMetadataManager();
            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;
            _Statistics = new WatsonTcpStatistics();
            _Listener = new TcpListener(_ListenerIpAddress, _ListenerPort);

            if (!_Events.IsUsingMessages && !_Events.IsUsingStreams)
                throw new InvalidOperationException("One of either 'MessageReceived' or 'StreamReceived' events must first be set.");

            if (_Mode == Mode.Tcp)
            {
                _Settings.Logger?.Invoke(Severity.Info, _Header + "starting on " + _ListenerIp + ":" + _ListenerPort);
            }
            else if (_Mode == Mode.Ssl)
            {
                _Settings.Logger?.Invoke(Severity.Info, _Header + "starting with SSL on " + _ListenerIp + ":" + _ListenerPort);
            }
            else
            {
                throw new ArgumentException("Unknown mode: " + _Mode.ToString());
            }

            _MessageBuilder.MaxHeaderSize = _Settings.MaxHeaderSize;
            _Listener.Start();
            _AcceptConnections = Task.Run(() => AcceptConnections(_Token), _Token); // sets _IsListening
            _MonitorClients = Task.Run(() => MonitorForIdleClients(_Token), _Token);
            _Events.HandleServerStarted(this, EventArgs.Empty);
        }

        /// <summary>
        /// Stop accepting connections.
        /// </summary>
        public void Stop()
        {
            _IsListening = false;
            _Listener.Stop();
            _TokenSource.Cancel();

            _Settings.Logger?.Invoke(Severity.Info, _Header + "stopped");
            _Events.HandleServerStopped(this, EventArgs.Empty);
        }

        #region SendAsync

        /// <summary>
        /// Send data and metadata to the specified client, asynchronously.
        /// </summary>
        /// <param name="guid">Globally-unique identifier of the client.</param>
        /// <param name="data">String containing data.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="start">Start position within the supplied array.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(Guid guid, string data, Dictionary<string, object> metadata = null, int start = 0, CancellationToken token = default)
        {
            byte[] bytes = Array.Empty<byte>();
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return await SendAsync(guid, bytes, metadata, start, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data and metadata to the specified client, asynchronously.
        /// </summary>
        /// <param name="guid">Globally-unique identifier of the client.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="start">Start position within the supplied array.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(Guid guid, byte[] data, Dictionary<string, object> metadata = null, int start = 0, CancellationToken token = default)
        {
            if (data == null) data = Array.Empty<byte>();
            WatsonCommon.BytesToStream(data, start, out int contentLength, out Stream stream);
            return await SendAsync(guid, contentLength, stream, metadata, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data and metadata to the specified client using a stream, asynchronously.
        /// </summary>
        /// <param name="guid">Globally-unique identifier of the client.</param>
        /// <param name="contentLength">The number of bytes in the stream.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(Guid guid, long contentLength, Stream stream, Dictionary<string, object> metadata = null, CancellationToken token = default)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (token == default(CancellationToken)) token = _Token;
            ClientMetadata client = _ClientManager.GetClient(guid);
            if (client == null)
            {
                _Settings.Logger?.Invoke(Severity.Error, _Header + "unable to find client " + guid.ToString());
                throw new KeyNotFoundException("Unable to find client " + guid.ToString() + ".");
            }

            if (stream == null) stream = new MemoryStream(Array.Empty<byte>());
            WatsonMessage msg = _MessageBuilder.ConstructNew(contentLength, stream, false, false, null, metadata);
            return await SendInternalAsync(client, msg, contentLength, stream, token).ConfigureAwait(false);
        }

        #endregion

        #region SendAndWaitAsync

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="guid">Globally-unique identifier of the client.</param>
        /// <param name="data">Data to send.</param>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="start">Start position within the supplied array.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        /// <returns>SyncResponse.</returns>
        public async Task<SyncResponse> SendAndWaitAsync(int timeoutMs, Guid guid, string data, Dictionary<string, object> metadata = null, int start = 0, CancellationToken token = default)
        {
            byte[] bytes = Array.Empty<byte>();
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return await SendAndWaitAsync(timeoutMs, guid, bytes, metadata, start, token);
                // SendAndWaitAsync(timeoutMs, guid, bytes, metadata, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.
        /// </summary>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="guid">Globally-unique identifier of the client.</param>
        /// <param name="data">Data to send.</param>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="start">Start position within the supplied array.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        /// <returns>SyncResponse.</returns>
        public async Task<SyncResponse> SendAndWaitAsync(int timeoutMs, Guid guid, byte[] data, Dictionary<string, object> metadata = null, int start = 0, CancellationToken token = default)
        {
            if (data == null) data = Array.Empty<byte>();
            WatsonCommon.BytesToStream(data, start, out int contentLength, out Stream stream);
            return await SendAndWaitAsync(timeoutMs, guid, contentLength, stream, metadata, token);
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="guid">Globally-unique identifier of the client.</param>
        /// <param name="contentLength">The number of bytes to send from the supplied stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        /// <returns>SyncResponse.</returns>
        public async Task<SyncResponse> SendAndWaitAsync(int timeoutMs, Guid guid, long contentLength, Stream stream, Dictionary<string, object> metadata = null, CancellationToken token = default)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            ClientMetadata client = _ClientManager.GetClient(guid);
            if (client == null)
            {
                _Settings.Logger?.Invoke(Severity.Error, _Header + "unable to find client " + guid.ToString());
                throw new KeyNotFoundException("Unable to find client " + guid.ToString() + ".");
            }
            if (stream == null) stream = new MemoryStream(Array.Empty<byte>());
            DateTime expiration = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            WatsonMessage msg = _MessageBuilder.ConstructNew(contentLength, stream, true, false, expiration, metadata);
            return await SendAndWaitInternalAsync(client, msg, timeoutMs, contentLength, stream, token);
        }

        #endregion

        /// <summary>
        /// Determine whether or not the specified client is connected to the server.
        /// </summary>
        /// <param name="guid">Globally-unique identifier of the client.</param>
        /// <returns>Boolean indicating if the client is connected to the server.</returns>
        public bool IsClientConnected(Guid guid)
        {
            return _ClientManager.ExistsClient(guid);
        }

        /// <summary>
        /// Retrieve the client metadata associated with each connected client.
        /// </summary>
        /// <returns>An enumerable collection of client metadata.</returns>
        public IEnumerable<ClientMetadata> ListClients()
        {
            Dictionary<Guid, ClientMetadata> clients = _ClientManager.AllClients();
            if (clients != null && clients.Count > 0)
            {
                foreach (KeyValuePair<Guid, ClientMetadata> client in clients)
                {
                    yield return client.Value;
                }
            }
        }

        /// <summary>
        /// Disconnects the specified client.
        /// </summary>
        /// <param name="guid">Globally-unique identifier of the client.</param>
        /// <param name="status">Reason for the disconnect.  This is conveyed to the client.</param>
        /// <param name="sendNotice">Flag to indicate whether the client should be notified of the disconnect.  This message will not be sent until other send requests have been handled.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        public async Task DisconnectClientAsync(Guid guid, MessageStatus status = MessageStatus.Removed, bool sendNotice = true, CancellationToken token = default)
        {
            ClientMetadata client = _ClientManager.GetTrackedClient(guid);
            if (client == null)
            {
                _Settings.Logger?.Invoke(Severity.Error, _Header + "unable to find client " + guid.ToString());
            }
            else
            {
                if (!_ClientManager.ExistsClientTimedout(guid)) _ClientManager.AddClientKicked(guid);

                if (sendNotice)
                {
                    WatsonMessage removeMsg = new WatsonMessage();
                    removeMsg.Status = status;
                    await SendInternalAsync(client, removeMsg, 0, null, token).ConfigureAwait(false);
                }

                client.Phase = ConnectionPhase.Disconnected;
                client.Dispose();
                _ClientManager.Remove(guid);
            }
        }

        /// <summary>
        /// Disconnects all connected clients.
        /// </summary>
        /// <param name="status">Reason for the disconnect.  This is conveyed to each client.</param>
        /// <param name="sendNotice">Flag to indicate whether the client should be notified of the disconnect.  This message will not be sent until other send requests have been handled.</param>
        /// <param name="token">Cancellation token to cancel the request.</param>
        public async Task DisconnectClientsAsync(MessageStatus status = MessageStatus.Removed, bool sendNotice = true, CancellationToken token = default)
        {
            Dictionary<Guid, ClientMetadata> clients = _ClientManager.AllClients();
            if (clients != null && clients.Count > 0)
            {
                foreach (KeyValuePair<Guid, ClientMetadata> client in clients)
                {
                    await DisconnectClientAsync(client.Key, status, sendNotice, token).ConfigureAwait(false);
                }
            }

            Dictionary<Guid, ClientMetadata> pendingClients = _ClientManager.AllPendingClients();
            if (pendingClients != null && pendingClients.Count > 0)
            {
                foreach (KeyValuePair<Guid, ClientMetadata> client in pendingClients)
                {
                    await DisconnectClientAsync(client.Key, status, sendNotice, token).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Tear down the server and dispose of background workers.
        /// Do not reuse the object after disposal.
        /// </summary>
        /// <param name="disposing">Indicate if resources should be disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _Settings.Logger?.Invoke(Severity.Info, _Header + "disposing");

                if (_IsListening) Stop();

                DisconnectClientsAsync(MessageStatus.Shutdown).Wait();

                if (_Listener != null)
                {
                    if (_Listener.Server != null)
                    {
                        _Listener.Server.Close();
                        _Listener.Server.Dispose();
                    }
                }

                if (_SslCertificate != null)
                {
                    _SslCertificate.Dispose();
                }

                if (_ClientManager != null)
                {
                    _ClientManager.Dispose();
                }

                Settings = null;
                _Events = null;
                _Callbacks = null;
                _Statistics = null;
                _Keepalive = null;
                _SslConfiguration = null;

                _ListenerIp = null;
                _ListenerIpAddress = null;
                _Listener = null;

                _SslCertificate = null;

                _TokenSource = null;

                _AcceptConnections = null;
                _MonitorClients = null;

                _IsListening = false;
            }
        }

        #region Connection

        private void EnableKeepalives(TcpClient client)
        {
            // issues with definitions: https://github.com/dotnet/sdk/issues/14540

            try
            {
#if NET6_0_OR_GREATER

                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _Keepalive.TcpKeepAliveTime);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _Keepalive.TcpKeepAliveInterval);

                // Windows 10 version 1703 or later

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    && Environment.OSVersion.Version >= new Version(10, 0, 15063))
                {
                    client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _Keepalive.TcpKeepAliveRetryCount);
                }

#elif NETFRAMEWORK

                // .NET Framework expects values in milliseconds

                byte[] keepAlive = new byte[12];
                Buffer.BlockCopy(BitConverter.GetBytes((uint)1), 0, keepAlive, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes((uint)(_Keepalive.TcpKeepAliveTime * 1000)), 0, keepAlive, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes((uint)(_Keepalive.TcpKeepAliveInterval * 1000)), 0, keepAlive, 8, 4);
                client.Client.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);

#elif NETSTANDARD

#endif
            }
            catch (Exception)
            {
                _Settings.Logger?.Invoke(Severity.Error, _Header + "keepalives not supported on this platform, disabled");
                _Keepalive.EnableTcpKeepAlives = false;
            }
        }

        private async Task AcceptConnections(CancellationToken token)
        {
            _IsListening = true;

            while (true)
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    #region Check-for-Maximum-Connections

                    if (!_IsListening && (_Connections >= _Settings.MaxConnections))
                    {
                        await Task.Delay(100, token);
                        continue;
                    }
                    else if (!_IsListening)
                    {
                        _Listener.Start();
                        _IsListening = true;
                    }

                    #endregion

                    #region Accept-and-Validate

#if NET6_0_OR_GREATER
                    TcpClient tcpClient = await _Listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
#else
                    TcpClient tcpClient = await _Listener.AcceptTcpClientAsync().ConfigureAwait(false);
#endif
                    tcpClient.LingerState.Enabled = false;
                    tcpClient.NoDelay = _Settings.NoDelay;

                    // Enforce max connections - reject if at capacity
                    if (_Connections >= _Settings.MaxConnections && _Settings.EnforceMaxConnections)
                    {
                        _Settings.Logger?.Invoke(Severity.Info, _Header + "rejecting connection, maximum connections " + _Settings.MaxConnections + " reached (currently " + _Connections + " connections)");
                        tcpClient.Close();
                        continue;
                    }

                    if (_Keepalive.EnableTcpKeepAlives) EnableKeepalives(tcpClient);

                    string clientIp = ((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address.ToString();
                    if (_Settings.PermittedIPs.Count > 0 && !_Settings.PermittedIPs.Contains(clientIp))
                    {
                        _Settings.Logger?.Invoke(Severity.Info, _Header + "rejecting connection from " + clientIp + " (not permitted)");
                        tcpClient.Close();
                        continue;
                    }

                    if (_Settings.BlockedIPs.Count > 0 && _Settings.BlockedIPs.Contains(clientIp))
                    {
                        _Settings.Logger?.Invoke(Severity.Info, _Header + "rejecting connection from " + clientIp + " (blocked)");
                        tcpClient.Close();
                        continue;
                    }

                    ClientMetadata client = new ClientMetadata(tcpClient);
                    _ClientManager.AddPendingClient(client.Guid, client);

                    CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_Token, client.Token);

                    #endregion

                    #region Check-for-Maximum-Connections

                    Interlocked.Increment(ref _Connections);
                    if (_Connections >= _Settings.MaxConnections && _Settings.EnforceMaxConnections)
                    {
                        _Settings.Logger?.Invoke(Severity.Info, _Header + "maximum connections " + _Settings.MaxConnections + " met (currently " + _Connections + " connections), pausing");
                        _IsListening = false;
                        _Listener.Stop();
                    }
                    else if (_Connections >= _Settings.MaxConnections)
                    {
                        _Settings.Logger?.Invoke(Severity.Warn, _Header + "maximum connections " + _Settings.MaxConnections + " exceeded (currently " + _Connections + " connections), enforcement disabled");
                    }

                    #endregion

                    #region Initialize-Client

                    Task unawaited = null;

                    if (_Mode == Mode.Tcp)
                    {
                        unawaited = Task.Run(() => ProcessAcceptedClientAsync(client, linkedCts.Token), linkedCts.Token);
                    }
                    else if (_Mode == Mode.Ssl)
                    {
                        if (_Settings.AcceptInvalidCertificates)
                        {
                            client.SslStream = new SslStream(client.NetworkStream, false, _SslConfiguration.ClientCertificateValidationCallback);
                        }
                        else
                        {
                            client.SslStream = new SslStream(client.NetworkStream, false);
                        }

                        unawaited = Task.Run(async () =>
                        {
                            bool success = await StartTls(client, linkedCts.Token).ConfigureAwait(false);
                            if (success)
                            {
                                client.Phase = ConnectionPhase.TlsEstablished;
                                await ProcessAcceptedClientAsync(client, linkedCts.Token).ConfigureAwait(false);
                            }
                        }, linkedCts.Token);
                    }
                    else
                    {
                        throw new ArgumentException("Unknown mode: " + _Mode.ToString());
                    }

                    _Settings.Logger?.Invoke(Severity.Debug, _Header + "accepted connection from " + client.ToString());

                    #endregion
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Settings.Logger?.Invoke(Severity.Error, _Header + "listener exception: " + e.Message);
                    _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                    break;
                }
            }
        }

        private async Task<bool> StartTls(ClientMetadata client, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                await client.SslStream.AuthenticateAsServerAsync(_SslCertificate, _SslConfiguration.ClientCertificateRequired, _TlsVersion.ToSslProtocols(), !_Settings.AcceptInvalidCertificates).ConfigureAwait(false);

                if (!client.SslStream.IsEncrypted)
                {
                    _Settings.Logger?.Invoke(Severity.Error, _Header + "stream from " + client.ToString() + " not encrypted");
                    CleanupPendingClient(client);
                    return false;
                }

                if (!client.SslStream.IsAuthenticated)
                {
                    _Settings.Logger?.Invoke(Severity.Error, _Header + "stream from " + client.ToString() + " not authenticated");
                    CleanupPendingClient(client);
                    return false;
                }

                if (_Settings.MutuallyAuthenticate && !client.SslStream.IsMutuallyAuthenticated)
                {
                    _Settings.Logger?.Invoke(Severity.Error, _Header + $"mutual authentication with {client.ToString()} ({_TlsVersion}) failed");
                    CleanupPendingClient(client);
                    return false;
                }
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(Severity.Error, _Header + $"disconnected during SSL/TLS establishment with {client.ToString()} ({_TlsVersion}): " + e.Message);
                _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));

                CleanupPendingClient(client);
                return false;
            }

            return true;
        }

        private async Task ProcessAcceptedClientAsync(ClientMetadata client, CancellationToken token)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            bool authorized = await AuthorizePendingClientAsync(client, token).ConfigureAwait(false);
            if (!authorized) return;

            _Settings.Logger?.Invoke(Severity.Debug, _Header + "starting data receiver for " + client.ToString());
            client.DataReceiver = Task.Run(() => DataReceiver(client, token), token);

            if (!String.IsNullOrEmpty(_Settings.PresharedKey))
            {
                client.Phase = ConnectionPhase.PresharedKeyPending;
                _Settings.Logger?.Invoke(Severity.Debug, _Header + "requesting authentication material from " + client.ToString());
                _ClientManager.AddUnauthenticatedClient(client.Guid);
                WatsonMessage authMsg = new WatsonMessage();
                authMsg.Status = MessageStatus.AuthRequired;
                await SendInternalAsync(client, authMsg, 0, null, token).ConfigureAwait(false);
                return;
            }

            await BeginPostAuthenticationFlowAsync(client, token).ConfigureAwait(false);
        }

        private async Task<bool> AuthorizePendingClientAsync(ClientMetadata client, CancellationToken token)
        {
            if (_Callbacks.AuthorizeConnectionAsync == null) return true;

            client.Phase = ConnectionPhase.Authorizing;
            ConnectionAuthorizationResult result = null;

            using (CancellationTokenSource timeoutCts = new CancellationTokenSource(_Settings.AuthorizationTimeoutMs))
            using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
            {
                try
                {
                    X509Certificate clientCertificate = client.SslStream?.RemoteCertificate;
                    ConnectionAuthorizationContext context = new ConnectionAuthorizationContext(client, _Mode == Mode.Ssl, clientCertificate);
                    result = await _Callbacks.AuthorizeConnectionAsync(context, linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (timeoutCts.IsCancellationRequested)
                    {
                        result = ConnectionAuthorizationResult.Reject("Connection authorization timed out.");
                    }
                    else
                    {
                        result = ConnectionAuthorizationResult.Reject("Connection authorization canceled.");
                    }
                }
                catch (Exception e)
                {
                    _Settings.Logger?.Invoke(Severity.Error, _Header + "connection authorization exception for " + client.ToString() + ": " + e.Message);
                    _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                    result = ConnectionAuthorizationResult.Reject("Connection authorization failed: " + e.Message);
                }
            }

            if (result == null) result = ConnectionAuthorizationResult.Allow();
            if (result.Allowed) return true;

            await RejectPendingClientBeforeReceiverAsync(client, result.Reason, result.RejectionStatus, token).ConfigureAwait(false);
            return false;
        }

        private async Task BeginPostAuthenticationFlowAsync(ClientMetadata client, CancellationToken token)
        {
            if (_Callbacks.HandshakeAsync != null)
            {
                await StartHandshakePhaseAsync(client, token).ConfigureAwait(false);
            }
            else
            {
                client.Phase = ConnectionPhase.AwaitingRegistration;
            }
        }

        private async Task StartHandshakePhaseAsync(ClientMetadata client, CancellationToken token)
        {
            client.HandshakeRequired = true;
            client.Phase = ConnectionPhase.HandshakePending;
            client.HandshakeTransport = new HandshakeSessionTransport(
                async (msg, innerToken) => await SendHandshakeDataAsync(client, msg, innerToken).ConfigureAwait(false),
                async (reason, status, innerToken) => await SendStatusMessageAsync(client, status, reason, innerToken).ConfigureAwait(false),
                token);
            client.ServerHandshakeSession = new ServerHandshakeSession(client, client.HandshakeTransport);

            await SendStatusMessageAsync(client, MessageStatus.HandshakeBegin, "Handshake required", token).ConfigureAwait(false);
            client.HandshakeTask = Task.Run(() => RunHandshakePhaseAsync(client, token), token);
        }

        private async Task RunHandshakePhaseAsync(ClientMetadata client, CancellationToken token)
        {
            HandshakeResult result = null;

            using (CancellationTokenSource timeoutCts = new CancellationTokenSource(_Settings.HandshakeTimeoutMs))
            using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
            {
                try
                {
                    result = await _Callbacks.HandshakeAsync(client.ServerHandshakeSession, linkedCts.Token).ConfigureAwait(false);
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
                    _Settings.Logger?.Invoke(Severity.Error, _Header + "handshake exception for " + client.ToString() + ": " + e.Message);
                    _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                    result = HandshakeResult.Fail("Handshake failed: " + e.Message);
                }
            }

            if (result == null) result = HandshakeResult.Succeed();

            if (result.Success)
            {
                client.HandshakeCompleted = true;
                client.Phase = ConnectionPhase.AwaitingRegistration;
                _Events.HandleHandshakeSucceeded(this, new HandshakeSucceededEventArgs(client));
                await SendStatusMessageAsync(client, MessageStatus.HandshakeSuccess, "Handshake successful", token).ConfigureAwait(false);
            }
            else
            {
                client.HandshakeFailed = true;
                client.FailureReason = result.Reason;
                client.FailureStatus = result.FailureStatus;
                _Events.HandleHandshakeFailed(this, new HandshakeFailedEventArgs(client, result.Reason, result.FailureStatus));
                await SendStatusMessageAsync(client, result.FailureStatus, result.Reason, token).ConfigureAwait(false);
                client.Dispose();
            }
        }

        private async Task ActivateClientAsync(ClientMetadata client, Guid requestedGuid)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            _Settings.Logger?.Invoke(Severity.Debug, _Header + "client " + client.ToString() + " attempting to register GUID " + requestedGuid.ToString());
            _ClientManager.ReplaceGuid(client.Guid, requestedGuid);
            _ClientManager.RemovePendingClient(requestedGuid);
            _ClientManager.AddClient(requestedGuid, client);
            _ClientManager.AddClientLastSeen(requestedGuid);
            _Settings.Logger?.Invoke(Severity.Debug, _Header + "updated client GUID from " + client.Guid + " to " + requestedGuid);

            client.Guid = requestedGuid;
            client.Registered = true;
            client.Phase = ConnectionPhase.Connected;
            _Events.HandleClientConnected(this, new ConnectionEventArgs(client));
        }

        private void CleanupPendingClient(ClientMetadata client)
        {
            if (client == null) return;

            _ClientManager.Remove(client.Guid);
            Interlocked.Decrement(ref _Connections);
            client.Phase = ConnectionPhase.Disconnected;
            client.Dispose();
        }

        private async Task RejectPendingClientBeforeReceiverAsync(ClientMetadata client, string reason, MessageStatus status, CancellationToken token)
        {
            if (client == null) return;

            client.ConnectionRejected = true;
            client.FailureReason = String.IsNullOrEmpty(reason) ? "Connection rejected." : reason;
            client.FailureStatus = status;
            client.Phase = ConnectionPhase.Rejected;

            _Events.HandleConnectionRejected(this, new ConnectionRejectedEventArgs(client, client.FailureReason, status));
            await SendStatusMessageAsync(client, status, client.FailureReason, token).ConfigureAwait(false);
            CleanupPendingClient(client);
        }

        private async Task SendStatusMessageAsync(ClientMetadata client, MessageStatus status, string reason, CancellationToken token)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            byte[] data = Array.Empty<byte>();
            if (!String.IsNullOrEmpty(reason)) data = Encoding.UTF8.GetBytes(reason);
            WatsonCommon.BytesToStream(data, 0, out int contentLength, out Stream stream);
            WatsonMessage msg = _MessageBuilder.ConstructNew(contentLength, stream, false, false, null, null);
            msg.Status = status;
            await SendInternalAsync(client, msg, contentLength, stream, token).ConfigureAwait(false);
        }

        private async Task<string> ReadStatusMessageAsync(WatsonMessage msg, CancellationToken token)
        {
            if (msg == null) return null;
            if (msg.ContentLength <= 0) return null;
            byte[] data = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize, token).ConfigureAwait(false);
            if (data == null || data.Length < 1) return null;
            return Encoding.UTF8.GetString(data);
        }

        private async Task SendHandshakeDataAsync(ClientMetadata client, HandshakeMessage handshakeMessage, CancellationToken token)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (handshakeMessage == null) throw new ArgumentNullException(nameof(handshakeMessage));

            byte[] data = Encoding.UTF8.GetBytes(SerializationHelper.SerializeJson(handshakeMessage, false));
            WatsonCommon.BytesToStream(data, 0, out int contentLength, out Stream stream);
            WatsonMessage msg = _MessageBuilder.ConstructNew(contentLength, stream, false, false, null, null);
            msg.Status = MessageStatus.HandshakeData;
            await SendInternalAsync(client, msg, contentLength, stream, token).ConfigureAwait(false);
        }

        private async Task<HandshakeMessage> ReadHandshakeMessageAsync(WatsonMessage msg, CancellationToken token)
        {
            if (msg == null) return null;
            if (msg.ContentLength <= 0) return new HandshakeMessage();

            byte[] data = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize, token).ConfigureAwait(false);
            if (data == null || data.Length < 1) return new HandshakeMessage();
            return SerializationHelper.DeserializeJson<HandshakeMessage>(Encoding.UTF8.GetString(data));
        }

        private async Task DrainMessageAsync(WatsonMessage msg, CancellationToken token)
        {
            if (msg == null || msg.ContentLength <= 0) return;
            await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize, token).ConfigureAwait(false);
        }

        private static bool IsClientConnected(ClientMetadata client)
        {
            if (client != null && client.TcpClient != null)
            {
                var state = IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpConnections()
                        .FirstOrDefault(x =>
                            x.LocalEndPoint.Equals(client.TcpClient.Client.LocalEndPoint)
                            && x.RemoteEndPoint.Equals(client.TcpClient.Client.RemoteEndPoint));

                if (state == default(TcpConnectionInformation)
                    || state.State == TcpState.Unknown
                    || state.State == TcpState.FinWait1
                    || state.State == TcpState.FinWait2
                    || state.State == TcpState.Closed
                    || state.State == TcpState.Closing
                    || state.State == TcpState.CloseWait)
                {
                    return false;
                }

                byte[] tmp = new byte[1];
                bool success = false;

                try
                {
                    client.WriteLock.Wait();
                    client.TcpClient.Client.Send(tmp, 0, 0);
                    success = true;
                }
                catch (SocketException se)
                {
                    if (se.NativeErrorCode.Equals(10035)) success = true;
                }
                catch (Exception)
                {
                }
                finally
                {
                    if (client != null)
                    {
                        client.WriteLock.Release();
                    }
                }

                if (success) return true;

                try
                {
                    client.WriteLock.Wait();

                    if ((client.TcpClient.Client.Poll(0, SelectMode.SelectWrite))
                        && (!client.TcpClient.Client.Poll(0, SelectMode.SelectError)))
                    {
                        byte[] buffer = new byte[1];
                        if (client.TcpClient.Client.Receive(buffer, SocketFlags.Peek) == 0)
                        {
                            return false;
                        }
                        else
                        {
                            return true;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
                catch (Exception)
                {
                    return false;
                }
                finally
                {
                    if (client != null) client.WriteLock.Release();
                }
            }
            else
            {
                return false;
            }
        }

        #endregion

        #region Read

        private async Task DataReceiver(ClientMetadata client, CancellationToken token)
        {
            while (true)
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    if (!IsClientConnected(client)) break;

#pragma warning disable CA2016 // token intentionally not forwarded - stream closure is the proper disconnect signal
                    WatsonMessage msg = await _MessageBuilder.BuildFromStream(client.DataStream);
#pragma warning restore CA2016
                    if (msg == null)
                    {
                        await Task.Delay(30, token).ConfigureAwait(false);
                        continue;
                    }

                    if (!String.IsNullOrEmpty(_Settings.PresharedKey))
                    {
                        if (_ClientManager.ExistsUnauthenticatedClient(client.Guid))
                        {
                            _Settings.Logger?.Invoke(Severity.Debug, _Header + "message received from unauthenticated endpoint " + client.ToString());

                            if (msg.Status == MessageStatus.AuthRequested)
                            {
                                // check preshared key
                                if (msg.PresharedKey != null && msg.PresharedKey.Length > 0)
                                {
                                    string clientPsk = Encoding.UTF8.GetString(msg.PresharedKey).Trim();
                                    if (_Settings.PresharedKey.Trim().Equals(clientPsk, StringComparison.Ordinal))
                                    {
                                        _Settings.Logger?.Invoke(Severity.Debug, _Header + "accepted authentication for " + client.ToString());
                                        _ClientManager.RemoveUnauthenticatedClient(client.Guid);
                                        _Events.HandleAuthenticationSucceeded(this, new AuthenticationSucceededEventArgs(client));

                                        await SendStatusMessageAsync(client, MessageStatus.AuthSuccess, "Authentication successful", token).ConfigureAwait(false);
                                        await BeginPostAuthenticationFlowAsync(client, token).ConfigureAwait(false);
                                        continue;
                                    }
                                    else
                                    {
                                        _Settings.Logger?.Invoke(Severity.Warn, _Header + "declined authentication for " + client.ToString());
                                        _Events.HandleAuthenticationFailed(this, new AuthenticationFailedEventArgs(client.IpPort));
                                        client.HandshakeFailed = false;
                                        client.FailureReason = "Authentication failed.";
                                        client.FailureStatus = MessageStatus.AuthFailure;
                                        await SendStatusMessageAsync(client, MessageStatus.AuthFailure, client.FailureReason, token).ConfigureAwait(false);
                                        break;
                                    }
                                }
                                else
                                {
                                    // AuthRequested message with no pre-shared key - decline and terminate
                                    _Settings.Logger?.Invoke(Severity.Warn, _Header + "no authentication material for " + client.ToString());
                                    _Events.HandleAuthenticationFailed(this, new AuthenticationFailedEventArgs(client.IpPort));
                                    client.FailureReason = "Authentication failed.";
                                    client.FailureStatus = MessageStatus.AuthFailure;
                                    await SendStatusMessageAsync(client, MessageStatus.AuthFailure, client.FailureReason, token).ConfigureAwait(false);
                                    break;
                                }
                            }
                            else
                            {
                                // Non-auth message from unauthenticated client - ignore and wait for auth
                                _Settings.Logger?.Invoke(Severity.Debug, _Header + "ignoring message from unauthenticated client " + client.ToString() + " (waiting for authentication)");
                                await DrainMessageAsync(msg, token).ConfigureAwait(false);
                                await Task.Delay(30, token).ConfigureAwait(false);
                                continue;
                            }
                        }
                    }

                    if (msg.Status == MessageStatus.Shutdown)
                    {
                        _Settings.Logger?.Invoke(Severity.Debug, _Header + "client " + client.ToString() + " is disconnecting");
                        break;
                    }
                    else if (msg.Status == MessageStatus.Removed)
                    {
                        _Settings.Logger?.Invoke(Severity.Debug, _Header + "sent disconnect notice to " + client.ToString());
                        break;
                    }
                    else if (client.HandshakeRequired && !client.HandshakeCompleted)
                    {
                        if (msg.Status == MessageStatus.HandshakeData)
                        {
                            HandshakeMessage handshakeMsg = await ReadHandshakeMessageAsync(msg, token).ConfigureAwait(false);
                            client.HandshakeTransport?.Enqueue(handshakeMsg);
                            continue;
                        }
                        else if (msg.Status == MessageStatus.HandshakeFailure)
                        {
                            string reason = await ReadStatusMessageAsync(msg, token).ConfigureAwait(false);
                            client.HandshakeFailed = true;
                            client.FailureReason = reason;
                            client.FailureStatus = MessageStatus.HandshakeFailure;
                            _Events.HandleHandshakeFailed(this, new HandshakeFailedEventArgs(client, reason, MessageStatus.HandshakeFailure));
                            break;
                        }
                        else if (msg.Status == MessageStatus.RegisterClient)
                        {
                            _Settings.Logger?.Invoke(Severity.Debug, _Header + "ignoring registration from " + client.ToString() + " while handshake is pending");
                            await DrainMessageAsync(msg, token).ConfigureAwait(false);
                            continue;
                        }
                        else
                        {
                            _Settings.Logger?.Invoke(Severity.Debug, _Header + "ignoring message from " + client.ToString() + " while handshake is pending");
                            await DrainMessageAsync(msg, token).ConfigureAwait(false);
                            continue;
                        }
                    }
                    else if (msg.Status == MessageStatus.RegisterClient)
                    {
                        await ActivateClientAsync(client, msg.SenderGuid).ConfigureAwait(false);
                        continue;
                    }

                    if (msg.SyncRequest)
                    {
                        _Settings.Logger?.Invoke(Severity.Debug, _Header + client.ToString() + " synchronous request received: " + msg.ConversationGuid.ToString());

                        DateTime expiration = WatsonCommon.GetExpirationTimestamp(msg);
                        byte[] msgData = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize, token).ConfigureAwait(false);

                        if (DateTime.UtcNow < expiration)
                        {
                            Task unawaited = Task.Run(async () =>
                            {
                                SyncRequest syncReq = new SyncRequest(
                                    client,
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
                                    await SendInternalAsync(client, respMsg, contentLength, stream, token).ConfigureAwait(false);
                                }
                            }, token);
                        }
                        else
                        {
                            _Settings.Logger?.Invoke(Severity.Debug, _Header + "expired synchronous request received and discarded from " + client.ToString());
                        }
                    }
                    else if (msg.SyncResponse)
                    {
                        // No need to amend message expiration; it is copied from the request, which was set by this node
                        // DateTime expiration = WatsonCommon.GetExpirationTimestamp(msg);
                        _Settings.Logger?.Invoke(Severity.Debug, _Header + client.ToString() + " synchronous response received: " + msg.ConversationGuid.ToString());
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
                                _Settings.Logger?.Invoke(Severity.Warn, _Header + "synchronous response received for unknown conversation from " + client.ToString() + ": " + msg.ConversationGuid.ToString());
                            }
                        }
                        else
                        {
                            _Settings.Logger?.Invoke(Severity.Debug, _Header + "expired synchronous response received and discarded from " + client.ToString());
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
                            MessageReceivedEventArgs mr = new MessageReceivedEventArgs(client, msg.Metadata, msgData);
                            await Task.Run(() => _Events.HandleMessageReceived(this, mr), token);
                        }
                        else if (_Events.IsUsingStreams)
                        {
                            StreamReceivedEventArgs sr = null;
                            WatsonStream ws = null;

                            if (msg.ContentLength >= _Settings.MaxProxiedStreamSize)
                            {
                                ws = new WatsonStream(msg.ContentLength, msg.DataStream);
                                sr = new StreamReceivedEventArgs(client, msg.Metadata, msg.ContentLength, ws);
                                _Events.HandleStreamReceived(this, sr);
                            }
                            else
                            {
                                MemoryStream ms = await WatsonCommon.DataStreamToMemoryStream(msg.ContentLength, msg.DataStream, _Settings.StreamBufferSize, token).ConfigureAwait(false);
                                ws = new WatsonStream(msg.ContentLength, ms);
                                sr = new StreamReceivedEventArgs(client, msg.Metadata, msg.ContentLength, ws);
                                await Task.Run(() => _Events.HandleStreamReceived(this, sr), token);
                            }
                        }
                        else
                        {
                            _Settings.Logger?.Invoke(Severity.Error, _Header + "event handler not set for either MessageReceived or StreamReceived");
                            break;
                        }
                    }

                    _Statistics.IncrementReceivedMessages();
                    _Statistics.AddReceivedBytes(msg.ContentLength);
                    if (client.Registered)
                    {
                        _ClientManager.UpdateClientLastSeen(client.Guid, DateTime.UtcNow);
                    }
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
                    _Settings?.Logger?.Invoke(Severity.Error, _Header + "data receiver exception for " + client.ToString() + ": " + e.Message);
                    _Events?.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                    break;
                }
            }

            if (_Settings != null && _Events != null)
            {
                DisconnectReason reason = DisconnectReason.Normal;
                if (client.ConnectionRejected) reason = DisconnectReason.ConnectionRejected;
                else if (client.HandshakeFailed && client.FailureStatus == MessageStatus.HandshakeFailure) reason = DisconnectReason.HandshakeFailure;
                else if (_ClientManager.ExistsClientKicked(client.Guid)) reason = DisconnectReason.Removed;
                else if (_ClientManager.ExistsClientTimedout(client.Guid)) reason = DisconnectReason.Timeout;
                else if (client.FailureStatus == MessageStatus.AuthFailure) reason = DisconnectReason.AuthFailure;

                if (client.Registered)
                {
                    _Events.HandleClientDisconnected(this, new DisconnectionEventArgs(client, reason));
                }

                _ClientManager.Remove(client.Guid);
                Interlocked.Decrement(ref _Connections);

                _Settings?.Logger?.Invoke(Severity.Debug, _Header + "client " + client.ToString() + " disconnected");
                client.Dispose();
            }
        }

        #endregion

        #region Send

        private async Task<bool> SendInternalAsync(ClientMetadata client, WatsonMessage msg, long contentLength, Stream stream, CancellationToken token)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (msg == null) throw new ArgumentNullException(nameof(msg));

            if (contentLength > 0)
            {
                if (stream == null || !stream.CanRead)
                {
                    throw new ArgumentException("Cannot read from supplied stream.");
                }
            }

            if (token == default(CancellationToken))
            {
                CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _Token);
                token = linkedCts.Token;
            }

            await client.WriteLock.WaitAsync(token).ConfigureAwait(false);

            try
            {
                await SendHeadersAsync(client, msg, token).ConfigureAwait(false);
                await SendDataStreamAsync(client, contentLength, stream, token).ConfigureAwait(false);

                _Statistics.IncrementSentMessages();
                _Statistics.AddSentBytes(contentLength);
                return true;
            }
            catch (TaskCanceledException)
            {
                _Settings?.Logger?.Invoke(Severity.Debug, _Header + "send to " + client.ToString() + " canceled");
                return false;
            }
            catch (OperationCanceledException)
            {
                _Settings?.Logger?.Invoke(Severity.Debug, _Header + "send to " + client.ToString() + " operation canceled");
                return false;
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(Severity.Error, _Header + "failed to write message to " + client.ToString() + ": " + e.Message);
                _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                return false;
            }
            finally
            {
                if (client != null) client.WriteLock.Release();
            }
        }

        private async Task<SyncResponse> SendAndWaitInternalAsync(ClientMetadata client, WatsonMessage msg, int timeoutMs, long contentLength, Stream stream, CancellationToken token)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (msg == null) throw new ArgumentNullException(nameof(msg));

            if (contentLength > 0)
            {
                if (stream == null || !stream.CanRead)
                {
                    throw new ArgumentException("Cannot read from supplied stream.");
                }
            }

            // Register a TaskCompletionSource for this conversation before sending
            TaskCompletionSource<SyncResponse> tcs = new TaskCompletionSource<SyncResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _SyncRequests[msg.ConversationGuid] = tcs;

            await client.WriteLock.WaitAsync(token);

            try
            {
                await SendHeadersAsync(client, msg, token);
                await SendDataStreamAsync(client, contentLength, stream, token);
                _Settings.Logger?.Invoke(Severity.Debug, _Header + client.ToString() + " synchronous request sent: " + msg.ConversationGuid);

                _Statistics.IncrementSentMessages();
                _Statistics.AddSentBytes(contentLength);
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(Severity.Error, _Header + client.ToString() + " failed to write message: " + e.Message);
                _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                _SyncRequests.TryRemove(msg.ConversationGuid, out _);
                throw;
            }
            finally
            {
                if (client != null) client.WriteLock.Release();
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

        private async Task SendHeadersAsync(ClientMetadata client, WatsonMessage msg, CancellationToken token)
        {
            byte[] headerBytes = _MessageBuilder.GetHeaderBytes(msg);
            await client.DataStream.WriteAsync(headerBytes, 0, headerBytes.Length, token).ConfigureAwait(false);
            await client.DataStream.FlushAsync(token).ConfigureAwait(false);
        }

        private async Task SendDataStreamAsync(ClientMetadata client, long contentLength, Stream stream, CancellationToken token)
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
                        await client.DataStream.WriteAsync(buffer, 0, bytesRead, token).ConfigureAwait(false);
                        bytesRemaining -= bytesRead;
                    }
                }

                await client.DataStream.FlushAsync(token).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        #endregion

        #region Tasks

        private async Task MonitorForIdleClients(CancellationToken token)
        {
            int purgeCounter = 0;

            try
            {
                Dictionary<Guid, DateTime> lastSeen = null;

                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    await Task.Delay(5000, _Token).ConfigureAwait(false);

                    if (_Settings.IdleClientTimeoutSeconds > 0)
                    {
                        lastSeen = _ClientManager.AllClientsLastSeen();

                        if (lastSeen != null && lastSeen.Count > 0)
                        {
                            DateTime idleTimestamp = DateTime.UtcNow.AddSeconds(-1 * _Settings.IdleClientTimeoutSeconds);

                            foreach (KeyValuePair<Guid, DateTime> curr in lastSeen)
                            {
                                if (curr.Value < idleTimestamp)
                                {
                                    _ClientManager.AddClientTimedout(curr.Key);
                                    _Settings.Logger?.Invoke(Severity.Debug, _Header + "disconnecting client " + curr.Key + " due to idle timeout");
                                    await DisconnectClientAsync(curr.Key, MessageStatus.Timeout, true, token);
                                }
                            }
                        }
                    }

                    // Purge stale kicked/timed-out records every ~60 seconds (12 iterations * 5s)
                    purgeCounter++;
                    if (purgeCounter >= 12)
                    {
                        purgeCounter = 0;
                        _ClientManager.PurgeStaleRecords(TimeSpan.FromMinutes(5));
                    }
                }
            }
            catch (TaskCanceledException)
            {
                _Settings?.Logger?.Invoke(Severity.Debug, _Header + "idle client monitor task canceled");
            }
            catch (OperationCanceledException)
            {
                _Settings?.Logger?.Invoke(Severity.Debug, _Header + "idle client monitor operation canceled");
            }
        }

        #endregion

        #endregion
    }
}
