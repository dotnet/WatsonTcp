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
        /// Function called when authentication has succeeded.  Expects a response of 'true'.
        /// </summary>
        public Func<bool> AuthenticationSucceeded = null;

        /// <summary>
        /// Function called when authentication has failed.  Expects a response of 'true'.
        /// </summary>
        public Func<bool> AuthenticationFailure = null;

        /// <summary>
        /// Function called when a message is received.  
        /// A byte array containing the message data is passed to this function.
        /// It is expected that 'true' will be returned.
        /// </summary>
        public Func<byte[], bool> MessageReceived = null;

        /// <summary>
        /// Method to call when a message is received from a client.
        /// The IP:port is passed to this method as a string, along with a long indicating the number of bytes to read from the stream.
        /// It is expected that the method will return true;
        /// </summary>
        public Func<long, Stream, bool> StreamReceived = null;

        /// <summary>
        /// Function called when the client successfully connects to the server.
        /// It is expected that 'true' will be returned.
        /// </summary>
        public Func<bool> ServerConnected = null;

        /// <summary>
        /// Function called when the client disconnects from the server.
        /// It is expected that 'true' will be returned.
        /// </summary>
        public Func<bool> ServerDisconnected = null;

        /// <summary>
        /// Enable acceptance of SSL certificates from the server that cannot be validated.
        /// </summary>
        public bool AcceptInvalidCertificates = true;

        /// <summary>
        /// Require mutual authentication between the server and this client.
        /// </summary>
        public bool MutuallyAuthenticate = false;
        
        /// <summary>
        /// Indicates whether or not the client is connected to the server.
        /// </summary>
        public bool Connected { get; private set; }

        #endregion

        #region Private-Members

        private bool _Disposed = false;
        private int _ReadStreamBufferSize = 65536;
        private Mode _Mode; 
        private string _SourceIp;
        private int _SourcePort;
        private string _ServerIp;
        private int _ServerPort; 
        private TcpClient _Client;

        private SslStream _Ssl;
        private X509Certificate2 _SslCertificate;
        private X509Certificate2Collection _SslCertificateCollection;
        
        private readonly SemaphoreSlim _SendLock;
        private CancellationTokenSource _TokenSource;
        private CancellationToken _Token;

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

            _Mode = Mode.Tcp;
            _ServerIp = serverIp;
            _ServerPort = serverPort;
            _SendLock = new SemaphoreSlim(1);
            _Ssl = null;
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

            _Mode = Mode.Ssl;
            _ServerIp = serverIp;
            _ServerPort = serverPort;
            _SendLock = new SemaphoreSlim(1);
            _SslCertificate = null;
            if (String.IsNullOrEmpty(pfxCertPass)) _SslCertificate = new X509Certificate2(pfxCertFile);
            else _SslCertificate = new X509Certificate2(pfxCertFile, pfxCertPass);

            _SslCertificateCollection = new X509Certificate2Collection
            {
                _SslCertificate
            };

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
            IAsyncResult asyncResult = null;
            WaitHandle waitHandle = null;

            if (_Mode == Mode.Tcp)
            {
                #region TCP

                Log("Watson TCP client connecting to " + _ServerIp + ":" + _ServerPort);

                _Client.LingerState = new LingerOption(true, 0);
                asyncResult = _Client.BeginConnect(_ServerIp, _ServerPort, null, null);
                waitHandle = asyncResult.AsyncWaitHandle;

                try
                {
                    if (!asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5), false))
                    {
                        _Client.Close();
                        throw new TimeoutException("Timeout connecting to " + _ServerIp + ":" + _ServerPort);
                    }

                    _Client.EndConnect(asyncResult);

                    _SourceIp = ((IPEndPoint)_Client.Client.LocalEndPoint).Address.ToString();
                    _SourcePort = ((IPEndPoint)_Client.Client.LocalEndPoint).Port;
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

                #endregion
            }
            else if (_Mode == Mode.Ssl)
            {
                #region SSL

                Log("Watson TCP client connecting with SSL to " + _ServerIp + ":" + _ServerPort);
                
                asyncResult = _Client.BeginConnect(_ServerIp, _ServerPort, null, null);
                waitHandle = asyncResult.AsyncWaitHandle;

                try
                {
                    if (!asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5), false))
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
                        _Ssl = new SslStream(_Client.GetStream(), false, new RemoteCertificateValidationCallback(AcceptCertificate));
                    }
                    else
                    {
                        // do not accept invalid SSL certificates
                        _Ssl = new SslStream(_Client.GetStream(), false);
                    }

                    _Ssl.AuthenticateAsClient(_ServerIp, _SslCertificateCollection, SslProtocols.Tls12, !AcceptInvalidCertificates);

                    if (!_Ssl.IsEncrypted)
                    {
                        throw new AuthenticationException("Stream is not encrypted");
                    }

                    if (!_Ssl.IsAuthenticated)
                    {
                        throw new AuthenticationException("Stream is not authenticated");
                    }

                    if (MutuallyAuthenticate && !_Ssl.IsMutuallyAuthenticated)
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

                #endregion
            }
            else
            {
                throw new ArgumentException("Unknown mode: " + _Mode.ToString());
            }
            
            if (ServerConnected != null)
            {
                Task.Run(() => ServerConnected());
            }

            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;
            Task.Run(async () => await DataReceiver(_Token), _Token);
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
         
        #endregion

        #region Private-Methods

        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed)
            {
                return;
            }

            if (disposing)
            {
                if (_Ssl != null)
                {
                    try
                    {
                        _Ssl.Close();
                    }
                    catch (Exception)
                    {

                    }
                }

                if (_Client != null)
                {
                    if (_Client.Connected)
                    {
                        try
                        {
                            NetworkStream ns = _Client.GetStream();
                            if (ns != null) ns.Close();
                        }
                        catch (Exception)
                        {

                        }
                    }

                    try
                    {
                        _Client.Close();
                    }
                    catch (Exception)
                    {

                    }
                }

                _TokenSource.Cancel();
                _TokenSource.Dispose();

                _SendLock.Dispose();

                Connected = false;
            }

            _Disposed = true;
        }
         
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

        private void LogException(string method, Exception e)
        {
            Log("================================================================================");
            Log(" = Method: " + method);
            Log(" = Exception Type: " + e.GetType().ToString());
            Log(" = Exception Data: " + e.Data);
            Log(" = Inner Exception: " + e.InnerException);
            Log(" = Exception Message: " + e.Message);
            Log(" = Exception Source: " + e.Source);
            Log(" = Exception StackTrace: " + e.StackTrace);
            Log("================================================================================");
        }

        private async Task DataReceiver(CancellationToken? cancelToken=null)
        {
            try
            {
                #region Wait-for-Data

                while (true)
                {
                    cancelToken?.ThrowIfCancellationRequested();

                    #region Check-Connection

                    if (_Client == null)
                    {
                        Log("*** DataReceiver null TCP interface detected, disconnection or close assumed");
                        break;
                    }

                    if (!_Client.Connected)
                    {
                        Log("*** DataReceiver server disconnected");
                        break;
                    }
                     
                    if (_Ssl != null && !_Ssl.CanRead)
                    {
                        Log("*** DataReceiver cannot read from SSL stream");
                        break;
                    }

                    #endregion

                    #region Read-Message-and-Handle

                    WatsonMessage msg = null;

                    if (_Ssl != null)
                    {
                        msg = new WatsonMessage(_Ssl, Debug);

                        if (ReadDataStream)
                        {
                            await msg.Build();
                        }
                        else
                        {
                            await msg.BuildStream();
                        }
                    }
                    else
                    {
                        msg = new WatsonMessage(_Client.GetStream(), Debug);

                        if (ReadDataStream)
                        {
                            await msg.Build();
                        }
                        else
                        {
                            await msg.BuildStream();
                        }
                    }

                    if (msg == null)
                    {
                        await Task.Delay(30);
                        continue;
                    }

                    if (msg.Status == MessageStatus.AuthSuccess)
                    {
                        Log("DataReceiver successfully authenticated");
                        AuthenticationSucceeded?.Invoke();
                        continue;
                    }
                    else if (msg.Status == MessageStatus.AuthFailure)
                    {
                        Log("DataReceiver authentication failed, please authenticate using pre-shared key");
                        AuthenticationFailure?.Invoke();
                        continue;
                    }

                    if (msg.Status == MessageStatus.AuthRequired)
                    {
                        Log("DataReceiver authentication required, please authenticate using pre-shared key");
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
                            Task<bool> unawaited = Task.Run(() => MessageReceived(msg.Data));
                        }
                    }
                    else
                    {
                        StreamReceived?.Invoke(msg.ContentLength, msg.DataStream);
                    }

                    #endregion
                }

                #endregion
            }
            catch (OperationCanceledException)
            {

            }
            catch (ObjectDisposedException)
            {

            }
            catch (IOException)
            {

            }
            catch (Exception e)
            {
                if (Debug)
                {
                    Log("*** DataReceiver server disconnected unexpectedly");
                    Log(Common.SerializeJson(e));
                }
            }
            finally
            {
                Connected = false;
                ServerDisconnected?.Invoke(); 
            }
        }

        private bool MessageWrite(WatsonMessage msg)
        {
            bool disconnectDetected = false;
            long dataLen = 0;
            if (msg.Data != null) dataLen = msg.Data.Length;

            try
            {
                #region Check-if-Connected

                if (_Client == null)
                {
                    Log("MessageWrite client is null");
                    disconnectDetected = true;
                    return false;
                }

                #endregion

                #region Send-Message
                 
                byte[] headerBytes = msg.ToHeaderBytes(dataLen);

                _SendLock.Wait();
                try
                {
                    if (_Mode == Mode.Tcp)
                    {
                        NetworkStream ns = _Client.GetStream();
                        ns.Write(headerBytes, 0, headerBytes.Length);
                        if (msg.Data != null && msg.Data.Length > 0) ns.Write(msg.Data, 0, msg.Data.Length);
                        ns.Flush();
                    }
                    else if (_Mode == Mode.Ssl)
                    {
                        _Ssl.Write(headerBytes, 0, headerBytes.Length);
                        if (msg.Data != null && msg.Data.Length > 0) _Ssl.Write(msg.Data, 0, msg.Data.Length);
                        _Ssl.Flush();
                    }
                    else
                    {
                        throw new ArgumentException("Unknown mode: " + _Mode.ToString());
                    }

                    string logMessage = "MessageWrite sent " + Encoding.UTF8.GetString(headerBytes);
                    if (msg.Data != null && msg.Data.Length > 0) logMessage += Encoding.UTF8.GetString(msg.Data);
                    Log(logMessage);
                }
                finally
                {
                    _SendLock.Release();
                }

                return true;

                #endregion
            }
            catch (ObjectDisposedException ObjDispInner)
            {
                Log("*** MessageWrite server disconnected (obj disposed exception): " + ObjDispInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (SocketException SockInner)
            {
                Log("*** MessageWrite server disconnected (socket exception): " + SockInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (InvalidOperationException InvOpInner)
            {
                Log("*** MessageWrite server disconnected (invalid operation exception): " + InvOpInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (IOException IOInner)
            {
                Log("*** MessageWrite server disconnected (IO exception): " + IOInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (Exception e)
            {
                Log(Common.SerializeJson(e));
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

        private bool MessageWrite(byte[] data)
        {
            bool disconnectDetected = false;
            long dataLen = 0;
            if (data != null) dataLen = data.Length;

            try
            {
                #region Check-if-Connected

                if (_Client == null)
                {
                    Log("MessageWrite client is null");
                    disconnectDetected = true;
                    return false;
                }

                #endregion

                #region Send-Message

                WatsonMessage msg = new WatsonMessage(data, Debug);
                byte[] headerBytes = msg.ToHeaderBytes(dataLen);

                _SendLock.Wait();
                try
                {
                    if (_Mode == Mode.Tcp)
                    {
                        NetworkStream ns = _Client.GetStream();
                        ns.Write(headerBytes, 0, headerBytes.Length);
                        if (data != null && data.Length > 0) ns.Write(data, 0, data.Length);
                        ns.Flush();
                    }
                    else if (_Mode == Mode.Ssl)
                    {
                        _Ssl.Write(headerBytes, 0, headerBytes.Length);
                        if (data != null && data.Length > 0) _Ssl.Write(data, 0, data.Length);
                        _Ssl.Flush();
                    }
                    else
                    {
                        throw new ArgumentException("Unknown mode: " + _Mode.ToString());
                    }

                    Log("MessageWrite sent " + Encoding.UTF8.GetString(headerBytes) + Encoding.UTF8.GetString(data));
                }
                finally
                {
                    _SendLock.Release();
                }

                return true;

                #endregion
            }
            catch (ObjectDisposedException ObjDispInner)
            {
                Log("*** MessageWrite server disconnected (obj disposed exception): " + ObjDispInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (SocketException SockInner)
            {
                Log("*** MessageWrite server disconnected (socket exception): " + SockInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (InvalidOperationException InvOpInner)
            {
                Log("*** MessageWrite server disconnected (invalid operation exception): " + InvOpInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (IOException IOInner)
            {
                Log("*** MessageWrite server disconnected (IO exception): " + IOInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (Exception e)
            {
                LogException("MessageWrite", e);
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
                #region Check-if-Connected

                if (_Client == null)
                {
                    Log("MessageWrite client is null");
                    disconnectDetected = true;
                    return false;
                }

                #endregion

                #region Send-Message

                WatsonMessage msg = new WatsonMessage(contentLength, stream, Debug);
                byte[] headerBytes = msg.ToHeaderBytes(contentLength);

                int bytesRead = 0;
                long bytesRemaining = contentLength;
                byte[] buffer = new byte[_ReadStreamBufferSize];

                _SendLock.Wait();
                try
                {
                    if (_Mode == Mode.Tcp)
                    {
                        NetworkStream ns = _Client.GetStream(); 
                        ns.Write(headerBytes, 0, headerBytes.Length);

                        if (contentLength > 0)
                        {
                            while (bytesRemaining > 0)
                            {
                                bytesRead = stream.Read(buffer, 0, buffer.Length);
                                if (bytesRead > 0)
                                {
                                    ns.Write(buffer, 0, bytesRead);
                                    bytesRemaining -= bytesRead;
                                }
                            }
                        }

                        ns.Flush();
                    }
                    else if (_Mode == Mode.Ssl)
                    {
                        _Ssl.Write(headerBytes, 0, headerBytes.Length);

                        if (contentLength > 0)
                        {
                            while (bytesRemaining > 0)
                            {
                                bytesRead = stream.Read(buffer, 0, buffer.Length);
                                if (bytesRead > 0)
                                {
                                    _Ssl.Write(buffer, 0, bytesRead);
                                    bytesRemaining -= bytesRead;
                                }
                            }
                        }

                        _Ssl.Flush();
                    }
                    else
                    {
                        throw new ArgumentException("Unknown mode: " + _Mode.ToString());
                    }
                }
                finally
                {
                    _SendLock.Release();
                } 

                return true;

                #endregion
            }
            catch (ObjectDisposedException ObjDispInner)
            {
                Log("*** MessageWrite server disconnected (obj disposed exception): " + ObjDispInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (SocketException SockInner)
            {
                Log("*** MessageWrite server disconnected (socket exception): " + SockInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (InvalidOperationException InvOpInner)
            {
                Log("*** MessageWrite server disconnected (invalid operation exception): " + InvOpInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (IOException IOInner)
            {
                Log("*** MessageWrite server disconnected (IO exception): " + IOInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (Exception e)
            {
                LogException("MessageWrite", e);
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
            bool disconnectDetected = false;
            long dataLen = 0;
            if (data != null) dataLen = data.Length;

            try
            {
                #region Check-if-Connected

                if (_Client == null)
                {
                    Log("MessageWriteAsync client is null");
                    disconnectDetected = true;
                    return false;
                }

                #endregion 

                #region Send-Message

                WatsonMessage msg = new WatsonMessage(data, Debug);
                byte[] headerBytes = msg.ToHeaderBytes(dataLen);

                if (Debug) Log(msg.ToString());

                await _SendLock.WaitAsync();
                try
                {
                    if (_Mode == Mode.Tcp)
                    {
                        NetworkStream ns = _Client.GetStream();
                        await ns.WriteAsync(headerBytes, 0, headerBytes.Length);
                        if (data != null && data.Length > 0) await ns.WriteAsync(data, 0, data.Length);
                        await ns.FlushAsync();
                    }
                    else if (_Mode == Mode.Ssl)
                    {
                        await _Ssl.WriteAsync(headerBytes, 0, headerBytes.Length);
                        if (data != null && data.Length > 0) await _Ssl.WriteAsync(data, 0, data.Length);
                        await _Ssl.FlushAsync();
                    }
                    else
                    {
                        throw new ArgumentException("Unknown mode: " + _Mode.ToString());
                    }
                }
                finally
                {
                    _SendLock.Release();
                }

                return true;

                #endregion
            }
            catch (ObjectDisposedException ObjDispInner)
            {
                Log("*** MessageWriteAsync server disconnected (obj disposed exception): " + ObjDispInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (SocketException SockInner)
            {
                Log("*** MessageWriteAsync server disconnected (socket exception): " + SockInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (InvalidOperationException InvOpInner)
            {
                Log("*** MessageWriteAsync server disconnected (invalid operation exception): " + InvOpInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (IOException IOInner)
            {
                Log("*** MessageWriteAsync server disconnected (IO exception): " + IOInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (Exception e)
            {
                LogException("MessageWriteAsync", e);
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

        private async Task<bool> MessageWriteAsync(long contentLength, Stream stream)
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
                #region Check-if-Connected

                if (_Client == null)
                {
                    Log("MessageWriteAsync client is null");
                    disconnectDetected = true;
                    return false;
                }

                #endregion

                #region Send-Message

                WatsonMessage msg = new WatsonMessage(contentLength, stream, Debug);
                byte[] headerBytes = msg.ToHeaderBytes(contentLength);

                int bytesRead = 0;
                long bytesRemaining = contentLength;
                byte[] buffer = new byte[_ReadStreamBufferSize];

                await _SendLock.WaitAsync();
                try
                {
                    if (_Mode == Mode.Tcp)
                    {
                        NetworkStream ns = _Client.GetStream();
                        await ns.WriteAsync(headerBytes, 0, headerBytes.Length);

                        if (contentLength > 0)
                        {
                            while (bytesRemaining > 0)
                            {
                                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                                if (bytesRead > 0)
                                {
                                    await ns.WriteAsync(buffer, 0, bytesRead);
                                    bytesRemaining -= bytesRead;
                                }
                            }
                        }

                        await ns.FlushAsync();
                    }
                    else if (_Mode == Mode.Ssl)
                    {
                        await _Ssl.WriteAsync(headerBytes, 0, headerBytes.Length);

                        if (contentLength > 0)
                        {
                            while (bytesRemaining > 0)
                            {
                                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                                if (bytesRead > 0)
                                {
                                    await _Ssl.WriteAsync(buffer, 0, bytesRead);
                                    bytesRemaining -= bytesRead;
                                }
                            }
                        }

                        await _Ssl.FlushAsync();
                    }
                    else
                    {
                        throw new ArgumentException("Unknown mode: " + _Mode.ToString());
                    }
                }
                finally
                {
                    _SendLock.Release();
                }

                return true;

                #endregion
            }
            catch (ObjectDisposedException ObjDispInner)
            {
                Log("*** MessageWriteAsync server disconnected (obj disposed exception): " + ObjDispInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (SocketException SockInner)
            {
                Log("*** MessageWriteAsync server disconnected (socket exception): " + SockInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (InvalidOperationException InvOpInner)
            {
                Log("*** MessageWriteAsync server disconnected (invalid operation exception): " + InvOpInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (IOException IOInner)
            {
                Log("*** MessageWriteAsync server disconnected (IO exception): " + IOInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (Exception e)
            {
                LogException("MessageWriteAsync", e);
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

        #endregion
    }
}
