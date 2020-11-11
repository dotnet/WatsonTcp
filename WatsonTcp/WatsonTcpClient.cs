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

        private Mode _Mode = Mode.Tcp;
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
            if (serverPort < 0) throw new ArgumentOutOfRangeException(nameof(serverPort));
              
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
        }

        /// <summary>
        /// Initialize the Watson TCP client with SSL.  Call Start() afterward to connect to the server.
        /// </summary>
        /// <param name="serverIp">The IP address or hostname of the server.</param>
        /// <param name="serverPort">The TCP port on which the server is listening.</param>
        /// <param name="cert">The SSL certificate</param>
        public WatsonTcpClient(
            string serverIp, 
            int serverPort, 
            X509Certificate2 cert)
        {
            if (String.IsNullOrEmpty(serverIp)) throw new ArgumentNullException(nameof(serverIp));
            if (serverPort < 0) throw new ArgumentOutOfRangeException(nameof(serverPort));
            if (cert == null) throw new ArgumentNullException(nameof(cert));
             
            _Mode = Mode.Ssl;
            _SslCertificate = cert;
            _ServerIp = serverIp;
            _ServerPort = serverPort;

            _SslCertificateCollection = new X509Certificate2Collection
            {
                _SslCertificate
            };
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

            _Client = new TcpClient();
            _Statistics = new WatsonTcpStatistics();

            IAsyncResult asyncResult = null;
            WaitHandle waitHandle = null;
            bool connectSuccess = false;

            if (!_Events.IsUsingMessages && !_Events.IsUsingStreams) 
                throw new InvalidOperationException("One of either 'MessageReceived' or 'StreamReceived' events must first be set.");

            if (_Keepalive.EnableTcpKeepAlives) EnableKeepalives();

            if (_Mode == Mode.Tcp)
            {
                #region TCP

                _Settings.Logger?.Invoke(_Header + "connecting to " + _ServerIp + ":" + _ServerPort);

                _Client.LingerState = new LingerOption(true, 0);
                asyncResult = _Client.BeginConnect(_ServerIp, _ServerPort, null, null);
                waitHandle = asyncResult.AsyncWaitHandle;

                try
                {
                    connectSuccess = waitHandle.WaitOne(TimeSpan.FromSeconds(_Settings.ConnectTimeoutSeconds), false);
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
                catch (Exception e)
                {
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

                _Settings.Logger?.Invoke(_Header + "connecting with SSL to " + _ServerIp + ":" + _ServerPort);

                _Client.LingerState = new LingerOption(true, 0);
                asyncResult = _Client.BeginConnect(_ServerIp, _ServerPort, null, null);
                waitHandle = asyncResult.AsyncWaitHandle;

                try
                {
                    connectSuccess = waitHandle.WaitOne(TimeSpan.FromSeconds(_Settings.ConnectTimeoutSeconds), false);
                    if (!connectSuccess)
                    {
                        _Client.Close();
                        throw new TimeoutException("Timeout connecting to " + _ServerIp + ":" + _ServerPort);
                    }

                    _Client.EndConnect(asyncResult);

                    _SourceIp = ((IPEndPoint)_Client.Client.LocalEndPoint).Address.ToString();
                    _SourcePort = ((IPEndPoint)_Client.Client.LocalEndPoint).Port;

                    if (_Settings.AcceptInvalidCertificates)
                    {
                        _SslStream = new SslStream(_Client.GetStream(), false, new RemoteCertificateValidationCallback(AcceptCertificate)); 
                    }
                    else
                    { 
                        _SslStream = new SslStream(_Client.GetStream(), false);
                    }

                    _SslStream.AuthenticateAsClient(_ServerIp, _SslCertificateCollection, SslProtocols.Tls12, !_Settings.AcceptInvalidCertificates);

                    if (!_SslStream.IsEncrypted)
                    {
                        throw new AuthenticationException("Stream is not encrypted");
                    }

                    if (!_SslStream.IsAuthenticated)
                    {
                        throw new AuthenticationException("Stream is not authenticated");
                    }

                    if (_Settings.MutuallyAuthenticate && !_SslStream.IsMutuallyAuthenticated)
                    {
                        throw new AuthenticationException("Mutual authentication failed");
                    }

                    _DataStream = _SslStream;

                    Connected = true;
                }
                catch (Exception e)
                {
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

            _DataReceiver = Task.Run(() => DataReceiver(), _Token);
            _MonitorSyncResponses = Task.Run(() => MonitorForExpiredSyncResponses(), _Token);
            _Events.HandleServerConnected(this, EventArgs.Empty);
            _Settings.Logger?.Invoke(_Header + "connected");
        }
         
        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public void Disconnect()
        {
            if (!Connected) throw new InvalidOperationException("Nonnected to the server.");

            _Settings.Logger?.Invoke(_Header + "disconnecting");

            if (Connected)
            {
                WatsonMessage msg = new WatsonMessage();
                msg.Status = MessageStatus.Disconnecting;
                SendInternal(msg, 0, null);
            }

            if (_TokenSource != null)
            {
                // stop the data receiver
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

            Connected = false;

            _Settings.Logger?.Invoke(_Header + "disconnected");
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
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
            return SendInternal(msg, contentLength, stream);
        }

        /// <summary>
        /// Send metadata to the server with no data.
        /// </summary>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(Dictionary<object, object> metadata)
        {
            WatsonMessage msg = new WatsonMessage(metadata, 0, new MemoryStream(new byte[0]), false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
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
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
            return await SendInternalAsync(msg, contentLength, stream);
        }

        /// <summary>
        /// Send metadata to the server with no data, asynchronously
        /// </summary>
        /// <param name="metadata">Dictionary containing metadata.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(Dictionary<object, object> metadata)
        {
            WatsonMessage msg = new WatsonMessage(metadata, 0, new MemoryStream(new byte[0]), false, false, null, null, (_Settings.DebugMessages ? _Settings.Logger : null));
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
            WatsonMessage msg = new WatsonMessage(metadata, contentLength, stream, true, false, expiration, Guid.NewGuid().ToString(), (_Settings.DebugMessages ? _Settings.Logger : null));
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
            WatsonMessage msg = new WatsonMessage(metadata, 0, new MemoryStream(new byte[0]), true, false, expiration, Guid.NewGuid().ToString(), (_Settings.DebugMessages ? _Settings.Logger : null));
            return SendAndWaitInternal(msg, timeoutMs, 0, new MemoryStream(new byte[0]));
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
                _Settings.Logger?.Invoke(_Header + "disposing");

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

        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return _Settings.AcceptInvalidCertificates;
        }
         
        private async Task DataReceiver()
        {  
            while (!_Token.IsCancellationRequested)
            {
                bool readLocked = false;
                 
                try
                {
                    #region Check-for-Connection

                    if (_Client == null || !_Client.Connected || _Token.IsCancellationRequested)
                    {
                        _Settings.Logger?.Invoke(_Header + "disconnect detected");
                        break;
                    }

                    #endregion

                    #region Read-Message

                    readLocked = await _ReadLock.WaitAsync(1);
                    if (!readLocked)
                    {
                        Task.Delay(30).Wait();
                        continue;
                    }
                     
                    WatsonMessage msg = new WatsonMessage(_DataStream, (_Settings.DebugMessages ? _Settings.Logger : null));
                    bool buildSuccess = await msg.BuildFromStream(_Token);
                    if (!buildSuccess)
                    {
                        _Settings.Logger?.Invoke(_Header + "disconnect detected");
                        break;
                    }

                    if (msg == null)
                    { 
                        await Task.Delay(30);
                        continue;
                    }

                    #endregion

                    #region Process-by-Status

                    if (msg.Status == MessageStatus.Removed)
                    {
                        _Settings.Logger?.Invoke(_Header + "disconnect due to server-side removal");
                        break;
                    }
                    else if (msg.Status == MessageStatus.Disconnecting)
                    {
                        _Settings.Logger?.Invoke(_Header + "disconnect due to server shutdown");
                        break;
                    }
                    else if (msg.Status == MessageStatus.AuthSuccess)
                    {
                        _Settings.Logger?.Invoke(_Header + "authentication successful");
                        _Events.HandleAuthenticationSucceeded(this, EventArgs.Empty);
                        continue;
                    }
                    else if (msg.Status == MessageStatus.AuthFailure)
                    {
                        _Settings.Logger?.Invoke(_Header + "authentication failed");
                        _Events.HandleAuthenticationFailure(this, EventArgs.Empty);
                        continue;
                    }
                    else if (msg.Status == MessageStatus.AuthRequired)
                    {
                        _Settings.Logger?.Invoke(_Header + "authentication required by server; please authenticate using pre-shared key"); 
                        string psk = _Callbacks.HandleAuthenticationRequested();
                        if (!String.IsNullOrEmpty(psk)) Authenticate(psk);
                        continue;
                    }

                    #endregion

                    #region Process-Message

                    if (msg.SyncRequest != null && msg.SyncRequest.Value)
                    { 
                        DateTime expiration = WatsonCommon.GetExpirationTimestamp(msg);
                        byte[] msgData = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize); 
                         
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
                                SendInternal(respMsg, contentLength, stream);
                            }
                        }
                        else
                        { 
                            _Settings.Logger?.Invoke(_Header + "expired synchronous request received and discarded");
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
                            _Settings.Logger?.Invoke(_Header + "expired synchronous response received and discarded");
                        }
                    }
                    else
                    {
                        byte[] msgData = null;

                        if (_Events.IsUsingMessages)
                        { 
                            msgData = await WatsonCommon.ReadMessageDataAsync(msg, _Settings.StreamBufferSize); 
                            MessageReceivedFromServerEventArgs args = new MessageReceivedFromServerEventArgs(msg.Metadata, msgData);
                            await Task.Run(() => _Events.HandleMessageReceived(this, args));
                        }
                        else if (_Events.IsUsingStreams)
                        {
                            StreamReceivedFromServerEventArgs sr = null;
                            WatsonStream ws = null;

                            if (msg.ContentLength >= _Settings.MaxProxiedStreamSize)
                            {
                                ws = new WatsonStream(msg.ContentLength, msg.DataStream);
                                sr = new StreamReceivedFromServerEventArgs(msg.Metadata, msg.ContentLength, ws);
                                // sr = new StreamReceivedFromServerEventArgs(msg.Metadata, msg.ContentLength, msg.DataStream);
                                // must run synchronously, data exists in the underlying stream
                                _Events.HandleStreamReceived(this, sr);
                            }
                            else
                            {
                                MemoryStream ms = WatsonCommon.DataStreamToMemoryStream(msg.ContentLength, msg.DataStream, _Settings.StreamBufferSize);
                                ws = new WatsonStream(msg.ContentLength, ms);
                                sr = new StreamReceivedFromServerEventArgs(msg.Metadata, msg.ContentLength, ws);
                                // sr = new StreamReceivedFromServerEventArgs(msg.Metadata, msg.ContentLength, ms);
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
                    _Settings.Logger?.Invoke(
                        _Header + "data receiver exception for " + _ServerIp + ":" + _ServerPort + ":" +
                        Environment.NewLine +
                        SerializationHelper.SerializeJson(e, true) +
                        Environment.NewLine);

                    _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                    break;
                } 
                finally
                {
                    if (readLocked && _ReadLock != null) _ReadLock.Release();
                }
            }

            Connected = false;

            _Settings.Logger?.Invoke(_Header + "data receiver terminated for " + _ServerIp + ":" + _ServerPort);
            _Events.HandleServerDisconnected(this, EventArgs.Empty);
        }

        private bool SendInternal(WatsonMessage msg, long contentLength, Stream stream)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (!Connected) return false;

            if (contentLength > 0 && (stream == null || !stream.CanRead))
            {
                throw new ArgumentException("Cannot read from supplied stream.");
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

                _Statistics.IncrementSentMessages();
                _Statistics.AddSentBytes(contentLength);
                return true;
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(
                    _Header + "failed to write message to " + _ServerIp + ":" + _ServerPort + ":" +
                    Environment.NewLine +
                    SerializationHelper.SerializeJson(e, true));

                _Events.HandleExceptionEncountered(this, new ExceptionEventArgs(e));
                 
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
            
            if (contentLength > 0  && (stream == null || !stream.CanRead))
            {
                throw new ArgumentException("Cannot read from supplied stream.");
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

                _Statistics.IncrementSentMessages();
                _Statistics.AddSentBytes(contentLength);
                return true;
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(_Header + "message write exception: " + 
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

                _Statistics.IncrementSentMessages();
                _Statistics.AddSentBytes(contentLength);
            }
            catch (Exception e)
            {
                _Settings.Logger?.Invoke(_Header + "message write exception: " +
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

        private async Task SendDataStreamAsync(long contentLength, Stream stream)
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
                    await _DataStream.WriteAsync(buffer, 0, bytesRead);
                    bytesRemaining -= bytesRead;
                }
            }  

            await _DataStream.FlushAsync();
        }
         
        private async Task MonitorForExpiredSyncResponses()
        {
            while (_TokenSource != null && !_TokenSource.IsCancellationRequested)
            {
                if (_Token.IsCancellationRequested) break;

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

                await Task.Delay(1000);
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

                _Client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, _Keepalive.TcpKeepAliveTime);
                _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, _Keepalive.TcpKeepAliveInterval);
                _Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, _Keepalive.TcpKeepAliveRetryCount);

#elif NETFRAMEWORK

                byte[] keepAlive = new byte[12]; 
                Buffer.BlockCopy(BitConverter.GetBytes((uint)1), 0, keepAlive, 0, 4); 
                Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveTime), 0, keepAlive, 4, 4);  
                Buffer.BlockCopy(BitConverter.GetBytes((uint)_Keepalive.TcpKeepAliveInterval), 0, keepAlive, 8, 4);  
                _Client.Client.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);

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