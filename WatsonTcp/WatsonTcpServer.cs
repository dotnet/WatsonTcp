﻿using System;
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
using System.Security.Cryptography;
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
        /// Buffer size to use when reading input and output streams.  Default is 65536.
        /// </summary>
        public int StreamBufferSize
        {
            get
            {
                return _ReadStreamBufferSize;
            }
            set
            {
                if (value < 1) throw new ArgumentException("Read stream buffer size must be greater than zero.");
                _ReadStreamBufferSize = value;
            }
        }

        /// <summary>
        /// Maximum amount of time to wait before considering a client idle and disconnecting them. 
        /// By default, this value is set to 0, which will never disconnect a client due to inactivity.
        /// The timeout is reset any time a message is received from a client or a message is sent to a client.
        /// For instance, if you set this value to 30, the client will be disconnected if the server has not received a message from the client within 30 seconds or if a message has not been sent to the client in 30 seconds.
        /// </summary>
        public int IdleClientTimeoutSeconds
        {
            get
            {
                return _IdleClientTimeoutSeconds;
            }
            set
            {
                if (value < 0) throw new ArgumentException("IdleClientTimeoutSeconds must be zero or greater.");
                _IdleClientTimeoutSeconds = value;
            }
        }
        
        /// <summary>
        /// Specify the maximum number of connections the server will accept.
        /// </summary>
        public int MaxConnections
        {
            get
            {
                return _MaxConnections;
            }
            set
            {
                if (value < 1) throw new ArgumentException("Max connections must be greater than zero.");
                _MaxConnections = value;
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

        /// <summary>
        /// Enable or disable message debugging.  Requires `Logger` to be set.
        /// WARNING: Setting this value to true will emit a large number of log messages with a large amount of data.
        /// </summary>
        public bool DebugMessages = false;

        /// <summary>
        /// Permitted IP addresses.
        /// </summary>
        public List<string> PermittedIPs = new List<string>();

        /// <summary>
        /// Event to fire when a client connects to the server.
        /// The IP:port of the client is passed in the arguments.
        /// </summary>
        public event EventHandler<ClientConnectedEventArgs> ClientConnected;

        /// <summary>
        /// Event to fire when a client disconnects from the server.
        /// The IP:port is passed in the arguments along with the reason for the disconnection.
        /// </summary>
        public event EventHandler<ClientDisconnectedEventArgs> ClientDisconnected;

        /// <summary>
        /// Use of 'MessageReceived' is exclusive and cannot be used with 'StreamReceived'.  
        /// This event is fired when a message is received from a client and it is desired that WatsonTcp pass the byte array containing the message payload. 
        /// </summary>
        public event EventHandler<MessageReceivedFromClientEventArgs> MessageReceived
        {
            add
            {
                if (_StreamReceived != null 
                    && _StreamReceived.GetInvocationList().Length > 0) 
                    throw new InvalidOperationException("Only one of 'MessageReceived' and 'StreamReceived' can be set.");
                _MessageReceived += value;
            }
            remove
            {
                _MessageReceived -= value;
            }
        }

        /// <summary>
        /// Use of 'StreamReceived' is exclusive and cannot be used with 'StreamReceived'.  
        /// This event is fired when a stream is received from a client and it is desired that WatsonTcp pass the stream containing the message payload to your application. 
        /// </summary>
        public event EventHandler<StreamReceivedFromClientEventArgs> StreamReceived
        {
            add
            {
                if (_MessageReceived != null 
                    && _MessageReceived.GetInvocationList().Length > 0) 
                    throw new InvalidOperationException("Only one of 'MessageReceived' and 'StreamReceived' can be set.");
                _StreamReceived += value;
            }
            remove
            {
                _StreamReceived -= value;
            }
        }

        /// <summary>
        /// Callback to invoke when receiving a synchronous request that demands a response.
        /// </summary>
        public Func<SyncRequest, SyncResponse> SyncRequestReceived
        {
            get
            {
                return _SyncRequestReceived;
            }
            set
            {
                _SyncRequestReceived = value;
            }
        }

        /// <summary>
        /// Enable acceptance of SSL certificates from clients that cannot be validated.
        /// </summary>
        public bool AcceptInvalidCertificates = true;

        /// <summary>
        /// Require mutual authentication between SSL clients and this server.
        /// </summary>
        public bool MutuallyAuthenticate = false;

        /// <summary>
        /// Preshared key that must be consistent between clients and this server.
        /// </summary>
        public string PresharedKey = null;

        /// <summary>
        /// Type of compression to apply on sent messages.
        /// </summary>
        public CompressionType Compression = CompressionType.None;

        /// <summary>
        /// Type of encryption to apply on sent messages.
        /// </summary>
        public EncryptionType Encryption = EncryptionType.None;

        /// <summary>
        /// Passphrase that must be consistent between clients and this server for encrypted communication.
        /// </summary>
        public string EncryptionPassphrase = null;

        /// <summary>
        /// Method to invoke when sending a log message.
        /// </summary>
        public Action<string> Logger = null;

        /// <summary>
        /// Access Watson TCP statistics.
        /// </summary>
        public Statistics Stats
        {
            get
            {
                return _Stats;
            }
        }

        #endregion

        #region Private-Members

        private int _ReadStreamBufferSize = 65536;
        private int _MaxConnections = 4096;
        private int _Connections = 0;
        private bool _IsListening = false;
        private int _IdleClientTimeoutSeconds = 0;
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

        private event EventHandler<MessageReceivedFromClientEventArgs> _MessageReceived;
        private event EventHandler<StreamReceivedFromClientEventArgs> _StreamReceived;
        private Func<SyncRequest, SyncResponse> _SyncRequestReceived = null;
         
        private readonly object _SyncResponseLock = new object();
        private Dictionary<string, SyncResponse> _SyncResponses = new Dictionary<string, SyncResponse>();
         
        private Statistics _Stats = new Statistics();
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
        /// Start the server.
        /// </summary>
        public void Start()
        {
            _Stats = new Statistics();

            if (_StreamReceived == null && _MessageReceived == null)
            {
                throw new InvalidOperationException("Either 'MessageReceived' or 'StreamReceived' must first be set.");
            }

            if (_Mode == Mode.Tcp)
            {
                Logger?.Invoke("[WatsonTcpServer] Starting on " + _ListenerIp + ":" + _ListenerPort);
            }
            else if (_Mode == Mode.Ssl)
            {
                Logger?.Invoke("[WatsonTcpServer] Starting with SSL on " + _ListenerIp + ":" + _ListenerPort);
            }
            else
            {
                throw new ArgumentException("Unknown mode: " + _Mode.ToString());
            }

            Task.Run(() => AcceptConnections(), _Token); 
        }

        /// <summary>
        /// Start the server.
        /// </summary>
        public Task StartAsync()
        {
            _Stats = new Statistics();

            if (_StreamReceived == null && _MessageReceived == null)
            {
                throw new InvalidOperationException("Either 'MessageReceived' or 'StreamReceived' must first be set.");
            }

            if (_Mode == Mode.Tcp)
            {
                Logger?.Invoke("[WatsonTcpServer] Starting on " + _ListenerIp + ":" + _ListenerPort); 
            }
            else if (_Mode == Mode.Ssl)
            {
                Logger?.Invoke("[WatsonTcpServer] Starting with SSL on " + _ListenerIp + ":" + _ListenerPort);
            }
            else
            {
                throw new ArgumentException("Unknown mode: " + _Mode.ToString());
            }

            return AcceptConnections();
        }

        /// <summary>
        /// Send data to the specified client.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">String containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(string ipPort, string data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (String.IsNullOrEmpty(data)) return Send(ipPort, new byte[0]);
            else return Send(ipPort, Encoding.UTF8.GetBytes(data));
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
            if (String.IsNullOrEmpty(data)) return Send(ipPort, new byte[0]);
            else return Send(ipPort, metadata, Encoding.UTF8.GetBytes(data));
        }

        /// <summary>
        /// Send data to the specified client.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(string ipPort, byte[] data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort); 
                return false;
            }

            if (data == null) data = new byte[0];
            BytesToStream(data, out long contentLength, out Stream stream);
            return Send(ipPort, contentLength, stream);
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
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort);
                return false;
            }

            if (data == null) data = new byte[0];
            BytesToStream(data, out long contentLength, out Stream stream);
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
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort);
                return false;
            }

            if (stream == null) stream = new MemoryStream(new byte[0]);
            WatsonMessage msg = new WatsonMessage(null, contentLength, stream, false, false, null, null, Compression, Encryption, (DebugMessages ? Logger : null));
            return SendInternal(client, msg, contentLength, stream);
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
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort);
                return false;
            }

            if (stream == null) stream = new MemoryStream(new byte[0]);
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, false, false, null, null, Compression, Encryption, (DebugMessages ? Logger : null));
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
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort);
                return false;
            }

            WatsonMessage msg = new WatsonMessage(metadata, 0, new MemoryStream(new byte[0]), false, false, null, null, Compression, Encryption, (DebugMessages ? Logger : null));
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
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (String.IsNullOrEmpty(data)) return await SendAsync(ipPort, new byte[0]);
            else return await SendAsync(ipPort, Encoding.UTF8.GetBytes(data));
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
            if (String.IsNullOrEmpty(data)) return await SendAsync(ipPort, new byte[0]);
            else return await SendAsync(ipPort, metadata, Encoding.UTF8.GetBytes(data));
        }

        /// <summary>
        /// Send data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, byte[] data)
        {
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort);
                return false;
            }

            if (data == null) data = new byte[0];
            BytesToStream(data, out long contentLength, out Stream stream);
            return await SendAsync(ipPort, null, contentLength, stream);
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
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort);
                return false;
            }

            if (data == null) data = new byte[0];
            BytesToStream(data, out long contentLength, out Stream stream);
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
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort);
                return false;
            }

            if (stream == null) stream = new MemoryStream(new byte[0]);
            WatsonMessage msg = new WatsonMessage(null, contentLength, stream, false, false, null, null, Compression, Encryption, (DebugMessages ? Logger : null));
            return await SendInternalAsync(client, msg, contentLength, stream);
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
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort);
                return false;
            }

            if (stream == null) stream = new MemoryStream(new byte[0]);
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, false, false, null, null, Compression, Encryption, (DebugMessages ? Logger : null));
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
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort);
                return false;
            }
            
            WatsonMessage msg = new WatsonMessage(metadata, 0, new MemoryStream(new byte[0]), false, false, null, null, Compression, Encryption, (DebugMessages ? Logger : null));
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
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (String.IsNullOrEmpty(data)) return SendAndWait(ipPort, null, timeoutMs, new byte[0]);
            return SendAndWait(ipPort, null, timeoutMs, Encoding.UTF8.GetBytes(data));
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
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (data == null) data = new byte[0];
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
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (stream == null) stream = new MemoryStream(new byte[0]);
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
            if (String.IsNullOrEmpty(ipPort)) throw new ArgumentNullException(nameof(ipPort));
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (String.IsNullOrEmpty(data)) return SendAndWait(ipPort, metadata, timeoutMs, new byte[0]);
            return SendAndWait(ipPort, metadata, timeoutMs, Encoding.UTF8.GetBytes(data));
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
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort);
                throw new KeyNotFoundException("Unable to find client " + ipPort + ".");
            }
            if (data == null) data = new byte[0];
            BytesToStream(data, out long contentLength, out Stream stream);
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
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort);
                throw new KeyNotFoundException("Unable to find client " + ipPort + ".");
            }
            if (stream == null) stream = new MemoryStream(new byte[0]);
            DateTime expiration = DateTime.Now.AddMilliseconds(timeoutMs);
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, true, false, expiration, Guid.NewGuid().ToString(), Compression, Encryption, (DebugMessages ? Logger : null));
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
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort);
                throw new KeyNotFoundException("Unable to find client " + ipPort + ".");
            }
            DateTime expiration = DateTime.Now.AddMilliseconds(timeoutMs);
            WatsonMessage msg = new WatsonMessage(metadata, 0, new MemoryStream(new byte[0]), true, false, expiration, Guid.NewGuid().ToString(), Compression, Encryption, (DebugMessages ? Logger : null));
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
                Logger?.Invoke("[WatsonTcpServer] Unable to find client " + ipPort);
            }
            else
            {
                byte[] data = null;

                if (_ClientsTimedout.ContainsKey(ipPort))
                {
                    data = Encoding.UTF8.GetBytes("Removed from server due to timeout.");
                }
                else
                {
                    data = Encoding.UTF8.GetBytes("Removed from server.");
                    _ClientsKicked.TryAdd(ipPort, DateTime.Now); 
                }

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
                Logger?.Invoke("[WatsonTcpClient] Disposing");

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

                Logger?.Invoke("[WatsonTcpServer] Dispose complete");
            }
        }

        private void LogException(string method, Exception e)
        {
            Logger?.Invoke(
                "[WatsonTcpServer] Exception encountered: " + Environment.NewLine +
                "   Method     : " + method + Environment.NewLine +
                "   Type       : " + e.GetType().ToString() + Environment.NewLine +
                "   Data       : " + e.Data + Environment.NewLine +
                "   Inner      : " + e.InnerException + Environment.NewLine +
                "   Message    : " + e.Message + Environment.NewLine +
                "   Source     : " + e.Source + Environment.NewLine +
                "   StackTrace : " + e.StackTrace);
        }

        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // return true; // Allow untrusted certificates.
            return AcceptInvalidCertificates;
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

                    if (!_IsListening && (_Connections >= _MaxConnections))
                    {
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
                    if (PermittedIPs.Count > 0 && !PermittedIPs.Contains(clientIp))
                    {
                        Logger?.Invoke("[WatsonTcpServer] Rejecting connection from " + clientIp + " (not permitted)");
                        tcp.Close();
                        continue;
                    }

                    ClientMetadata client = new ClientMetadata(tcp);
                    ipPort = client.IpPort;

                    #endregion Accept-Connection-and-Validate-IP

                    #region Check-for-Maximum-Connections

                    _Connections++; 
                    if (_Connections >= _MaxConnections)
                    {
                        Logger?.Invoke("[WatsonTcpServer] Maximum connections " + _MaxConnections + " met or exceeded (currently " + _Connections + " connections), pausing listener");
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
                        if (AcceptInvalidCertificates)
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

                    Logger?.Invoke("[WatsonTcpServer] Accepted connection from " + client.IpPort);

                    #endregion 
                }
                catch (ObjectDisposedException)
                {
                    Logger?.Invoke("[WatsonTcpServer] Disposal detected, closing connection listener");
                }
                catch (Exception e)
                {
                    Logger?.Invoke("[WatsonTcpServer] Exception for " + ipPort + ": " + e.Message);
                }
            }
        }

        private async Task<bool> StartTls(ClientMetadata client)
        {
            try
            { 
                await client.SslStream.AuthenticateAsServerAsync(_SslCertificate, true, SslProtocols.Tls12, !AcceptInvalidCertificates);

                if (!client.SslStream.IsEncrypted)
                {
                    Logger?.Invoke("[WatsonTcpServer] Stream from " + client.IpPort + " not encrypted");
                    client.Dispose();
                    _Connections--;
                    return false;
                }

                if (!client.SslStream.IsAuthenticated)
                {
                    Logger?.Invoke("[WatsonTcpServer] Stream from " + client.IpPort + " not authenticated");
                    client.Dispose();
                    _Connections--;
                    return false;
                }

                if (MutuallyAuthenticate && !client.SslStream.IsMutuallyAuthenticated)
                {
                    Logger?.Invoke("[WatsonTcpServer] Stream from " + client.IpPort + " failed mutual authentication");
                    client.Dispose();
                    _Connections--;
                    return false;
                }
            }
            catch (IOException ex)
            {
                // Some type of problem initiating the SSL connection
                switch (ex.Message)
                {
                    case "Authentication failed because the remote party has closed the transport stream.":
                    case "Unable to read data from the transport connection: An existing connection was forcibly closed by the remote host.":
                        Logger?.Invoke("[WatsonTcpServer] Connection closed by " + client.IpPort + " during SSL negotiation");
                        break;

                    case "The handshake failed due to an unexpected packet format.":
                        Logger?.Invoke("[WatsonTcpServer] Disconnected " + client.IpPort + " due to invalid handshake");
                        break;

                    default:
                        Logger?.Invoke(
                            "[WatsonTcpServer] Disconnected " + client.IpPort + " due to TLS exception: " +
                            Environment.NewLine +
                            SerializationHelper.SerializeJson(ex, true));
                        break;
                }

                client.Dispose();
                _Connections--;
                return false;
            }
            catch (Exception ex)
            {
                Logger?.Invoke("[WatsonTcpServer] Exception on " + client.IpPort + " during TLS negotiation: " + Environment.NewLine + ex.ToString());
                client.Dispose();
                _Connections--;
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

            if (!String.IsNullOrEmpty(PresharedKey))
            {
                Logger?.Invoke("[WatsonTcpServer] Soliciting authentication material from " + client.IpPort);
                _UnauthenticatedClients.TryAdd(client.IpPort, DateTime.Now);

                byte[] data = Encoding.UTF8.GetBytes("Authentication required");
                WatsonMessage authMsg = new WatsonMessage();
                authMsg.Status = MessageStatus.AuthRequired; 
                SendInternal(client, authMsg, 0, null);
            }

            #endregion

            #region Start-Data-Receiver

            Logger?.Invoke("[WatsonTcpServer] Starting data receiver for " + client.IpPort);
            if (ClientConnected != null)
            {
                ClientConnected?.Invoke(this, new ClientConnectedEventArgs(client.IpPort));
            }

            Task.Run(async () => await DataReceiver(client, client.Token));

            #endregion
        }

        private bool IsConnected(ClientMetadata client)
        {
            if (client.TcpClient.Connected)
            {
                byte[] tmp = new byte[1];
                bool success = false;
                bool sendLocked = false;
                bool readLocked = false;

                try
                {
                    client.WriteLock.Wait(1);
                    sendLocked = true;
                    client.TcpClient.Client.Send(tmp, 0, 0);
                    success = true;
                }
                catch (ObjectDisposedException)
                {
                }
                catch (IOException)
                {
                }
                catch (SocketException se)
                {
                    if (se.NativeErrorCode.Equals(10035)) success = true;
                }
                catch (Exception e)
                {
                    Logger?.Invoke("[WatsonTcpServer] Exception while testing connection to " + client.IpPort + " using send: " + e.Message);
                    success = false;
                }
                finally
                {
                    if (sendLocked) client.WriteLock.Release();
                }

                if (success) return true;

                try
                {
                    client.ReadLock.Wait(1);
                    readLocked = true;

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
                catch (Exception e)
                {
                    Logger?.Invoke("[WatsonTcpServer] Exception while testing connection to " + client.IpPort + " using poll/peek: " + e.Message);;
                    return false;
                }
                finally
                {
                    if (readLocked) client.ReadLock.Release();
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

                    WatsonMessage msg = new WatsonMessage(client.DataStream, (DebugMessages ? Logger : null));
                    bool buildSuccess = await msg.BuildFromStream();
                    if (!buildSuccess)
                    {
                        Logger?.Invoke("[WatsonTcpServer] Message build failed due to disconnect for client " + client.IpPort);
                        break;
                    }

                    if (msg == null)
                    {
                        await Task.Delay(30);
                        continue;
                    }
                     
                    if (!String.IsNullOrEmpty(PresharedKey))
                    {
                        if (_UnauthenticatedClients.ContainsKey(client.IpPort))
                        {
                            Logger?.Invoke("[WatsonTcpServer] Message received from unauthenticated endpoint " + client.IpPort);

                            if (msg.Status == MessageStatus.AuthRequested)
                            {
                                // check preshared key
                                if (msg.PresharedKey != null && msg.PresharedKey.Length > 0)
                                {
                                    string clientPsk = Encoding.UTF8.GetString(msg.PresharedKey).Trim();
                                    if (PresharedKey.Trim().Equals(clientPsk))
                                    {
                                        Logger?.Invoke("[WatsonTcpServer] Accepted authentication for " + client.IpPort);
                                        _UnauthenticatedClients.TryRemove(client.IpPort, out DateTime dt);
                                        byte[] data = Encoding.UTF8.GetBytes("Authentication successful");
                                        BytesToStream(data, out long contentLength, out Stream stream);
                                        WatsonMessage authMsg = new WatsonMessage(null, contentLength, stream, false, false, null, null, CompressionType.None, EncryptionType.None, (DebugMessages ? Logger : null));
                                        authMsg.Status = MessageStatus.AuthSuccess;
                                        SendInternal(client, authMsg, 0, null);
                                        continue;
                                    }
                                    else
                                    {
                                        Logger?.Invoke("[WatsonTcpServer] Declined authentication for " + client.IpPort);
                                        byte[] data = Encoding.UTF8.GetBytes("Authentication declined");
                                        BytesToStream(data, out long contentLength, out Stream stream);
                                        WatsonMessage authMsg = new WatsonMessage(null, contentLength, stream, false, false, null, null, CompressionType.None, EncryptionType.None, (DebugMessages ? Logger : null));
                                        authMsg.Status = MessageStatus.AuthFailure;
                                        SendInternal(client, authMsg, 0, null);
                                        continue;
                                    }
                                }
                                else
                                {
                                    Logger?.Invoke("[WatsonTcpServer] No authentication material for " + client.IpPort);
                                    byte[] data = Encoding.UTF8.GetBytes("No authentication material");
                                    BytesToStream(data, out long contentLength, out Stream stream);
                                    WatsonMessage authMsg = new WatsonMessage(null, contentLength, stream, false, false, null, null, CompressionType.None, EncryptionType.None, (DebugMessages ? Logger : null));
                                    authMsg.Status = MessageStatus.AuthFailure;
                                    SendInternal(client, authMsg, 0, null);
                                    continue;
                                }
                            }
                            else
                            {
                                // decline the message
                                Logger?.Invoke("[WatsonTcpServer] No authentication material for " + client.IpPort);
                                byte[] data = Encoding.UTF8.GetBytes("Authentication required");
                                BytesToStream(data, out long contentLength, out Stream stream);
                                EncryptionInfo encryptionInfo = new EncryptionInfo(Encryption);
                                WatsonMessage authMsg = new WatsonMessage(null, contentLength, stream, false, false, null, null, CompressionType.None, EncryptionType.None, (DebugMessages ? Logger : null));
                                authMsg.Status = MessageStatus.AuthRequired;
                                SendInternal(client, authMsg, 0, null);
                                continue;
                            }
                        }
                    }

                    if (msg.Status == MessageStatus.Disconnecting)
                    {
                        Logger?.Invoke("[WatsonTcpServer] Received notification of disconnection from " + client.IpPort);
                        break;
                    }
                    else if (msg.Status == MessageStatus.Removed)
                    {
                        Logger?.Invoke("[WatsonTcpServer] Sent notification of removal to " + client.IpPort);
                        break;
                    }

                    byte[] msgData = msg.Data;
                        
                    if (Encryption == EncryptionType.None)
                    {
                        // do nothing
                    }
                    else
                    {
                        if (String.IsNullOrEmpty(EncryptionPassphrase))
                        {
                            throw new ArgumentNullException(EncryptionPassphrase);
                        }

                        byte[] key = Encoding.UTF8.GetBytes(EncryptionPassphrase);
                        byte[] salt = Encoding.UTF8.GetBytes(msg.Encryption.Salt);
                        if (key.Length > 32)
                        {
                            throw new ArgumentException("EncryptionPassphrase must be 32 bytes or greater.");
                        }
                        
                        if (Encryption == EncryptionType.Aes)
                        {
                            byte[] decryptedData = EncryptionHelper.Decrypt<AesCryptoServiceProvider>(msgData, key, salt);
                            msgData = decryptedData;
                        }
                        else if (Encryption == EncryptionType.TripleDes)
                        {
                            byte[] decryptedData = EncryptionHelper.Decrypt<TripleDESCryptoServiceProvider>(msgData, key, salt);
                            msgData = decryptedData;
                        }
                        else
                        {
                            throw new InvalidOperationException("Unknown encryption type: " + Encryption.ToString());
                        }
                    }
                    
                    if (msg.SyncRequest)
                    { 
                        if (SyncRequestReceived != null)
                        { 
                            if (DateTime.Now < msg.Expiration.Value)
                            {
                                SyncRequest syncReq = new SyncRequest(
                                    client.IpPort,
                                    msg.ConversationGuid,
                                    msg.Expiration.Value,
                                    msg.Metadata,
                                    msgData);

                                SyncResponse syncResp = SyncRequestReceived(syncReq);
                                if (syncResp != null)
                                {
                                    BytesToStream(msgData, out long contentLength, out Stream stream);
                                    EncryptionInfo encryptionInfo = new EncryptionInfo(Encryption);
                                    WatsonMessage respMsg = new WatsonMessage(
                                        syncResp.Metadata,
                                        contentLength,
                                        stream,
                                        false,
                                        true,
                                        msg.Expiration.Value,
                                        msg.ConversationGuid,
                                        Compression,
                                        Encryption,
                                        (DebugMessages ? Logger : null)); 
                                    SendInternal(client, respMsg, contentLength, stream);
                                }
                            }
                            else
                            {
                                Logger?.Invoke("[WatsonTcpServer] Expired synchronous request received and discarded from " + client.IpPort);
                            }
                        } 
                    }
                    else if (msg.SyncResponse)
                    { 
                        if (DateTime.Now < msg.Expiration.Value)
                        {
                            lock (_SyncResponseLock)
                            {
                                _SyncResponses.Add(msg.ConversationGuid, new SyncResponse(msg.Expiration.Value, msg.Metadata, msgData));
                            }
                        }
                        else
                        {
                            Logger?.Invoke("[WatsonTcpServer] Expired synchronous response received and discarded from " + client.IpPort);
                        }
                    }
                    else
                    {
                        if (_MessageReceived != null
                            && _MessageReceived.GetInvocationList().Length > 0)
                        {
                            MessageReceivedFromClientEventArgs mr = new MessageReceivedFromClientEventArgs(client.IpPort, msg.Metadata, msgData);
                            _MessageReceived?.Invoke(this, mr);
                        }
                        else if (_StreamReceived != null
                                 && _StreamReceived.GetInvocationList().Length > 0)
                        {
                            BytesToStream(msgData, out long contentLength, out Stream msgStream);
                            
                            StreamReceivedFromClientEventArgs sr = new StreamReceivedFromClientEventArgs(client.IpPort, msg.Metadata, contentLength, msgStream);
                            _StreamReceived?.Invoke(this, sr);
                        }
                        else
                        {
                            Logger?.Invoke("[WatsonTcpServer] Event handler not set for either MessageReceived or StreamReceived");
                            break;
                        }
                    }
                     
                    _Stats.ReceivedMessages = _Stats.ReceivedMessages + 1;
                    _Stats.ReceivedBytes += msg.ContentLength;
                    UpdateClientLastSeen(client.IpPort);
                }
                catch (OperationCanceledException)
                {
                    Logger?.Invoke("[WatsonTcpServer] Cancellation requested");
                }
                catch (Exception e)
                { 
                    Logger?.Invoke(
                        "[WatsonTcpServer] Data receiver exception for " + client.IpPort + ":" +
                        Environment.NewLine +
                        SerializationHelper.SerializeJson(e, true) +
                        Environment.NewLine);
                    break;
                }
            }

            Logger?.Invoke("[WatsonTcpServer] Data receiver terminated for " + client.IpPort);

            ClientDisconnectedEventArgs cd = null;
            if (ClientDisconnected != null)
            { 
                if (_ClientsKicked.ContainsKey(client.IpPort))
                {
                    cd = new ClientDisconnectedEventArgs(client.IpPort, DisconnectReason.Kicked);
                }
                else if (_ClientsTimedout.ContainsKey(client.IpPort))
                {
                    cd = new ClientDisconnectedEventArgs(client.IpPort, DisconnectReason.Timeout);
                }
                else
                {
                    cd = new ClientDisconnectedEventArgs(client.IpPort, DisconnectReason.Normal);
                }
            }

            DateTime removedTs; 
            _Clients.TryRemove(client.IpPort, out ClientMetadata removedClient);
            _ClientsLastSeen.TryRemove(client.IpPort, out removedTs);
            _ClientsKicked.TryRemove(client.IpPort, out removedTs);
            _ClientsTimedout.TryRemove(client.IpPort, out removedTs); 
            _UnauthenticatedClients.TryRemove(client.IpPort, out removedTs);
            _Connections--;

            if (cd != null)
            {
                ClientDisconnected?.Invoke(this, cd);
            }
            
            Logger?.Invoke("[WatsonTcpServer] Disposing data receiver for " + client.IpPort);
            client.Dispose();  
        }

        private void BytesToStream(byte[] data, out long contentLength, out Stream stream)
        {
            contentLength = 0;
            stream = new MemoryStream(new byte[0]);

            if (data != null && data.Length > 0)
            {
                contentLength = data.Length;
                stream = new MemoryStream();
                stream.Write(data, 0, data.Length);
                stream.Seek(0, SeekOrigin.Begin);
            }
        }

        private byte[] ReadStreamFully(Stream input)
        {
            byte[] buffer = new byte[65536];
            using (MemoryStream ms = new MemoryStream())
            {
                int read = 0;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                return ms.ToArray();
            }
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

            if (Encryption == EncryptionType.None)
            {
                // do nothing
            }
            else
            {
                if (String.IsNullOrEmpty(EncryptionPassphrase))
                {
                    throw new ArgumentNullException(EncryptionPassphrase);
                }
                    
                byte[] key = Encoding.UTF8.GetBytes(EncryptionPassphrase);
                byte[] salt = Encoding.UTF8.GetBytes(msg.Encryption.Salt);
                if (key.Length > 32)
                {
                    throw new ArgumentException("EncryptionPassphrase must be 32 bytes or greater.");
                }
                
                if (Encryption == EncryptionType.Aes)
                {
                    byte[] streamData = ReadStreamFully(stream);
                    byte[] aesData = EncryptionHelper.Encrypt<AesCryptoServiceProvider>(streamData, key, salt);
                    
                    BytesToStream(aesData, out contentLength, out stream);

                    msg.ContentLength = contentLength;
                }
                else if (Encryption == EncryptionType.TripleDes)
                {
                    byte[] streamData = ReadStreamFully(stream);
                    byte[] aesData = EncryptionHelper.Encrypt<TripleDESCryptoServiceProvider>(streamData, key, salt);
                    
                    BytesToStream(aesData, out contentLength, out stream);

                    msg.ContentLength = contentLength;
                }
                else
                {
                    throw new InvalidOperationException("Unknown encryption type: " + Encryption.ToString());
                }
            }

            try
            {
                SendHeaders(client, msg);
                SendDataStream(client, contentLength, stream); 

                _Stats.SentMessages += 1;
                _Stats.SentBytes += contentLength;
                return true;
            }
            catch (Exception e)
            {
                Logger?.Invoke("[WatsonTcpServer] Failed to write message to " + client.IpPort + " due to exception: " + e.Message);
                return false;
            }
            finally
            {
                if (client != null && client.WriteLock != null)
                {
                    client.WriteLock.Release();
                }
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
            
            if (Encryption == EncryptionType.None)
            {
                // do nothing
            }
            else
            {
                if (String.IsNullOrEmpty(EncryptionPassphrase))
                {
                    throw new ArgumentNullException(EncryptionPassphrase);
                }
                    
                byte[] key = Encoding.UTF8.GetBytes(EncryptionPassphrase);
                byte[] salt = Encoding.UTF8.GetBytes(msg.Encryption.Salt);
                if (key.Length > 32)
                {
                    throw new ArgumentException("EncryptionPassphrase must be 32 bytes or greater.");
                }
                
                if (Encryption == EncryptionType.Aes)
                {
                    byte[] streamData = ReadStreamFully(stream);
                    byte[] aesData = EncryptionHelper.Encrypt<AesCryptoServiceProvider>(streamData, key, salt);
                    
                    BytesToStream(aesData, out contentLength, out stream);

                    msg.ContentLength = contentLength;
                }
                else if (Encryption == EncryptionType.TripleDes)
                {
                    byte[] streamData = ReadStreamFully(stream);
                    byte[] aesData = EncryptionHelper.Encrypt<TripleDESCryptoServiceProvider>(streamData, key, salt);
                    
                    BytesToStream(aesData, out contentLength, out stream);

                    msg.ContentLength = contentLength;
                }
                else
                {
                    throw new InvalidOperationException("Unknown encryption type: " + Encryption.ToString());
                }
            }

            try
            {
                await SendHeadersAsync(client, msg);
                await SendDataStreamAsync(client, contentLength, stream);

                _Stats.SentMessages += 1;
                _Stats.SentBytes += contentLength;
                return true;
            }
            catch (Exception e)
            {
                Logger?.Invoke("[WatsonTcpServer] Failed to write message to " + client.IpPort + " due to exception: " + e.Message);
                return false;
            }
            finally
            {
                client.WriteLock.Release();
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

            if (Encryption == EncryptionType.None)
            {
                // do nothing
            }
            else
            {
                if (String.IsNullOrEmpty(EncryptionPassphrase))
                {
                    throw new ArgumentNullException(EncryptionPassphrase);
                }
                    
                byte[] key = Encoding.UTF8.GetBytes(EncryptionPassphrase);
                byte[] salt = Encoding.UTF8.GetBytes(msg.Encryption.Salt);
                if (key.Length > 32)
                {
                    throw new ArgumentException("EncryptionPassphrase must be 32 bytes or greater.");
                }
                
                if (Encryption == EncryptionType.Aes)
                {
                    byte[] streamData = ReadStreamFully(stream);
                    byte[] aesData = EncryptionHelper.Encrypt<AesCryptoServiceProvider>(streamData, key, salt);
                    
                    BytesToStream(aesData, out contentLength, out stream);

                    msg.ContentLength = contentLength;
                }
                else if (Encryption == EncryptionType.TripleDes)
                {
                    byte[] streamData = ReadStreamFully(stream);
                    byte[] aesData = EncryptionHelper.Encrypt<TripleDESCryptoServiceProvider>(streamData, key, salt);
                    
                    BytesToStream(aesData, out contentLength, out stream);

                    msg.ContentLength = contentLength;
                }
                else
                {
                    throw new InvalidOperationException("Unknown encryption type: " + Encryption.ToString());
                }
            }
            
            try
            {
                SendHeaders(client, msg);
                SendDataStream(client, contentLength, stream);

                _Stats.SentMessages += 1;
                _Stats.SentBytes += contentLength; 
            }
            catch (Exception e)
            {
                Logger?.Invoke("[WatsonTcpServer] Failed to write message to " + client.IpPort + " due to exception: " + e.Message);
                throw;
            }
            finally
            {
                client.WriteLock.Release();
            }

            SyncResponse ret = GetSyncResponse(msg.ConversationGuid, msg.Expiration.Value); 
            return ret;
        }

        private void SendHeaders(ClientMetadata client, WatsonMessage msg)
        { 
            byte[] headerBytes = msg.HeaderBytes;
            client.DataStream.Write(headerBytes, 0, headerBytes.Length);
        }

        private async Task SendHeadersAsync(ClientMetadata client, WatsonMessage msg)
        { 
            byte[] headerBytes = msg.HeaderBytes;
            await client.DataStream.WriteAsync(headerBytes, 0, headerBytes.Length);
        }
         
        private void SendDataStream(ClientMetadata client, long contentLength, Stream stream)
        {
            if (contentLength <= 0) return;

            long bytesRemaining = contentLength;
            int bytesRead = 0;
            byte[] buffer = new byte[_ReadStreamBufferSize];

            if (Compression == CompressionType.None)
            {
                while (bytesRemaining > 0)
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        client.DataStream.Write(buffer, 0, bytesRead);
                        bytesRemaining -= bytesRead;
                    }
                }
            }
            else if (Compression == CompressionType.Gzip)
            {
                using (GZipStream gzs = new GZipStream(client.DataStream, CompressionMode.Compress, true))
                {
                    while (bytesRemaining > 0)
                    {
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            gzs.Write(buffer, 0, bytesRead);
                            bytesRemaining -= bytesRead;
                        }
                    }

                    gzs.Flush();
                }
            }
            else if (Compression == CompressionType.Deflate)
            {
                using (DeflateStream ds = new DeflateStream(client.DataStream, CompressionMode.Compress, true))
                {
                    while (bytesRemaining > 0)
                    {
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            ds.Write(buffer, 0, bytesRead);
                            bytesRemaining -= bytesRead;
                        }
                    }

                    ds.Flush();
                }
            }
            else
            {
                throw new InvalidOperationException("Unknown compression type: " + Compression.ToString());
            }

            client.DataStream.Flush();
        }

        private async Task SendDataStreamAsync(ClientMetadata client, long contentLength, Stream stream)
        {
            if (contentLength <= 0) return;

            long bytesRemaining = contentLength;
            int bytesRead = 0;
            byte[] buffer = new byte[_ReadStreamBufferSize];

            if (Compression == CompressionType.None)
            {
                while (bytesRemaining > 0)
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        await client.DataStream.WriteAsync(buffer, 0, bytesRead);
                        bytesRemaining -= bytesRead;
                    }
                }
            }
            else if (Compression == CompressionType.Gzip)
            {
                using (GZipStream gzs = new GZipStream(client.DataStream, CompressionMode.Compress, true))
                {
                    while (bytesRemaining > 0)
                    {
                        bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            await gzs.WriteAsync(buffer, 0, bytesRead);
                            bytesRemaining -= bytesRead;
                        }
                    }

                    await gzs.FlushAsync();
                }
            }
            else if (Compression == CompressionType.Deflate)
            {
                using (DeflateStream ds = new DeflateStream(client.DataStream, CompressionMode.Compress, true))
                {
                    while (bytesRemaining > 0)
                    {
                        bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            await ds.WriteAsync(buffer, 0, bytesRead);
                            bytesRemaining -= bytesRead;
                        }
                    }

                    await ds.FlushAsync();
                }
            }
            else
            {
                throw new InvalidOperationException("Unknown compression type: " + Compression.ToString());
            }

            await client.DataStream.FlushAsync();
        }

        private async Task MonitorForIdleClients()
        { 
            while (!_Token.IsCancellationRequested)
            {
                if (_IdleClientTimeoutSeconds > 0 && _ClientsLastSeen.Count > 0)
                {
                    MonitorForIdleClientsTask();
                }
                await Task.Delay(5000, _Token);
            }
        }

        private void MonitorForIdleClientsTask()
        { 
            DateTime idleTimestamp = DateTime.Now.AddSeconds(-1 * _IdleClientTimeoutSeconds);

            foreach (KeyValuePair<string, DateTime> curr in _ClientsLastSeen)
            { 
                if (curr.Value < idleTimestamp)
                {
                    _ClientsTimedout.TryAdd(curr.Key, DateTime.Now);
                    Logger?.Invoke("[WatsonTcpServer] Disconnecting client " + curr.Key + " due to idle timeout");
                    DisconnectClient(curr.Key);
                }
            }  
        }

        private void UpdateClientLastSeen(string ipPort)
        {
            if (_ClientsLastSeen.ContainsKey(ipPort))
            {
                DateTime ts;
                _ClientsLastSeen.TryRemove(ipPort, out ts);
            }

            _ClientsLastSeen.TryAdd(ipPort, DateTime.Now);
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
                            Logger?.Invoke("[WatsonTcpServer] MonitorForExpiredSyncResponses expiring response " + curr.Key.ToString());
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

        #endregion
    }
}