using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using WatsonTcp.Message;

namespace WatsonTcp
{
    /// <summary>
    /// Watson TCP client, with or without SSL.
    /// </summary>
    public class WatsonTcpClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Enable or disable full reading of input streams.  When enabled, use MessageReceived.  When disabled, use StreamReceived.
        /// </summary>
        public bool ReadDataStream = true;

        /// <summary>
        /// Buffer size to use when reading input and output streams.  Default is 65536.
        /// </summary>
        public int ReadStreamBufferSize
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
        /// Enable or disable console debugging.
        /// </summary>
        public bool Debug = false;

        /// <summary>
        /// Function called when authentication is requested from the server.  Expects the 16-byte preshared key.
        /// </summary>
        public Func<string> AuthenticationRequested = null;

        /// <summary>
        /// Function called when authentication has succeeded.
        /// </summary>
        public Func<Task> AuthenticationSucceeded = null;

        /// <summary>
        /// Function called when authentication has failed.
        /// </summary>
        public Func<Task> AuthenticationFailure = null;

        /// <summary>
        /// Function called when a message is received.
        /// A byte array containing the message data is passed to this function.
        /// </summary>
        public Func<byte[], Task> MessageReceived = null;

        /// <summary>
        /// Method to call when a message is received from a client.
        /// The number of bytes (long) and the stream containing the data are passed to this function.
        /// </summary>
        public Func<long, Stream, Task> StreamReceived = null;

        /// <summary>
        /// Function called when the client successfully connects to the server.
        /// </summary>
        public Func<Task> ServerConnected = null;

        /// <summary>
        /// Function called when the client disconnects from the server.
        /// </summary>
        public Func<Task> ServerDisconnected = null;

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

        #endregion Public-Members

        #region Private-Members
         
        private int _ReadStreamBufferSize = 65536;
        private int _ConnectTimeoutSeconds = 5;
        private Mode _Mode;
        private string _SourceIp;
        private int _SourcePort;
        private string _ServerIp;
        private int _ServerPort;
        private bool _MutuallyAuthenticate = false;
        private TcpClient _Client = null;
        private NetworkStream _TcpStream = null;
        private SslStream _SslStream = null;

        private X509Certificate2 _SslCertificate = null;
        private X509Certificate2Collection _SslCertificateCollection = null;

        private SemaphoreSlim _WriteLock = new SemaphoreSlim(1);
        private SemaphoreSlim _ReadLock = new SemaphoreSlim(1);

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private CancellationToken _Token;

        #endregion Private-Members

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
        }

        #endregion Constructors-and-Factories

        #region Public-Methods

        /// <summary>
        /// Tear down the client and dispose of background workers.
        /// </summary>
        public void Dispose()
        {
            Log("Disposing WatsonTcpClient");
              
            if (Connected)
            {
                WatsonMessage msg = new WatsonMessage();
                msg.Status = MessageStatus.Disconnecting;
                msg.Data = null;
                msg.ContentLength = 0;
                MessageWrite(msg);
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

            Connected = false; 
            Log("Dispose routine complete");
        }

        /// <summary>
        /// Start the client and establish a connection to the server.
        /// </summary>
        public void Start()
        {
            _Client = new TcpClient();
            IAsyncResult asyncResult = null;
            WaitHandle waitHandle = null;
            bool connectSuccess = false;

            if (_Mode == Mode.Tcp)
            {
                #region TCP

                Log("Watson TCP client connecting to " + _ServerIp + ":" + _ServerPort);

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

                Log("Watson TCP client connecting with SSL to " + _ServerIp + ":" + _ServerPort);

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

            if (ServerConnected != null)
            {
                Task serverConnected = Task.Run(() => ServerConnected());
            }

            Task dataReceiver = Task.Run(() => DataReceiver(), _Token);
        }

        /// <summary>
        /// Start the client and establish a connection to the server.
        /// </summary>
        /// <returns></returns>
        public Task StartAsync()
        {
            _Client = new TcpClient();
            IAsyncResult asyncResult = null;
            WaitHandle waitHandle = null;
            bool connectSuccess = false;

            if (_Mode == Mode.Tcp)
            {
                #region TCP

                Log("Watson TCP client connecting to " + _ServerIp + ":" + _ServerPort);

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

                Log("Watson TCP client connecting with SSL to " + _ServerIp + ":" + _ServerPort);

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

            if (ServerConnected != null)
            {
                Task serverConnected = Task.Run(() => ServerConnected());
            }

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

            presharedKey = presharedKey.PadRight(16, ' ');
            WatsonMessage msg = new WatsonMessage();
            msg.Status = MessageStatus.AuthRequested;
            msg.PresharedKey = Encoding.UTF8.GetBytes(presharedKey);
            msg.Data = null;
            msg.ContentLength = 0;
            MessageWrite(msg);
        }

        /// <summary>
        /// Send data to the server.
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(byte[] data)
        {
            return MessageWrite(data);
        }

        /// <summary>
        /// Send data to the server using a stream.
        /// </summary>
        /// <param name="contentLength">The number of bytes in the stream.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(long contentLength, Stream stream)
        {
            return MessageWrite(contentLength, stream);
        }

        /// <summary>
        /// Send data to the server asynchronously
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(byte[] data)
        {
            return await MessageWriteAsync(data);
        }

        /// <summary>
        /// Send data to the server from a stream asynchronously.
        /// </summary>
        /// <param name="contentLength">The number of bytes to send.</param>
        /// <param name="stream">The stream containing the data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(long contentLength, Stream stream)
        {
            return await MessageWriteAsync(contentLength, stream);
        }

        #endregion Public-Methods

        #region Private-Methods
         
        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // return true; // Allow untrusted certificates.
            return AcceptInvalidCertificates;
        }

        private void Log(string msg)
        {
            if (Debug)
            {
                Console.WriteLine(msg);
            }
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
                        Log("Disconnect detected");
                        break;
                    }

                    WatsonMessage msg = null;
                    readLocked = await _ReadLock.WaitAsync(1);
                    bool buildSuccess = false;

                    if (_SslStream != null)
                    {
                        msg = new WatsonMessage(_SslStream, Debug); 
                    }
                    else
                    {
                        msg = new WatsonMessage(_TcpStream, Debug); 
                    }

                    if (ReadDataStream)
                    {
                        buildSuccess = await msg.Build();
                    }
                    else
                    {
                        buildSuccess = await msg.BuildStream();
                    }

                    if (!buildSuccess)
                    {
                        Log("Message build failed due to disconnect");
                        break;
                    }

                    if (msg == null)
                    { 
                        await Task.Delay(30);
                        continue;
                    }

                    if (msg.Status == MessageStatus.Removed)
                    {
                        Log("Disconnect due to server-side removal");
                        break;
                    }
                    else if (msg.Status == MessageStatus.Disconnecting)
                    {
                        Log("Disconnect due to server shutting down");
                        break;
                    }
                    else if (msg.Status == MessageStatus.AuthSuccess)
                    {
                        Log("Authentication successful");
                        AuthenticationSucceeded?.Invoke();
                        continue;
                    }
                    else if (msg.Status == MessageStatus.AuthFailure)
                    {
                        Log("Authentication failed");
                        AuthenticationFailure?.Invoke();
                        continue;
                    }

                    if (msg.Status == MessageStatus.AuthRequired)
                    {
                        Log("Authentication required by server, please authenticate using pre-shared key");
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

                    if (ReadDataStream)
                    {
                        if (MessageReceived != null)
                        {
                            // does not need to be awaited, because the stream has been fully read
                            Task unawaited = Task.Run(() => MessageReceived(msg.Data));
                        }
                    }
                    else
                    {
                        if (StreamReceived != null)
                        {
                            // must be awaited, because the content has not yet been fully read
                            await StreamReceived(msg.ContentLength, msg.DataStream);
                        }
                    } 
                } 
                catch (Exception e)
                {
                    Log(Environment.NewLine +
                        "Data receiver exception:" +
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

            Log("Data receiver terminated");
            Connected = false;
            ServerDisconnected?.Invoke();
            Dispose();
        }

        private bool MessageWrite(byte[] data)
        {
            long dataLen = 0;
            MemoryStream ms = new MemoryStream();
            if (data != null && data.Length > 0)
            {
                dataLen = data.Length;
                ms.Write(data, 0, data.Length);
                ms.Seek(0, SeekOrigin.Begin);
            }
            else
            {
                ms = new MemoryStream(new byte[0]);
            }

            return MessageWrite(dataLen, ms);
        }

        private bool MessageWrite(long contentLength, Stream stream)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater bytes.");
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

                WatsonMessage msg = new WatsonMessage(contentLength, stream, Debug);
                byte[] headerBytes = msg.ToHeaderBytes(contentLength);

                int bytesRead = 0;
                long bytesRemaining = contentLength;
                byte[] buffer = new byte[_ReadStreamBufferSize];

                _WriteLock.Wait(1);

                try
                {
                    if (_Mode == Mode.Tcp)
                    {
                        _TcpStream.Write(headerBytes, 0, headerBytes.Length);

                        if (contentLength > 0)
                        {
                            while (bytesRemaining > 0)
                            {
                                bytesRead = stream.Read(buffer, 0, buffer.Length);
                                if (bytesRead > 0)
                                {
                                    _TcpStream.Write(buffer, 0, bytesRead);
                                    bytesRemaining -= bytesRead;
                                }
                            }
                        }

                        _TcpStream.Flush();
                    }
                    else if (_Mode == Mode.Ssl)
                    {
                        _SslStream.Write(headerBytes, 0, headerBytes.Length);

                        if (contentLength > 0)
                        {
                            while (bytesRemaining > 0)
                            {
                                bytesRead = stream.Read(buffer, 0, buffer.Length);
                                if (bytesRead > 0)
                                {
                                    _SslStream.Write(buffer, 0, bytesRead);
                                    bytesRemaining -= bytesRead;
                                }
                            }
                        }

                        _SslStream.Flush();
                    } 
                }
                finally
                {
                    _WriteLock.Release();
                }
                 
                return true;
            }
            catch (Exception e)
            {
                Log(Environment.NewLine +
                    "MessageWrite exception encountered:" +
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

        private bool MessageWrite(WatsonMessage msg)
        {
            bool disconnectDetected = false;
            long dataLen = 0;
            if (msg.Data != null) dataLen = msg.Data.Length;

            try
            {
                if (_Client == null
                    || !_Client.Connected)
                {
                    disconnectDetected = true;
                    return false;
                }

                byte[] headerBytes = msg.ToHeaderBytes(dataLen);

                _WriteLock.Wait(1);

                try
                {
                    if (_Mode == Mode.Tcp)
                    {
                        _TcpStream.Write(headerBytes, 0, headerBytes.Length);
                        if (msg.Data != null && msg.Data.Length > 0) _TcpStream.Write(msg.Data, 0, msg.Data.Length);
                        _TcpStream.Flush();
                    }
                    else if (_Mode == Mode.Ssl)
                    {
                        _SslStream.Write(headerBytes, 0, headerBytes.Length);
                        if (msg.Data != null && msg.Data.Length > 0) _SslStream.Write(msg.Data, 0, msg.Data.Length);
                        _SslStream.Flush();
                    }
                }
                finally
                {
                    _WriteLock.Release();
                }

                return true;
            }
            catch (Exception e)
            {
                Log(Environment.NewLine +
                    "MessageWrite exception encountered:" +
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

        private async Task<bool> MessageWriteAsync(byte[] data)
        {
            long dataLen = 0;
            MemoryStream ms = new MemoryStream();
            if (data != null)
            {
                dataLen = data.Length;
                ms.Write(data, 0, data.Length);
                ms.Seek(0, SeekOrigin.Begin);
            }
            else
            {
                ms = new MemoryStream(new byte[0]);
            }

            return await MessageWriteAsync(dataLen, ms);
        }

        private async Task<bool> MessageWriteAsync(long contentLength, Stream stream)
        {
            if (!Connected) return false;
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater bytes.");
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

                WatsonMessage msg = new WatsonMessage(contentLength, stream, Debug);
                byte[] headerBytes = msg.ToHeaderBytes(contentLength);

                int bytesRead = 0;
                long bytesRemaining = contentLength;
                byte[] buffer = new byte[_ReadStreamBufferSize];

                await _WriteLock.WaitAsync();

                try
                {
                    if (_Mode == Mode.Tcp)
                    {
                        await _TcpStream.WriteAsync(headerBytes, 0, headerBytes.Length);

                        if (contentLength > 0)
                        {
                            while (bytesRemaining > 0)
                            {
                                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                                if (bytesRead > 0)
                                {
                                    await _TcpStream.WriteAsync(buffer, 0, bytesRead);
                                    bytesRemaining -= bytesRead;
                                }
                            }
                        }

                        await _TcpStream.FlushAsync();
                    }
                    else if (_Mode == Mode.Ssl)
                    {
                        await _SslStream.WriteAsync(headerBytes, 0, headerBytes.Length);

                        if (contentLength > 0)
                        {
                            while (bytesRemaining > 0)
                            {
                                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                                if (bytesRead > 0)
                                {
                                    await _SslStream.WriteAsync(buffer, 0, bytesRead);
                                    bytesRemaining -= bytesRead;
                                }
                            }
                        }

                        await _SslStream.FlushAsync();
                    }
                    else
                    {
                        throw new ArgumentException("Unknown mode: " + _Mode.ToString());
                    }
                }
                finally
                {
                    _WriteLock.Release();
                }
                 
                return true;
            }
            catch (Exception e)
            {
                Log(Environment.NewLine +
                    "MessageWrite exception encountered:" +
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

        #endregion Private-Methods
    }
}