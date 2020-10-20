using System;
using System.Collections.Concurrent;
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

namespace WatsonTcp
{
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
        private WatsonTcpServerSettings _Settings = new WatsonTcpServerSettings();
        private WatsonTcpServerEvents _Events = new WatsonTcpServerEvents();
        private WatsonTcpServerCallbacks _Callbacks = new WatsonTcpServerCallbacks();
        private WatsonTcpStatistics _Statistics = new WatsonTcpStatistics();
        private WatsonTcpKeepaliveSettings _Keepalive = new WatsonTcpKeepaliveSettings();

        private int _Connections = 0;
        private bool _IsListening = false;

        private Mode _Mode;
        private string _ListenerIp;
        private int _ListenerPort;
        private IPAddress _ListenerIpAddress;
        private TcpListener _Listener;

        private X509Certificate2 _SslCertificate;

        private ConcurrentDictionary<string, DateTime> _UnauthenticatedClients = new ConcurrentDictionary<string, DateTime>();
        private ConcurrentDictionary<string, ClientMetadata> _Clients = new ConcurrentDictionary<string, ClientMetadata>();
        private ConcurrentDictionary<string, DateTime> _ClientsLastSeen = new ConcurrentDictionary<string, DateTime>();
        private ConcurrentDictionary<string, DateTime> _ClientsKicked = new ConcurrentDictionary<string, DateTime>();
        private ConcurrentDictionary<string, DateTime> _ClientsTimedout = new ConcurrentDictionary<string, DateTime>();

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private CancellationToken _Token;

        private readonly object _SyncResponseLock = new object();
        private Dictionary<string, SyncResponse> _SyncResponses = new Dictionary<string, SyncResponse>();
         
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
             
            if (String.IsNullOrEmpty(listenerIp))
            {
                _ListenerIpAddress = IPAddress.Any;
                _ListenerIp = _ListenerIpAddress.ToString();
            }
            else
            {
                _ListenerIpAddress = IPAddress.Parse(listenerIp);
                _ListenerIp = listenerIp;
            }

            _ListenerPort = listenerPort; 
            _Listener = new TcpListener(_ListenerIpAddress, _ListenerPort); 
            _Token = _TokenSource.Token;

            Task.Run(() => MonitorForIdleClients(), _Token); 
            Task.Run(() => MonitorForExpiredSyncResponses(), _Token);
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
        public WatsonTcpServer(
            string listenerIp,
            int listenerPort,
            string pfxCertFile,
            string pfxCertPass)
        {
            if (listenerPort < 1) throw new ArgumentOutOfRangeException(nameof(listenerPort));
            if (String.IsNullOrEmpty(pfxCertFile)) throw new ArgumentNullException(nameof(pfxCertFile));
             
            _Mode = Mode.Ssl;

            if (String.IsNullOrEmpty(listenerIp))
            {
                _ListenerIpAddress = IPAddress.Any;
                _ListenerIp = _ListenerIpAddress.ToString();
            }
            else
            {
                _ListenerIpAddress = IPAddress.Parse(listenerIp);
                _ListenerIp = listenerIp;
            }

            _SslCertificate = null; 
            if (String.IsNullOrEmpty(pfxCertPass))
            {
                _SslCertificate = new X509Certificate2(pfxCertFile);
            }
            else
            {
                _SslCertificate = new X509Certificate2(pfxCertFile, pfxCertPass);
            }

            _ListenerPort = listenerPort;
            _Listener = new TcpListener(_ListenerIpAddress, _ListenerPort); 
            _Token = _TokenSource.Token;

            Task.Run(() => MonitorForIdleClients(), _Token); 
            Task.Run(() => MonitorForExpiredSyncResponses(), _Token);
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
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public WatsonTcpServer(
            string listenerIp,
            int listenerPort,
            X509Certificate2 cert)
        {
            if (listenerPort < 1) throw new ArgumentOutOfRangeException(nameof(listenerPort));
            if (cert == null) throw new ArgumentNullException(nameof(cert));

            _Mode = Mode.Ssl;
            _SslCertificate = cert;

            if (String.IsNullOrEmpty(listenerIp))
            {
                _ListenerIpAddress = IPAddress.Any;
                _ListenerIp = _ListenerIpAddress.ToString();
            }
            else
            {
                _ListenerIpAddress = IPAddress.Parse(listenerIp);
                _ListenerIp = listenerIp;
            }

            _ListenerPort = listenerPort;
            _Listener = new TcpListener(_ListenerIpAddress, _ListenerPort);
            _Token = _TokenSource.Token;

            Task.Run(() => MonitorForIdleClients(), _Token);
            Task.Run(() => MonitorForExpiredSyncResponses(), _Token);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tear down the server and dispose of background workers.
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

            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;
            _Statistics = new WatsonTcpStatistics();

            if (!_Events.IsUsingMessages && !_Events.IsUsingStreams)
                throw new InvalidOperationException("One of either 'MessageReceived' or 'StreamReceived' events must first be set.");

            if (_Keepalive.EnableTcpKeepAlives) EnableKeepalives();

            if (_Mode == Mode.Tcp)
            {
                _Settings.Logger?.Invoke(_Header + "starting on " + _ListenerIp + ":" + _ListenerPort);
            }
            else if (_Mode == Mode.Ssl)
            {
                _Settings.Logger?.Invoke(_Header + "starting with SSL on " + _ListenerIp + ":" + _ListenerPort);
            }
            else
            {
                throw new ArgumentException("Unknown mode: " + _Mode.ToString());
            }

            Task.Run(() => AcceptConnections(), _Token); // sets _IsListening
        }

        /// <summary>
        /// Start accepting connections.
        /// </summary>
        public Task StartAsync()
        {
            if (_IsListening) throw new InvalidOperationException("WatsonTcpServer is already running.");

            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;
            _Statistics = new WatsonTcpStatistics();

            if (!_Events.IsUsingMessages && !_Events.IsUsingStreams)
                throw new InvalidOperationException("One of either 'MessageReceived' or 'StreamReceived' events must first be set.");

            if (_Keepalive.EnableTcpKeepAlives)
                ServicePointManager.SetTcpKeepAlive(true, _Keepalive.TcpKeepAliveTime, _Keepalive.TcpKeepAliveInterval);

            if (_Mode == Mode.Tcp)
            {
                _Settings.Logger?.Invoke(_Header + "starting on " + _ListenerIp + ":" + _ListenerPort); 
            }
            else if (_Mode == Mode.Ssl)
            {
                _Settings.Logger?.Invoke(_Header + "starting with SSL on " + _ListenerIp + ":" + _ListenerPort);
            }
            else
            {
                throw new ArgumentException("Unknown mode: " + _Mode.ToString());
            }

            return AcceptConnections(); 
        }

        /// <summary>
        /// Stop accepting connections.
        /// </summary>
        public void Stop()
        {
            if (!_IsListening) throw new InvalidOperationException("WatsonTcpServer is not running.");

            try
            {
                _IsListening = false;
                _Listener.Stop();
                _TokenSource.Cancel();

                _Settings?.Logger.Invoke(_Header + "stopped");
            }
            catch (Exception e)
            {
                _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                throw;
            }
        }

        /// <summary>
        /// Send data to the specified client.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">String containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(string ipPort, string data)
        {
            return Send(ipPort, null, data);
        }

        /// <summary>
        /// Send data and metadata to the specified client.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="data">String containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(string ipPort, Dictionary<object, object> metadata, string data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            byte[] bytes = new byte[0];
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return Send(ipPort, metadata, bytes);
        }

        /// <summary>
        /// Send data to the specified client.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(string ipPort, byte[] data)
        {
            return Send(ipPort, null, data); 
        }

        /// <summary>
        /// Send data and metadata to the specified client.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(string ipPort, Dictionary<object, object> metadata, byte[] data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                _Settings.Logger?.Invoke(_Header + "unable to find client " + ipPort);
                return false;
            }

            if (data == null) data = new byte[0];
            WatsonCommon.BytesToStream(data, out long contentLength, out Stream stream);
            return Send(ipPort, metadata, contentLength, stream);
        }

        /// <summary>
        /// Send data to the specified client using a stream.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="contentLength">The number of bytes in the stream.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(string ipPort, long contentLength, Stream stream)
        {
            return Send(ipPort, null, contentLength, stream);
        }

        /// <summary>
        /// Send data and metadata to the specified client using a stream.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="contentLength">The number of bytes in the stream.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(string ipPort, Dictionary<object, object> metadata, long contentLength, Stream stream)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                _Settings.Logger?.Invoke(_Header + "unable to find client " + ipPort);
                return false;
            }

            if (stream == null) stream = new MemoryStream(new byte[0]);
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
            return SendInternal(client, msg, contentLength, stream);
        }

        /// <summary>
        /// Send metadata to the specified client with no data.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(string ipPort, Dictionary<object, object> metadata)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                _Settings.Logger?.Invoke(_Header + "unable to find client " + ipPort);
                return false;
            }

            WatsonMessage msg = new WatsonMessage(metadata, 0, new MemoryStream(new byte[0]), false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
            return SendInternal(client, msg, 0, new MemoryStream(new byte[0]));
        }

        /// <summary>
        /// Send data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">String containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, string data)
        {
            return await SendAsync(ipPort, null, data);
        }

        /// <summary>
        /// Send data and metadata to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="data">String containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, Dictionary<object, object> metadata, string data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            byte[] bytes = new byte[0];
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return await SendAsync(ipPort, metadata, bytes);
        }

        /// <summary>
        /// Send data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, byte[] data)
        {
            return await SendAsync(ipPort, null, data); 
        }

        /// <summary>
        /// Send data and metadata to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, Dictionary<object, object> metadata, byte[] data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                _Settings.Logger?.Invoke(_Header + "unable to find client " + ipPort);
                return false;
            }

            if (data == null) data = new byte[0];
            WatsonCommon.BytesToStream(data, out long contentLength, out Stream stream);
            return await SendAsync(ipPort, metadata, contentLength, stream);
        }

        /// <summary>
        /// Send data to the specified client using a stream, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="contentLength">The number of bytes in the stream.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, long contentLength, Stream stream)
        {
            return await SendAsync(ipPort, null, contentLength, stream);
        }

        /// <summary>
        /// Send data and metadata to the specified client using a stream, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="contentLength">The number of bytes in the stream.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, Dictionary<object, object> metadata, long contentLength, Stream stream)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                _Settings.Logger?.Invoke(_Header + "unable to find client " + ipPort);
                return false;
            }

            if (stream == null) stream = new MemoryStream(new byte[0]);
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
            return await SendInternalAsync(client, msg, contentLength, stream);
        }

        /// <summary>
        /// Send metadata to the specified client with no data, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, Dictionary<object, object> metadata)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                _Settings.Logger?.Invoke(_Header + "unable to find client " + ipPort);
                return false;
            }

            WatsonMessage msg = new WatsonMessage(metadata, 0, new MemoryStream(new byte[0]), false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
            return await SendInternalAsync(client, msg, 0, new MemoryStream(new byte[0]));
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="ipPort">The IP:port of the client.</param>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="data">Data to send.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(string ipPort, int timeoutMs, string data)
        {
            return SendAndWait(ipPort, null, timeoutMs, data);
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="ipPort">The IP:port of the client.</param>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="data">Data to send.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(string ipPort, int timeoutMs, byte[] data)
        {
            return SendAndWait(ipPort, null, timeoutMs, data);
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="ipPort">The IP:port of the client.</param>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="contentLength">The number of bytes to send from the supplied stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(string ipPort, int timeoutMs, long contentLength, Stream stream)
        {
            return SendAndWait(ipPort, null, timeoutMs, contentLength, stream); 
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="ipPort">The IP:port of the client.</param>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="data">Data to send.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(string ipPort, Dictionary<object, object> metadata, int timeoutMs, string data)
        {
            byte[] bytes = new byte[0];
            if (!String.IsNullOrEmpty(data)) bytes = Encoding.UTF8.GetBytes(data);
            return SendAndWait(ipPort, metadata, timeoutMs, bytes);
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.
        /// </summary>
        /// <param name="ipPort">The IP:port of the client.</param>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="data">Data to send.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(string ipPort, Dictionary<object, object> metadata, int timeoutMs, byte[] data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                _Settings.Logger?.Invoke(_Header + "unable to find client " + ipPort);
                throw new KeyNotFoundException("Unable to find client " + ipPort + ".");
            }
            if (data == null) data = new byte[0];
            WatsonCommon.BytesToStream(data, out long contentLength, out Stream stream);
            return SendAndWait(ipPort, metadata, timeoutMs, contentLength, stream);
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="ipPort">The IP:port of the client.</param>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="contentLength">The number of bytes to send from the supplied stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(string ipPort, Dictionary<object, object> metadata, int timeoutMs, long contentLength, Stream stream)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                _Settings.Logger?.Invoke(_Header + "unable to find client " + ipPort);
                throw new KeyNotFoundException("Unable to find client " + ipPort + ".");
            }
            if (stream == null) stream = new MemoryStream(new byte[0]);
            DateTime expiration = DateTime.Now.AddMilliseconds(timeoutMs);
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, true, false, expiration, Guid.NewGuid().ToString(), (_Settings.DebugMessages ? _Settings.Logger : null));
            return SendAndWaitInternal(client, msg, timeoutMs, contentLength, stream);
        }

        /// <summary>
        /// Send metadata and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="ipPort">The IP:port of the client.</param>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param> 
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(string ipPort, Dictionary<object, object> metadata, int timeoutMs)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                _Settings.Logger?.Invoke(_Header + "unable to find client " + ipPort);
                throw new KeyNotFoundException("Unable to find client " + ipPort + ".");
            }
            DateTime expiration = DateTime.Now.AddMilliseconds(timeoutMs);
            WatsonMessage msg = new WatsonMessage(metadata, 0, new MemoryStream(new byte[0]), true, false, expiration, Guid.NewGuid().ToString(), (_Settings.DebugMessages ? _Settings.Logger : null));
            return SendAndWaitInternal(client, msg, timeoutMs, 0, new MemoryStream(new byte[0]));
        }

        /// <summary>
        /// Determine whether or not the specified client is connected to the server.
        /// </summary>
        /// <returns>Boolean indicating if the client is connected to the server.</returns>
        public bool IsClientConnected(string ipPort)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            return (_Clients.TryGetValue(ipPort, out ClientMetadata client));
        }

        /// <summary>
        /// List the IP:port of each connected client.
        /// </summary>
        /// <returns>An enumerable string list containing each client IP:port.</returns>
        public IEnumerable<string> ListClients()
        {
            return _Clients.Keys.ToList();
        }

        /// <summary>
        /// Disconnects the specified client.
        /// </summary>
        /// <param name="ipPort">IP:port of the client.</param>
        public void DisconnectClient(string ipPort)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort)); 
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                _Settings.Logger?.Invoke(_Header + "unable to find client " + ipPort);
            }
            else
            { 
                if (!_ClientsTimedout.ContainsKey(ipPort)) _ClientsKicked.TryAdd(ipPort, DateTime.Now); 

                WatsonMessage removeMsg = new WatsonMessage();
                removeMsg.Status = MessageStatus.Removed; 
                SendInternal(client, removeMsg, 0, null); 

                client.Dispose();
                _Clients.TryRemove(ipPort, out ClientMetadata removed);
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Tear down the server and dispose of background workers.
        /// </summary>
        /// <param name="disposing">Indicate if resources should be disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _Settings.Logger?.Invoke(_Header + "disposing");

                    if (_TokenSource != null)
                    {
                        if (!_TokenSource.IsCancellationRequested) _TokenSource.Cancel();
                        _TokenSource.Dispose();
                        _TokenSource = null;
                    }

                    if (_Listener != null && _Listener.Server != null)
                    {
                        _Listener.Server.Close();
                        _Listener.Server.Dispose();
                        _Listener = null;
                    }

                    if (_Clients != null && _Clients.Count > 0)
                    {
                        WatsonMessage discMsg = new WatsonMessage();
                        discMsg.Status = MessageStatus.Disconnecting;

                        foreach (KeyValuePair<string, ClientMetadata> currMetadata in _Clients)
                        {
                            SendInternal(currMetadata.Value, discMsg, 0, null);
                            currMetadata.Value.Dispose();
                        }

                        _Clients = null;
                        _UnauthenticatedClients = null;
                    }

                    _Settings.Logger?.Invoke(_Header + "disposed");
                }
                catch (Exception e)
                {
                    _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                    throw;
                }
            }
        }
         
        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // return true; // Allow untrusted certificates.
            return _Settings.AcceptInvalidCertificates;
        }

        private async Task AcceptConnections()
        {
            _IsListening = true;
            _Listener.Start();

            while (!_Token.IsCancellationRequested)
            {
                string ipPort = String.Empty;

                try
                {
                    #region Check-for-Maximum-Connections

                    if (!_IsListening && (_Connections >= _Settings.MaxConnections))
                    {
                        Task.Delay(100).Wait();
                        continue;
                    }
                    else if (!_IsListening)
                    {
                        _Listener.Start();
                        _IsListening = true;
                    }

                    #endregion

                    #region Accept-Connection-and-Validate-IP

                    TcpClient tcp = await _Listener.AcceptTcpClientAsync();
                    tcp.LingerState.Enabled = false;

                    string clientIp = ((IPEndPoint)tcp.Client.RemoteEndPoint).Address.ToString();
                    if (_Settings.PermittedIPs.Count > 0 && !_Settings.PermittedIPs.Contains(clientIp))
                    {
                        _Settings.Logger?.Invoke(_Header + "rejecting connection from " + clientIp + " (not permitted)");
                        tcp.Close();
                        continue;
                    }

                    ClientMetadata client = new ClientMetadata(tcp);
                    ipPort = client.IpPort;

                    #endregion Accept-Connection-and-Validate-IP

                    #region Check-for-Maximum-Connections

                    Interlocked.Increment(ref _Connections);
                    if (_Connections >= _Settings.MaxConnections)
                    {
                        _Settings.Logger?.Invoke(_Header + "maximum connections " + _Settings.MaxConnections + " met or exceeded (currently " + _Connections + " connections), pausing listener");
                        _IsListening = false;
                        _Listener.Stop();
                    }

                    #endregion

                    #region Initialize-Client-and-Finalize-Connection

                    if (_Mode == Mode.Tcp)
                    { 
                        Task unawaited = Task.Run(() => FinalizeConnection(client), _Token); 
                    }
                    else if (_Mode == Mode.Ssl)
                    { 
                        if (_Settings.AcceptInvalidCertificates)
                        {
                            client.SslStream = new SslStream(client.NetworkStream, false, new RemoteCertificateValidationCallback(AcceptCertificate));
                        }
                        else
                        {
                            client.SslStream = new SslStream(client.NetworkStream, false);
                        }

                        Task unawaited = Task.Run(() =>
                        {
                            Task<bool> success = StartTls(client);
                            if (success.Result)
                            {
                                FinalizeConnection(client);
                            }

                        }, _Token); 
                    }
                    else
                    {
                        throw new ArgumentException("Unknown mode: " + _Mode.ToString());
                    }

                    _Settings.Logger?.Invoke(_Header + "accepted connection from " + client.IpPort);

                    #endregion 
                }
                catch (ObjectDisposedException)
                {
                    _Settings.Logger?.Invoke(_Header + "listener disposed");
                }
                catch (Exception e)
                {
                    _Settings.Logger?.Invoke(
                        _Header + "listener exception: " +
                        Environment.NewLine +
                        SerializationHelper.SerializeJson(e, true) +
                        Environment.NewLine);

                    _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                }

                _Settings.Logger?.Invoke(_Header + "listener stopped");
            }
        }

        private async Task<bool> StartTls(ClientMetadata client)
        {
            try
            { 
                await client.SslStream.AuthenticateAsServerAsync(_SslCertificate, true, SslProtocols.Tls12, !_Settings.AcceptInvalidCertificates);

                if (!client.SslStream.IsEncrypted)
                {
                    _Settings.Logger?.Invoke(_Header + "stream from " + client.IpPort + " not encrypted");
                    client.Dispose();
                    Interlocked.Decrement(ref _Connections);
                    return false;
                }

                if (!client.SslStream.IsAuthenticated)
                {
                    _Settings.Logger?.Invoke(_Header + "stream from " + client.IpPort + " not authenticated");
                    client.Dispose();
                    Interlocked.Decrement(ref _Connections);
                    return false;
                }

                if (_Settings.MutuallyAuthenticate && !client.SslStream.IsMutuallyAuthenticated)
                {
                    _Settings.Logger?.Invoke(_Header + "stream from " + client.IpPort + " failed mutual authentication");
                    client.Dispose(); 
                    Interlocked.Decrement(ref _Connections);
                    return false;
                }
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(
                    _Header + "disconnected during SSL/TLS establishment: " +
                    Environment.NewLine +
                    SerializationHelper.SerializeJson(e, true));

                _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));

                client.Dispose(); 
                Interlocked.Decrement(ref _Connections);
                return false;
            } 

            return true;
        }

        private void FinalizeConnection(ClientMetadata client)
        {
            #region Add-to-Client-List
             
            _Clients.TryAdd(client.IpPort, client);
            _ClientsLastSeen.TryAdd(client.IpPort, DateTime.Now);
             
            #endregion

            #region Request-Authentication

            if (!String.IsNullOrEmpty(_Settings.PresharedKey))
            {
                _Settings.Logger?.Invoke(_Header + "requesting authentication material from " + client.IpPort);
                _UnauthenticatedClients.TryAdd(client.IpPort, DateTime.Now);

                byte[] data = Encoding.UTF8.GetBytes("Authentication required");
                WatsonMessage authMsg = new WatsonMessage();
                authMsg.Status = MessageStatus.AuthRequired; 
                SendInternal(client, authMsg, 0, null);
            }

            #endregion

            #region Start-Data-Receiver

            _Settings.Logger?.Invoke(_Header + "starting data receiver for " + client.IpPort);
            Task.Run(async () => await DataReceiver(client, client.Token));

            _Events.HandleClientConnected(this, new ClientConnectedEventArgs(client.IpPort)); 

            #endregion
        }

        private bool IsConnected(ClientMetadata client)
        {
            if (client != null && client.TcpClient != null)
            {
                if (client.TcpClient.Connected)
                {
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
            else
            {
                return false;
            }
        }

        private async Task DataReceiver(ClientMetadata client, CancellationToken token)
        { 
            while (true)
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    if (!IsConnected(client)) break;

                    WatsonMessage msg = new WatsonMessage(client.DataStream, (_Settings.DebugMessages ? _Settings.Logger : null));
                    bool buildSuccess = await msg.BuildFromStream();
                    if (!buildSuccess)
                    {
                        _Settings.Logger?.Invoke(_Header + "message build failed due to disconnect for client " + client.IpPort);
                        break;
                    }

                    if (msg == null)
                    {
                        await Task.Delay(30);
                        continue;
                    }
                     
                    if (!String.IsNullOrEmpty(_Settings.PresharedKey))
                    {
                        if (_UnauthenticatedClients.ContainsKey(client.IpPort))
                        {
                            _Settings.Logger?.Invoke(_Header + "message received from unauthenticated endpoint " + client.IpPort);

                            if (msg.Status == MessageStatus.AuthRequested)
                            {
                                // check preshared key
                                if (msg.PresharedKey != null && msg.PresharedKey.Length > 0)
                                {
                                    string clientPsk = Encoding.UTF8.GetString(msg.PresharedKey).Trim();
                                    if (_Settings.PresharedKey.Trim().Equals(clientPsk))
                                    {
                                        _Settings.Logger?.Invoke(_Header + "accepted authentication for " + client.IpPort);
                                        _UnauthenticatedClients.TryRemove(client.IpPort, out DateTime dt);
                                        _Events.HandleAuthenticationSucceeded(this, new AuthenticationSucceededEventArgs(client.IpPort));
                                        byte[] data = Encoding.UTF8.GetBytes("Authentication successful");
                                        WatsonCommon.BytesToStream(data, out long contentLength, out Stream stream);
                                        WatsonMessage authMsg = new WatsonMessage(null, contentLength, stream, false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
                                        authMsg.Status = MessageStatus.AuthSuccess;
                                        SendInternal(client, authMsg, 0, null);
                                        continue;
                                    }
                                    else
                                    {
                                        _Settings.Logger?.Invoke(_Header + "declined authentication for " + client.IpPort);
                                        byte[] data = Encoding.UTF8.GetBytes("Authentication declined");
                                        _Events.HandleAuthenticationFailed(this, new AuthenticationFailedEventArgs(client.IpPort));
                                        WatsonCommon.BytesToStream(data, out long contentLength, out Stream stream);
                                        WatsonMessage authMsg = new WatsonMessage(null, contentLength, stream, false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
                                        authMsg.Status = MessageStatus.AuthFailure;
                                        SendInternal(client, authMsg, 0, null);
                                        continue;
                                    }
                                }
                                else
                                {
                                    _Settings.Logger?.Invoke(_Header + "no authentication material for " + client.IpPort);
                                    byte[] data = Encoding.UTF8.GetBytes("No authentication material");
                                    _Events.HandleAuthenticationFailed(this, new AuthenticationFailedEventArgs(client.IpPort));
                                    WatsonCommon.BytesToStream(data, out long contentLength, out Stream stream);
                                    WatsonMessage authMsg = new WatsonMessage(null, contentLength, stream, false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
                                    authMsg.Status = MessageStatus.AuthFailure;
                                    SendInternal(client, authMsg, 0, null);
                                    continue;
                                }
                            }
                            else
                            {
                                // decline the message
                                _Settings.Logger?.Invoke(_Header + "no authentication material for " + client.IpPort);
                                byte[] data = Encoding.UTF8.GetBytes("Authentication required");
                                _Events.HandleAuthenticationRequested(this, new AuthenticationRequestedEventArgs(client.IpPort));
                                WatsonCommon.BytesToStream(data, out long contentLength, out Stream stream);
                                WatsonMessage authMsg = new WatsonMessage(null, contentLength, stream, false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
                                authMsg.Status = MessageStatus.AuthRequired;
                                SendInternal(client, authMsg, 0, null);
                                continue;
                            }
                        }
                    }

                    if (msg.Status == MessageStatus.Disconnecting)
                    {
                        _Settings.Logger?.Invoke(_Header + "received notification of disconnection from " + client.IpPort);
                        break;
                    }
                    else if (msg.Status == MessageStatus.Removed)
                    {
                        _Settings.Logger?.Invoke(_Header + "sent notification of removal to " + client.IpPort);
                        break;
                    }
                     
                    if (msg.SyncRequest != null && msg.SyncRequest.Value)
                    { 
                        DateTime expiration = WatsonCommon.GetExpirationTimestamp(msg);
                        byte[] msgData = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize);
                          
                        if (DateTime.Now < expiration)
                        { 
                            SyncRequest syncReq = new SyncRequest(
                                client.IpPort,
                                msg.ConversationGuid,
                                msg.Expiration.Value,
                                msg.Metadata,
                                msgData);
                                 
                            SyncResponse syncResp = _Callbacks.HandleSyncRequestReceived(syncReq);
                            if (syncResp != null)
                            { 
                                WatsonCommon.BytesToStream(syncResp.Data, out long contentLength, out Stream stream);
                                WatsonMessage respMsg = new WatsonMessage(
                                    syncResp.Metadata,
                                    contentLength,
                                    stream,
                                    false,
                                    true,
                                    msg.Expiration.Value,
                                    msg.ConversationGuid,
                                    (_Settings.DebugMessages ? _Settings.Logger : null)); 
                                SendInternal(client, respMsg, contentLength, stream);
                            }
                        }
                        else
                        { 
                            _Settings.Logger?.Invoke(_Header + "expired synchronous request received and discarded from " + client.IpPort);
                        } 
                    }
                    else if (msg.SyncResponse != null && msg.SyncResponse.Value)
                    { 
                        // No need to amend message expiration; it is copied from the request, which was set by this node
                        // DateTime expiration = WatsonCommon.GetExpirationTimestamp(msg); 
                        byte[] msgData = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize);

                        if (DateTime.Now < msg.Expiration.Value)
                        {
                            lock (_SyncResponseLock)
                            {
                                _SyncResponses.Add(msg.ConversationGuid, new SyncResponse(msg.Expiration.Value, msg.Metadata, msgData));
                            }
                        }
                        else
                        {
                            _Settings.Logger?.Invoke(_Header + "expired synchronous response received and discarded from " + client.IpPort);
                        }
                    }
                    else
                    {
                        byte[] msgData = null; 

                        if (_Events.IsUsingMessages)
                        { 
                            msgData = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize); 
                            MessageReceivedFromClientEventArgs mr = new MessageReceivedFromClientEventArgs(client.IpPort, msg.Metadata, msgData);
                            await Task.Run(() => _Events.HandleMessageReceived(this, mr));
                        }
                        else if (_Events.IsUsingStreams)
                        {
                            StreamReceivedFromClientEventArgs sr = null;
                            WatsonStream ws = null;

                            if (msg.ContentLength >= _Settings.MaxProxiedStreamSize)
                            {
                                ws = new WatsonStream(msg.ContentLength, msg.DataStream);
                                sr = new StreamReceivedFromClientEventArgs(client.IpPort, msg.Metadata, msg.ContentLength, ws);
                                // sr = new StreamReceivedFromClientEventArgs(client.IpPort, msg.Metadata, msg.ContentLength, msg.DataStream);
                                // must run synchronously, data exists in the underlying stream
                                _Events.HandleStreamReceived(this, sr); 
                            }
                            else
                            {
                                MemoryStream ms = WatsonCommon.DataStreamToMemoryStream(msg.ContentLength, msg.DataStream, _Settings.StreamBufferSize);
                                ws = new WatsonStream(msg.ContentLength, ms); 
                                sr = new StreamReceivedFromClientEventArgs(client.IpPort, msg.Metadata, msg.ContentLength, ws);
                                // sr = new StreamReceivedFromClientEventArgs(client.IpPort, msg.Metadata, msg.ContentLength, ms);
                                // data has been read, can continue to next message
                                await Task.Run(() => _Events.HandleStreamReceived(this, sr));
                            } 
                        }
                        else
                        {
                            _Settings.Logger?.Invoke(_Header + "event handler not set for either MessageReceived or StreamReceived");
                            break;
                        }
                    }

                    _Statistics.IncrementReceivedMessages();
                    _Statistics.AddReceivedBytes(msg.ContentLength);

                    UpdateClientLastSeen(client.IpPort); 
                }
                catch (ObjectDisposedException)
                {
                    _Settings.Logger?.Invoke(_Header + "client " + client.IpPort + " disposed");
                    break;
                }
                catch (OperationCanceledException)
                {
                    _Settings.Logger?.Invoke(_Header + "cancellation requested");
                    break;
                }
                catch (Exception e)
                { 
                    _Settings.Logger?.Invoke(
                        _Header + "data receiver exception for " + client.IpPort + ":" +
                        Environment.NewLine +
                        SerializationHelper.SerializeJson(e, true) +
                        Environment.NewLine);

                    _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                    break;
                }
            }

            _Settings.Logger?.Invoke(_Header + "data receiver terminated for " + client.IpPort);

            ClientDisconnectedEventArgs cd = null; 
            if (_ClientsKicked.ContainsKey(client.IpPort)) cd = new ClientDisconnectedEventArgs(client.IpPort, DisconnectReason.Kicked); 
            else if (_ClientsTimedout.ContainsKey(client.IpPort)) cd = new ClientDisconnectedEventArgs(client.IpPort, DisconnectReason.Timeout); 
            else cd = new ClientDisconnectedEventArgs(client.IpPort, DisconnectReason.Normal);
            _Events.HandleClientDisconnected(this, cd); 

            DateTime removedTs; 
            _Clients.TryRemove(client.IpPort, out ClientMetadata removedClient);
            _ClientsLastSeen.TryRemove(client.IpPort, out removedTs);
            _ClientsKicked.TryRemove(client.IpPort, out removedTs);
            _ClientsTimedout.TryRemove(client.IpPort, out removedTs); 
            _UnauthenticatedClients.TryRemove(client.IpPort, out removedTs);
            Interlocked.Decrement(ref _Connections);

            _Settings.Logger?.Invoke(_Header + "disposing data receiver for " + client.IpPort);
            client.Dispose();  
        }

        private bool SendInternal(ClientMetadata client, WatsonMessage msg, long contentLength, Stream stream)
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

            client.WriteLock.Wait();

            try
            {
                SendHeaders(client, msg);
                SendDataStream(client, contentLength, stream);

                _Statistics.IncrementSentMessages();
                _Statistics.AddSentBytes(contentLength);
                return true;
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(
                    _Header + "failed to write message to " + client.IpPort + ": " +
                    Environment.NewLine +
                    SerializationHelper.SerializeJson(e, true));

                _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                return false;
            }
            finally
            {
                if (client != null) client.WriteLock.Release();
            }
        }
         
        private async Task<bool> SendInternalAsync(ClientMetadata client, WatsonMessage msg, long contentLength, Stream stream)
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

            await client.WriteLock.WaitAsync();

            try
            {
                await SendHeadersAsync(client, msg);
                await SendDataStreamAsync(client, contentLength, stream);

                _Statistics.IncrementSentMessages();
                _Statistics.AddSentBytes(contentLength);
                return true;
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(
                    _Header + "failed to write message to " + client.IpPort + ": " +
                    Environment.NewLine +
                    SerializationHelper.SerializeJson(e, true));

                _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                return false;
            }
            finally
            {
                if (client != null) client.WriteLock.Release();
            }
        }
         
        private SyncResponse SendAndWaitInternal(ClientMetadata client, WatsonMessage msg, int timeoutMs, long contentLength, Stream stream)
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
             
            client.WriteLock.Wait(); 

            try
            {
                SendHeaders(client, msg);
                SendDataStream(client, contentLength, stream);

                _Statistics.IncrementSentMessages();
                _Statistics.AddSentBytes(contentLength);
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(
                    _Header + "failed to write message to " + client.IpPort + " due to exception: " + 
                    Environment.NewLine +
                    SerializationHelper.SerializeJson(e, true));

                _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                throw;
            }
            finally
            {
                if (client != null) client.WriteLock.Release();  
            }

            SyncResponse ret = GetSyncResponse(msg.ConversationGuid, msg.Expiration.Value); 
            return ret;
        }

        private void SendHeaders(ClientMetadata client, WatsonMessage msg)
        { 
            byte[] headerBytes = msg.HeaderBytes;
            client.DataStream.Write(headerBytes, 0, headerBytes.Length);
            client.DataStream.Flush();
        }

        private async Task SendHeadersAsync(ClientMetadata client, WatsonMessage msg)
        { 
            byte[] headerBytes = msg.HeaderBytes;
            await client.DataStream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await client.DataStream.FlushAsync();
        }
         
        private void SendDataStream(ClientMetadata client, long contentLength, Stream stream)
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
                    client.DataStream.Write(buffer, 0, bytesRead);
                    bytesRemaining -= bytesRead;
                }
            }  

            client.DataStream.Flush();
        }

        private async Task SendDataStreamAsync(ClientMetadata client, long contentLength, Stream stream)
        {
            if (contentLength <= 0) return;

            long bytesRemaining = contentLength;
            int bytesRead = 0;
            byte[] buffer = new byte[_Settings.StreamBufferSize];
             
            while (bytesRemaining > 0)
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    await client.DataStream.WriteAsync(buffer, 0, bytesRead);
                    bytesRemaining -= bytesRead;
                }
            } 

            await client.DataStream.FlushAsync();
        }
          
        private async Task MonitorForIdleClients()
        { 
            while (!_Token.IsCancellationRequested)
            {
                if (_Settings.IdleClientTimeoutSeconds > 0 && _ClientsLastSeen.Count > 0)
                {
                    MonitorForIdleClientsTask();
                }

                await Task.Delay(5000, _Token);
            }
        }

        private void MonitorForIdleClientsTask()
        { 
            DateTime idleTimestamp = DateTime.Now.AddSeconds(-1 * _Settings.IdleClientTimeoutSeconds);

            foreach (KeyValuePair<string, DateTime> curr in _ClientsLastSeen)
            { 
                if (curr.Value < idleTimestamp)
                {
                    _ClientsTimedout.TryAdd(curr.Key, DateTime.Now);
                    _Settings.Logger?.Invoke(_Header + "disconnecting client " + curr.Key + " due to idle timeout");
                    DisconnectClient(curr.Key);
                }
            }  
        }

        private void UpdateClientLastSeen(string ipPort)
        {
            _ClientsLastSeen.AddOrUpdate(ipPort, DateTime.Now, (key, value) => DateTime.Now);
        }
         
        private async Task MonitorForExpiredSyncResponses()
        {
            while (!_TokenSource.IsCancellationRequested)
            {
                if (_Token.IsCancellationRequested) break;

                await Task.Delay(1000);

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
                            _Settings.Logger?.Invoke(_Header + "expiring response " + curr.Key.ToString());
                            _SyncResponses.Remove(curr.Key);
                        }
                    }
                }
            }
        }

        private SyncResponse GetSyncResponse(string guid, DateTime expirationUtc)
        {
            SyncResponse ret = null;

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
                Task.Delay(50).Wait();
            }

            if (ret != null) return ret;
            else throw new TimeoutException("A response to a synchronous request was not received within the timeout window.");
        }

        private void EnableKeepalives()
        {
            try
            {
#if NETCOREAPP

                _Listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                _Listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _Keepalive.TcpKeepAliveTime);
                _Listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _Keepalive.TcpKeepAliveInterval);
                _Listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _Keepalive.TcpKeepAliveRetryCount);

#elif NETFRAMEWORK

                byte[] keepAlive = new byte[12];

                // Turn keepalive on
                Buffer.BlockCopy(BitConverter.GetBytes((uint)1), 0, keepAlive, 0, 4);

                // Set TCP keepalive time
                Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveTime), 0, keepAlive, 4, 4); 

                // Set TCP keepalive interval
                Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveInterval), 0, keepAlive, 8, 4); 

                // Set keepalive settings on the underlying Socket
                _Listener.Server.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);

#elif NETSTANDARD

#endif
            }
            catch (Exception)
            {
                _Settings.Logger?.Invoke(_Header + "keepalives not supported on this platform, disabled");
            }
        }

        #endregion
    }
}
