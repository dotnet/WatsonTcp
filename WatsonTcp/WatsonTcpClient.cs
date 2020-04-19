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
        /// Enable or disable message debugging.  Requires `Logger` to be set.
        /// WARNING: Setting this value to true will emit a large number of log messages with a large amount of data.
        /// </summary>
        public bool DebugMessages = false;

        /// <summary>
        /// Function called when authentication is requested from the server.  Expects the 16-byte preshared key.
        /// </summary>
        public Func<string> AuthenticationRequested = null;

        /// <summary>
        /// Event fired when authentication has succeeded.
        /// </summary>
        public event EventHandler AuthenticationSucceeded;

        /// <summary>
        /// Event fired when authentication has failed.
        /// </summary>
        public event EventHandler AuthenticationFailure;

        /// <summary>
        /// Use of 'MessageReceived' is exclusive and cannot be used with 'StreamReceived'.  
        /// This event is fired when a message is received from the server and it is desired that WatsonTcp pass the byte array containing the message payload. 
        /// </summary>
        public event EventHandler<MessageReceivedFromServerEventArgs> MessageReceived
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
        /// Use of 'StreamReceived' is exclusive and cannot be used with 'MessageReceived'.  
        /// This callback is called when a stream is received from the server and it is desired that WatsonTcp pass the stream containing the message payload to your application. 
        /// </summary>
        public event EventHandler<StreamReceivedFromServerEventArgs> StreamReceived
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
        /// Event fired when the client successfully connects to the server.
        /// </summary>
        public event EventHandler ServerConnected;

        /// <summary>
        /// Event fired when the client disconnects from the server.
        /// </summary>
        public event EventHandler ServerDisconnected;

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
        /// Enable acceptance of SSL certificates from the server that cannot be validated.
        /// </summary>
        public bool AcceptInvalidCertificates = true;

        /// <summary>
        /// Require mutual authentication between the server and this client.
        /// </summary>
        public bool MutuallyAuthenticate
        {
            get
            {
                return _MutuallyAuthenticate;
            }
            set
            {
                if (value)
                {
                    if (_Mode == Mode.Tcp) throw new ArgumentException("Mutual authentication only supported with SSL.");
                    if (_SslCertificate == null) throw new ArgumentException("Mutual authentication requires a certificate.");
                }

                _MutuallyAuthenticate = value;
            }
        }

        /// <summary>
        /// Indicates whether or not the client is connected to the server.
        /// </summary>
        public bool Connected { get; private set; }

        /// <summary>
        /// The number of seconds to wait before timing out a connection attempt.  Default is 5 seconds.
        /// </summary>
        public int ConnectTimeoutSeconds
        {
            get
            {
                return _ConnectTimeoutSeconds;
            }
            set
            {
                if (value < 1) throw new ArgumentException("ConnectTimeoutSeconds must be greater than zero.");
                _ConnectTimeoutSeconds = value;
            }
        }

        /// <summary>
        /// Type of compression to apply on sent messages.
        /// </summary>
        public CompressionType Compression = CompressionType.None;

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
        private int _ConnectTimeoutSeconds = 5;
        private Mode _Mode = Mode.Tcp;
        private string _SourceIp = null;
        private int _SourcePort = 0;
        private string _ServerIp = null;
        private int _ServerPort = 0;
        private bool _MutuallyAuthenticate = false;
        private TcpClient _Client = null;
        private Stream _DataStream = null;
        private NetworkStream _TcpStream = null;
        private SslStream _SslStream = null;

        private X509Certificate2 _SslCertificate = null;
        private X509Certificate2Collection _SslCertificateCollection = null;

        private SemaphoreSlim _WriteLock = new SemaphoreSlim(1);
        private SemaphoreSlim _ReadLock = new SemaphoreSlim(1);

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private CancellationToken _Token;

        private event EventHandler<MessageReceivedFromServerEventArgs> _MessageReceived;
        private event EventHandler<StreamReceivedFromServerEventArgs> _StreamReceived;
        private Func<SyncRequest, SyncResponse> _SyncRequestReceived = null;
         
        private readonly object _SyncResponseLock = new object();
        private Dictionary<string, SyncResponse> _SyncResponses = new Dictionary<string, SyncResponse>();

        private Statistics _Stats = new Statistics();

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
            if (serverPort < 1) throw new ArgumentOutOfRangeException(nameof(serverPort));
             
            _Token = _TokenSource.Token;
            _Mode = Mode.Tcp;
            _ServerIp = serverIp;
            _ServerPort = serverPort;
             
            Task.Run(() => MonitorForExpiredSyncResponses(), _Token);
        }

        /// <summary>
        /// Initialize the Watson TCP client with SSL.  Call Start() afterward to connect to the server.
        /// </summary>
        /// <param name="serverIp">The IP address or hostname of the server.</param>
        /// <param name="serverPort">The TCP port on which the server is listening.</param>
        /// <param name="pfxCertFile">The file containing the SSL certificate.</param>
        /// <param name="pfxCertPass">The password for the SSL certificate.</param>
        public WatsonTcpClient(
            string serverIp,
            int serverPort,
            string pfxCertFile,
            string pfxCertPass)
        {
            if (String.IsNullOrEmpty(serverIp)) throw new ArgumentNullException(nameof(serverIp));
            if (serverPort < 1) throw new ArgumentOutOfRangeException(nameof(serverPort));
             
            _Token = _TokenSource.Token;
            _Mode = Mode.Ssl;
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
             
            Task.Run(() => MonitorForExpiredSyncResponses(), _Token);
        }

        /// <summary>
        /// Initialize the Watson TCP client with SSL.  Call Start() afterward to connect to the server.
        /// </summary>
        /// <param name="serverIp">The IP address or hostname of the server.</param>
        /// <param name="serverPort">The TCP port on which the server is listening.</param>
        /// <param name="cert">The SSL certificate</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public WatsonTcpClient(
            string serverIp, 
            int serverPort, 
            X509Certificate2 cert)
        {
            if (String.IsNullOrEmpty(serverIp)) throw new ArgumentNullException(nameof(serverIp));
            if (serverPort < 1) throw new ArgumentOutOfRangeException(nameof(serverPort));
            if (cert == null) throw new ArgumentNullException(nameof(cert));

            _Token = _TokenSource.Token;
            _Mode = Mode.Ssl;
            _SslCertificate = cert;
            _ServerIp = serverIp;
            _ServerPort = serverPort;

            _SslCertificateCollection = new X509Certificate2Collection
            {
                _SslCertificate
            };

            Task.Run(() => MonitorForExpiredSyncResponses(), _Token);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tear down the client and dispose of background workers.
        /// </summary>
        public void Dispose()
        {
            Dispose(true); 
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Start the client and establish a connection to the server.
        /// </summary>
        public void Start()
        {
            _Client = new TcpClient();
            _Stats = new Statistics();
            IAsyncResult asyncResult = null;
            WaitHandle waitHandle = null;
            bool connectSuccess = false;

            if (_StreamReceived == null && _MessageReceived == null)
            {
                throw new InvalidOperationException("Either 'MessageReceived' or 'StreamReceived' must first be set.");
            }

            if (_Mode == Mode.Tcp)
            {
                #region TCP

                Logger?.Invoke("[WatsonTcpClient] Connecting to " + _ServerIp + ":" + _ServerPort);

                _Client.LingerState = new LingerOption(true, 0);
                asyncResult = _Client.BeginConnect(_ServerIp, _ServerPort, null, null);
                waitHandle = asyncResult.AsyncWaitHandle;

                try
                {
                    connectSuccess = waitHandle.WaitOne(TimeSpan.FromSeconds(_ConnectTimeoutSeconds), false);
                    if (!connectSuccess)
                    {
                        _Client.Close();
                        throw new TimeoutException("Timeout connecting to " + _ServerIp + ":" + _ServerPort);
                    }

                    _Client.EndConnect(asyncResult);

                    _SourceIp = ((IPEndPoint)_Client.Client.LocalEndPoint).Address.ToString();
                    _SourcePort = ((IPEndPoint)_Client.Client.LocalEndPoint).Port;
                    _TcpStream = _Client.GetStream();
                    _DataStream = _TcpStream;
                    _SslStream = null;

                    Connected = true;
                }
                catch (Exception)
                {
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

                Logger?.Invoke("[WatsonTcpClient] Connecting with SSL to " + _ServerIp + ":" + _ServerPort);

                _Client.LingerState = new LingerOption(true, 0);
                asyncResult = _Client.BeginConnect(_ServerIp, _ServerPort, null, null);
                waitHandle = asyncResult.AsyncWaitHandle;

                try
                {
                    connectSuccess = waitHandle.WaitOne(TimeSpan.FromSeconds(_ConnectTimeoutSeconds), false);
                    if (!connectSuccess)
                    {
                        _Client.Close();
                        throw new TimeoutException("Timeout connecting to " + _ServerIp + ":" + _ServerPort);
                    }

                    _Client.EndConnect(asyncResult);

                    _SourceIp = ((IPEndPoint)_Client.Client.LocalEndPoint).Address.ToString();
                    _SourcePort = ((IPEndPoint)_Client.Client.LocalEndPoint).Port;

                    if (AcceptInvalidCertificates)
                    {
                        // accept invalid certs
                        _SslStream = new SslStream(_Client.GetStream(), false, new RemoteCertificateValidationCallback(AcceptCertificate));
                    }
                    else
                    {
                        // do not accept invalid SSL certificates
                        _SslStream = new SslStream(_Client.GetStream(), false);
                    }

                    _SslStream.AuthenticateAsClient(_ServerIp, _SslCertificateCollection, SslProtocols.Tls12, !AcceptInvalidCertificates);

                    if (!_SslStream.IsEncrypted)
                    {
                        throw new AuthenticationException("Stream is not encrypted");
                    }

                    if (!_SslStream.IsAuthenticated)
                    {
                        throw new AuthenticationException("Stream is not authenticated");
                    }

                    if (MutuallyAuthenticate && !_SslStream.IsMutuallyAuthenticated)
                    {
                        throw new AuthenticationException("Mutual authentication failed");
                    }

                    _DataStream = _SslStream;
                    Connected = true;
                }
                catch (Exception)
                {
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
             
            Logger?.Invoke("[WatsonTcpClient] Connected to server");
            ServerConnected?.Invoke(this, EventArgs.Empty);  
            Task dataReceiver = Task.Run(() => DataReceiver(), _Token);
        }

        /// <summary>
        /// Start the client and establish a connection to the server.
        /// </summary>
        /// <returns></returns>
        public Task StartAsync()
        {
            _Client = new TcpClient();
            _Stats = new Statistics();
            IAsyncResult asyncResult = null;
            WaitHandle waitHandle = null;
            bool connectSuccess = false;

            if (_StreamReceived == null && _MessageReceived == null)
            {
                throw new InvalidOperationException("Either 'MessageReceived' or 'StreamReceived' must first be set.");
            }

            if (_Mode == Mode.Tcp)
            {
                #region TCP

                Logger?.Invoke("[WatsonTcpClient] Connecting to " + _ServerIp + ":" + _ServerPort);

                _Client.LingerState = new LingerOption(true, 0);
                asyncResult = _Client.BeginConnect(_ServerIp, _ServerPort, null, null);
                waitHandle = asyncResult.AsyncWaitHandle;

                try
                {
                    connectSuccess = waitHandle.WaitOne(TimeSpan.FromSeconds(_ConnectTimeoutSeconds), false);
                    if (!connectSuccess)
                    {
                        _Client.Close();
                        throw new TimeoutException("Timeout connecting to " + _ServerIp + ":" + _ServerPort);
                    }

                    _Client.EndConnect(asyncResult);

                    _SourceIp = ((IPEndPoint)_Client.Client.LocalEndPoint).Address.ToString();
                    _SourcePort = ((IPEndPoint)_Client.Client.LocalEndPoint).Port;
                    _TcpStream = _Client.GetStream();
                    _DataStream = _TcpStream;
                    _SslStream = null;

                    Connected = true;
                }
                catch (Exception)
                {
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

                Logger?.Invoke("[WatsonTcpClient] Connecting with SSL to " + _ServerIp + ":" + _ServerPort);

                _Client.LingerState = new LingerOption(true, 0);
                asyncResult = _Client.BeginConnect(_ServerIp, _ServerPort, null, null);
                waitHandle = asyncResult.AsyncWaitHandle;

                try
                {
                    connectSuccess = waitHandle.WaitOne(TimeSpan.FromSeconds(_ConnectTimeoutSeconds), false);
                    if (!connectSuccess)
                    {
                        _Client.Close();
                        throw new TimeoutException("Timeout connecting to " + _ServerIp + ":" + _ServerPort);
                    }

                    _Client.EndConnect(asyncResult);

                    _SourceIp = ((IPEndPoint)_Client.Client.LocalEndPoint).Address.ToString();
                    _SourcePort = ((IPEndPoint)_Client.Client.LocalEndPoint).Port;

                    if (AcceptInvalidCertificates)
                    {
                        // accept invalid certs
                        _SslStream = new SslStream(_Client.GetStream(), false, new RemoteCertificateValidationCallback(AcceptCertificate));
                    }
                    else
                    {
                        // do not accept invalid SSL certificates
                        _SslStream = new SslStream(_Client.GetStream(), false);
                    }

                    _SslStream.AuthenticateAsClient(_ServerIp, _SslCertificateCollection, SslProtocols.Tls12, !AcceptInvalidCertificates);

                    if (!_SslStream.IsEncrypted)
                    {
                        throw new AuthenticationException("Stream is not encrypted");
                    }

                    if (!_SslStream.IsAuthenticated)
                    {
                        throw new AuthenticationException("Stream is not authenticated");
                    }

                    if (MutuallyAuthenticate && !_SslStream.IsMutuallyAuthenticated)
                    {
                        throw new AuthenticationException("Mutual authentication failed");
                    }

                    _DataStream = _SslStream;
                    Connected = true;
                }
                catch (Exception)
                {
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

            Logger?.Invoke("[WatsonTcpClient] Connected to server");
            ServerConnected?.Invoke(this, EventArgs.Empty);
            return DataReceiver();
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
        /// Send data to the server.
        /// </summary>
        /// <param name="data">String containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(string data)
        {
            if (String.IsNullOrEmpty(data)) return Send(null, new byte[0]);
            else return Send(null, Encoding.UTF8.GetBytes(data));
        }

        /// <summary>
        /// Send data and metadata to the server.
        /// </summary>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="data">String containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(Dictionary<object, object> metadata, string data)
        {
            if (String.IsNullOrEmpty(data)) return Send(null, new byte[0]);
            else return Send(metadata, Encoding.UTF8.GetBytes(data));
        }

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(byte[] data)
        {
            if (data == null) data = new byte[0];
            return Send(null, data);
        }

        /// <summary>
        /// Send data and metadata to the server.
        /// </summary>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(Dictionary<object, object> metadata, byte[] data)
        {
            if (data == null) data = new byte[0];
            WatsonCommon.BytesToStream(data, out long contentLength, out Stream stream);
            return Send(metadata, contentLength, stream);
        }

        /// <summary>
        /// Send data to the server using a stream.
        /// </summary>
        /// <param name="contentLength">The number of bytes in the stream.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(long contentLength, Stream stream)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (stream == null) stream = new MemoryStream(new byte[0]);
            return Send(null, contentLength, stream);
        }

        /// <summary>
        /// Send data and metadata to the server using a stream.
        /// </summary>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="contentLength">The number of bytes in the stream.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(Dictionary<object, object> metadata, long contentLength, Stream stream)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (stream == null) stream = new MemoryStream(new byte[0]); 
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, false, false, null, null, Compression, (DebugMessages ? Logger : null));
            return SendInternal(msg, contentLength, stream);
        }

        /// <summary>
        /// Send metadata to the server with no data.
        /// </summary>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(Dictionary<object, object> metadata)
        {
            WatsonMessage msg = new WatsonMessage(metadata, 0, new MemoryStream(new byte[0]), false, false, null, null, Compression, (DebugMessages ? Logger : null));
            return SendInternal(msg, 0, new MemoryStream(new byte[0]));
        }

        /// <summary>
        /// Send data to the server asynchronously.
        /// </summary>
        /// <param name="data">String containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string data)
        {
            if (String.IsNullOrEmpty(data)) return await SendAsync(null, new byte[0]);
            else return await SendAsync(null, Encoding.UTF8.GetBytes(data));
        }

        /// <summary>
        /// Send data and metadata to the server asynchronously.
        /// </summary>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="data">String containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(Dictionary<object, object> metadata, string data)
        {
            if (String.IsNullOrEmpty(data)) return await SendAsync(null, new byte[0]);
            else return await SendAsync(metadata, Encoding.UTF8.GetBytes(data));
        }

        /// <summary>
        /// Send data to the server asynchronously.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(byte[] data)
        {
            if (data == null) data = new byte[0];
            return await SendAsync(null, data);
        }

        /// <summary>
        /// Send data and metadata to the server asynchronously.
        /// </summary>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(Dictionary<object, object> metadata, byte[] data)
        {
            if (data == null) data = new byte[0];
            WatsonCommon.BytesToStream(data, out long contentLength, out Stream stream);
            return await SendAsync(metadata, contentLength, stream);
        }

        /// <summary>
        /// Send data to the server from a stream asynchronously.
        /// </summary>
        /// <param name="contentLength">The number of bytes to send.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(long contentLength, Stream stream)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (stream == null) stream = new MemoryStream(new byte[0]);
            return await SendAsync(null, contentLength, stream);
        }

        /// <summary>
        /// Send data and metadata to the server from a stream asynchronously.
        /// </summary>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <param name="contentLength">The number of bytes to send.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(Dictionary<object, object> metadata, long contentLength, Stream stream)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (stream == null) stream = new MemoryStream(new byte[0]);
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, false, false, null, null, Compression, (DebugMessages ? Logger : null));
            return await SendInternalAsync(msg, contentLength, stream);
        }

        /// <summary>
        /// Send metadata to the server with no data, asynchronously
        /// </summary>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(Dictionary<object, object> metadata)
        {
            WatsonMessage msg = new WatsonMessage(metadata, 0, new MemoryStream(new byte[0]), false, false, null, null, Compression, (DebugMessages ? Logger : null));
            return await SendInternalAsync(msg, 0, new MemoryStream(new byte[0]));
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="data">Data to send.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(int timeoutMs, string data)
        {
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (String.IsNullOrEmpty(data)) return SendAndWait(null, timeoutMs, new byte[0]);
            return SendAndWait(null, timeoutMs, Encoding.UTF8.GetBytes(data));
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="data">Data to send.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(int timeoutMs, byte[] data)
        {
            if (data == null) data = new byte[0];
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            return SendAndWait(null, timeoutMs, data);
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="contentLength">The number of bytes to send from the supplied stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(int timeoutMs, long contentLength, Stream stream)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (stream == null) stream = new MemoryStream(new byte[0]);
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            return SendAndWait(null, timeoutMs, contentLength, stream);
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="data">Data to send.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(Dictionary<object, object> metadata, int timeoutMs, string data)
        {
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (String.IsNullOrEmpty(data)) return SendAndWait(metadata, timeoutMs, new byte[0]);
            return SendAndWait(metadata, timeoutMs, Encoding.UTF8.GetBytes(data)); 
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="data">Data to send.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(Dictionary<object, object> metadata, int timeoutMs, byte[] data)
        {
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (data == null) data = new byte[0];
            DateTime expiration = DateTime.Now.AddMilliseconds(timeoutMs);
            WatsonCommon.BytesToStream(data, out long contentLength, out Stream stream);
            return SendAndWait(metadata, timeoutMs, contentLength, stream);
        }

        /// <summary>
        /// Send data and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param>
        /// <param name="contentLength">The number of bytes to send from the supplied stream.</param>
        /// <param name="stream">Stream containing data.</param>
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(Dictionary<object, object> metadata, int timeoutMs, long contentLength, Stream stream)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            if (stream == null) stream = new MemoryStream(new byte[0]);
            DateTime expiration = DateTime.Now.AddMilliseconds(timeoutMs);
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, true, false, expiration, Guid.NewGuid().ToString(), Compression, (DebugMessages ? Logger : null));
            return SendAndWaitInternal(msg, timeoutMs, contentLength, stream);
        }

        /// <summary>
        /// Send metadata and wait for a response for the specified number of milliseconds.  A TimeoutException will be thrown if a response is not received.
        /// </summary>
        /// <param name="metadata">Metadata dictionary to attach to the message.</param>
        /// <param name="timeoutMs">Number of milliseconds to wait before considering a request to be expired.</param> 
        /// <returns>SyncResponse.</returns>
        public SyncResponse SendAndWait(Dictionary<object, object> metadata, int timeoutMs)
        {
            if (timeoutMs < 1000) throw new ArgumentException("Timeout milliseconds must be 1000 or greater.");
            DateTime expiration = DateTime.Now.AddMilliseconds(timeoutMs);
            WatsonMessage msg = new WatsonMessage(metadata, 0, new MemoryStream(new byte[0]), true, false, expiration, Guid.NewGuid().ToString(), Compression, (DebugMessages ? Logger : null));
            return SendAndWaitInternal(msg, timeoutMs, 0, new MemoryStream(new byte[0]));
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Tear down the client and dispose of background workers.
        /// </summary>
        /// <param name="disposing">Indicate if resources should be disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger?.Invoke("[WatsonTcpClient] Disposing"); 

                if (Connected)
                {
                    WatsonMessage msg = new WatsonMessage();
                    msg.Status = MessageStatus.Disconnecting; 
                    SendInternal(msg, 0, null);
                }

                if (_TokenSource != null)
                {
                    if (!_TokenSource.IsCancellationRequested) _TokenSource.Cancel();
                    _TokenSource.Dispose();
                    _TokenSource = null;
                }

                if (_WriteLock != null)
                {
                    _WriteLock.Dispose();
                    _WriteLock = null;
                }

                if (_ReadLock != null)
                {
                    _ReadLock.Dispose();
                    _ReadLock = null;
                }

                if (_SslStream != null)
                {
                    _SslStream.Close();
                    _SslStream.Dispose();
                    _SslStream = null;
                }

                if (_TcpStream != null)
                {
                    _TcpStream.Close();
                    _TcpStream.Dispose();
                    _TcpStream = null;
                }

                if (_Client != null)
                {
                    _Client.Close();
                    _Client.Dispose();
                    _Client = null;
                }
                
                _DataStream = null; 
                Connected = false;
                Logger?.Invoke("[WatsonTcpClient] Dispose complete");
            }
        }

        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // return true; // Allow untrusted certificates.
            return AcceptInvalidCertificates;
        }
         
        private async Task DataReceiver()
        {  
            while (true)
            {
                bool readLocked = false;
                 
                try
                {
                    _Token.ThrowIfCancellationRequested();
                     
                    if (_Client == null 
                        || !_Client.Connected
                        || _Token.IsCancellationRequested)
                    {
                        Logger?.Invoke("[WatsonTcpClient] Disconnect detected");
                        break;
                    }

                    WatsonMessage msg = new WatsonMessage(_DataStream, (DebugMessages ? Logger : null)); 
                    readLocked = await _ReadLock.WaitAsync(1);
                    bool buildSuccess = await msg.BuildFromStream();
                    if (!buildSuccess)
                    {
                        Logger?.Invoke("[WatsonTcpClient] Message build failed due to disconnect");
                        break;
                    }

                    if (msg == null)
                    { 
                        await Task.Delay(30);
                        continue;
                    }

                    if (msg.Status == MessageStatus.Removed)
                    {
                        Logger?.Invoke("[WatsonTcpClient] Disconnect due to server-side removal");
                        break;
                    }
                    else if (msg.Status == MessageStatus.Disconnecting)
                    {
                        Logger?.Invoke("[WatsonTcpClient] Disconnect due to server shutdown");
                        break;
                    }
                    else if (msg.Status == MessageStatus.AuthSuccess)
                    {
                        Logger?.Invoke("[WatsonTcpClient] Authentication successful");
                        AuthenticationSucceeded?.Invoke(this, EventArgs.Empty);
                        continue;
                    }
                    else if (msg.Status == MessageStatus.AuthFailure)
                    {
                        Logger?.Invoke("[WatsonTcpClient] Authentication failed");
                        AuthenticationFailure?.Invoke(this, EventArgs.Empty);
                        continue;
                    }

                    if (msg.Status == MessageStatus.AuthRequired)
                    {
                        Logger?.Invoke("[WatsonTcpClient] Authentication required by server; please authenticate using pre-shared key");
                        if (AuthenticationRequested != null)
                        {
                            string psk = AuthenticationRequested();
                            if (!String.IsNullOrEmpty(psk))
                            {
                                Authenticate(psk);
                            }
                        }
                        continue;
                    }

                    if (msg.SyncRequest)
                    { 
                        if (SyncRequestReceived != null)
                        {
                            byte[] msgData = await ReadMessageDataAsync(msg);

                            if (DateTime.Now < msg.Expiration.Value)
                            {
                                SyncRequest syncReq = new SyncRequest(
                                    _ServerIp + ":" + _ServerPort,
                                    msg.ConversationGuid,
                                    msg.Expiration.Value,
                                    msg.Metadata,
                                    msgData);

                                SyncResponse syncResp = SyncRequestReceived(syncReq);
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
                                        Compression,
                                        (DebugMessages ? Logger : null)); 
                                    SendInternal(respMsg, contentLength, stream);
                                }
                            }
                            else
                            {
                                Logger?.Invoke("[WatsonTcpClient] Expired synchronous request received and discarded");
                            }
                        } 
                    }
                    else if (msg.SyncResponse)
                    {
                        if (DateTime.Now < msg.Expiration.Value)
                        {
                            byte[] msgData = await ReadMessageDataAsync(msg);

                            lock (_SyncResponseLock)
                            {
                                _SyncResponses.Add(msg.ConversationGuid, new SyncResponse(msg.Expiration.Value, msg.Metadata, msgData));
                            }
                        }
                        else
                        {
                            Logger?.Invoke("[WatsonTcpClient] Expired synchronous response received and discarded");
                        }
                    }
                    else
                    {
                        byte[] msgData = null;
                        MemoryStream ms = new MemoryStream();

                        if (_MessageReceived != null
                            && _MessageReceived.GetInvocationList().Length > 0)
                        {
                            msgData = await ReadMessageDataAsync(msg); 
                            MessageReceivedFromServerEventArgs args = new MessageReceivedFromServerEventArgs(msg.Metadata, msgData);
                            _MessageReceived?.Invoke(this, args);
                        }
                        else if (_StreamReceived != null
                            && _StreamReceived.GetInvocationList().Length > 0)
                        {
                            StreamReceivedFromServerEventArgs sr = null;

                            if (msg.Compression == CompressionType.None)
                            {
                                sr = new StreamReceivedFromServerEventArgs(msg.Metadata, msg.ContentLength, msg.DataStream);
                                _StreamReceived?.Invoke(this, sr);
                            }
                            else if (msg.Compression == CompressionType.Deflate)
                            {
                                using (DeflateStream ds = new DeflateStream(msg.DataStream, CompressionMode.Decompress, true))
                                {
                                    msgData = WatsonCommon.ReadStreamFully(ds);
                                    ms = new MemoryStream(msgData);
                                    ms.Seek(0, SeekOrigin.Begin);

                                    sr = new StreamReceivedFromServerEventArgs(msg.Metadata, msg.ContentLength, ms);
                                    _StreamReceived?.Invoke(this, sr); 
                                }
                            }
                            else if (msg.Compression == CompressionType.Gzip)
                            {
                                using (GZipStream gs = new GZipStream(msg.DataStream, CompressionMode.Decompress, true))
                                {
                                    msgData = WatsonCommon.ReadStreamFully(gs);
                                    ms = new MemoryStream(msgData);
                                    ms.Seek(0, SeekOrigin.Begin);

                                    sr = new StreamReceivedFromServerEventArgs(msg.Metadata, msg.ContentLength, ms);
                                    _StreamReceived?.Invoke(this, sr);
                                }
                            }
                            else
                            {
                                throw new InvalidOperationException("Unknown compression type: " + msg.Compression.ToString());
                            } 
                        }
                        else
                        {
                            Logger?.Invoke("[WatsonTcpClient] Event handler not set for either MessageReceived or StreamReceived");
                            break;
                        }
                    }
                     
                    _Stats.ReceivedMessages = _Stats.ReceivedMessages + 1;
                    _Stats.ReceivedBytes += msg.ContentLength;
                } 
                catch (OperationCanceledException)
                {
                    Logger?.Invoke("[WatsonTcpClient] Cancellation requested");
                }
                catch (Exception e)
                {
                    Logger?.Invoke(
                        "[WatsonTcpClient] Data receiver exception: " +
                        Environment.NewLine +
                        e.ToString() +
                        Environment.NewLine); 
                    break;
                } 
                finally
                {
                    if (readLocked) _ReadLock.Release();
                }
            }

            Logger?.Invoke("[WatsonTcpClient] Data receiver terminated");
            Connected = false;
            ServerDisconnected?.Invoke(this, EventArgs.Empty);
            Dispose();
        }

        private bool SendInternal(WatsonMessage msg, long contentLength, Stream stream)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (!Connected) return false;

            if (contentLength > 0)
            {
                if (stream == null || !stream.CanRead)
                {
                    throw new ArgumentException("Cannot read from supplied stream.");
                }
            }
             
            bool disconnectDetected = false;

            try
            {
                if (_Client == null
                    || !_Client.Connected)
                {
                    disconnectDetected = true;
                    return false;
                }
                  
                _WriteLock.Wait();

                try
                { 
                    SendHeaders(msg); 
                    SendDataStream(contentLength, stream); 
                }
                finally
                {
                    _WriteLock.Release();
                }

                _Stats.SentMessages += 1;
                _Stats.SentBytes += contentLength;
                return true;
            }
            catch (Exception e)
            {
                Logger?.Invoke(
                    "[WatsonTcpClient] Message write exception: " +
                    Environment.NewLine +
                    e.ToString() +
                    Environment.NewLine);

                disconnectDetected = true;
                return false;
            }
            finally
            {
                if (disconnectDetected)
                {
                    Connected = false;
                    Dispose();
                }
            }
        }

        private async Task<bool> SendInternalAsync(WatsonMessage msg, long contentLength, Stream stream)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (!Connected) return false;
            
            if (contentLength > 0)
            {
                if (stream == null || !stream.CanRead)
                {
                    throw new ArgumentException("Cannot read from supplied stream.");
                }
            }

            bool disconnectDetected = false;

            try
            {
                if (_Client == null || !_Client.Connected)
                {
                    disconnectDetected = true;
                    return false;
                }
                 
                await _WriteLock.WaitAsync();

                try
                { 
                    await SendHeadersAsync(msg); 
                    await SendDataStreamAsync(contentLength, stream); 
                }
                finally
                {
                    _WriteLock.Release();
                }

                _Stats.SentMessages += 1;
                _Stats.SentBytes += contentLength;
                return true;
            }
            catch (Exception e)
            {
                Logger?.Invoke(
                    "[WatsonTcpClient] Message write exception: " +
                    Environment.NewLine +
                    e.ToString() +
                    Environment.NewLine);

                disconnectDetected = true;
                return false;
            }
            finally
            {
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
            if (contentLength > 0)
            {
                if (stream == null || !stream.CanRead)
                {
                    throw new ArgumentException("Cannot read from supplied stream.");
                }
            }

            bool disconnectDetected = false;
            
            if (_Client == null || !_Client.Connected)
            {
                disconnectDetected = true;
                throw new InvalidOperationException("Client is not connected to the server.");
            }
             
            try
            { 
                _WriteLock.Wait();

                try
                {
                    SendHeaders(msg);
                    SendDataStream(contentLength, stream); 
                }
                finally
                {
                    _WriteLock.Release();
                }

                _Stats.SentMessages += 1;
                _Stats.SentBytes += contentLength; 
            }
            catch (Exception e)
            {
                Logger?.Invoke(
                    "[WatsonTcpClient] Message write exception: " +
                    Environment.NewLine +
                    e.ToString() +
                    Environment.NewLine);

                disconnectDetected = true;
                throw;
            }
            finally
            {
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

        private async Task SendHeadersAsync(WatsonMessage msg)
        {
            byte[] headerBytes = msg.HeaderBytes; 
            await _DataStream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await _DataStream.FlushAsync();
        }
         
        private void SendDataStream(long contentLength, Stream stream)
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
                        _DataStream.Write(buffer, 0, bytesRead);
                        bytesRemaining -= bytesRead;
                    } 
                }
            }
            else if (Compression == CompressionType.Gzip)
            {
                using (GZipStream gs = new GZipStream(_DataStream, CompressionMode.Compress, true))
                {
                    while (bytesRemaining > 0)
                    {
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            gs.Write(buffer, 0, bytesRead);
                            bytesRemaining -= bytesRead;
                        }
                    }

                    gs.Flush();
                    gs.Close();
                }
            }
            else if (Compression == CompressionType.Deflate)
            {
                using (DeflateStream ds = new DeflateStream(_DataStream, CompressionMode.Compress, true))
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
                    ds.Close();
                }
            }
            else
            {
                throw new InvalidOperationException("Unknown compression type: " + Compression.ToString());
            }
             
            _DataStream.Flush(); 
        }

        private async Task SendDataStreamAsync(long contentLength, Stream stream)
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
                        await _DataStream.WriteAsync(buffer, 0, bytesRead);
                        bytesRemaining -= bytesRead;
                    }
                } 
            }
            else if (Compression == CompressionType.Gzip)
            {
                using (GZipStream gs = new GZipStream(_DataStream, CompressionMode.Compress, true))
                {
                    while (bytesRemaining > 0)
                    {
                        bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            await gs.WriteAsync(buffer, 0, bytesRead);
                            bytesRemaining -= bytesRead;
                        }
                    }

                    await gs.FlushAsync();
                    gs.Close();
                }
            }
            else if (Compression == CompressionType.Deflate)
            {
                using (DeflateStream ds = new DeflateStream(_DataStream, CompressionMode.Compress, true))
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
                    ds.Close();
                }
            }
            else
            {
                throw new InvalidOperationException("Unknown compression type: " + Compression.ToString());
            }

            await _DataStream.FlushAsync();
        }
         
        private async Task<byte[]> ReadMessageDataAsync(WatsonMessage msg)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (msg.ContentLength == 0) return new byte[0];

            byte[] msgData = null;
            MemoryStream ms = new MemoryStream();

            if (msg.Compression == CompressionType.None)
            {
                msgData = await WatsonCommon.ReadFromStreamAsync(msg.DataStream, msg.ContentLength, _ReadStreamBufferSize);
            }
            else if (msg.Compression == CompressionType.Deflate)
            {
                using (DeflateStream ds = new DeflateStream(msg.DataStream, CompressionMode.Decompress, true))
                {
                    msgData = WatsonCommon.ReadStreamFully(ds); 
                }
            }
            else if (msg.Compression == CompressionType.Gzip)
            {
                using (GZipStream gs = new GZipStream(msg.DataStream, CompressionMode.Decompress, true))
                {
                    msgData = WatsonCommon.ReadStreamFully(gs);
                }
            }
            else
            {
                throw new InvalidOperationException("Unknown compression type: " + Compression.ToString());
            }

            return msgData;
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
                            Logger?.Invoke("[WatsonTcpClient] MonitorForExpiredSyncResponses expiring response " + curr.Key.ToString());
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