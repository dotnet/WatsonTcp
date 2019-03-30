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
        /// Enable or disable console debugging.
        /// </summary>
        public bool Debug = false;

        /// <summary>
        /// Function called when a message is received.  
        /// A byte array containing the message data is passed to this function.
        /// It is expected that 'true' will be returned.
        /// </summary>
        public Func<byte[], bool> MessageReceived = null;

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
            if (presharedKey.Length > 16) throw new ArgumentException("Preshared key length must be 16 or fewer characters");

            presharedKey = presharedKey.PadRight(16, ' ');
            WatsonMessage msg = new WatsonMessage(Encoding.UTF8.GetBytes(presharedKey), Debug);
            msg.Status = MessageStatus.AuthRequired;
            msg.PresharedKey = Encoding.UTF8.GetBytes(presharedKey);
            Send(msg);
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
        /// Send data to the server.
        /// </summary>
        /// <param name="msg">Populated message object.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(WatsonMessage msg)
        {
            return MessageWrite(msg);
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
        /// Send data to the server asynchronously
        /// </summary>
        /// <param name="msg">Populated message object.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(WatsonMessage msg)
        {
            return await MessageWriteAsync(msg);
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
                if (_Ssl != null) _Ssl.Close();  

                if (_Client != null)
                {
                    if (_Client.Connected)
                    {
                        NetworkStream ns = _Client.GetStream();
                        if (ns != null)
                        {
                            ns.Close();
                        }
                    }

                    _Client.Close(); 
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
                        await msg.Build();
                    }
                    else
                    {
                        msg = new WatsonMessage(_Client.GetStream(), Debug);
                        await msg.Build();
                    }

                    if (msg == null)
                    {
                        await Task.Delay(30);
                        continue;
                    }

                    if (msg.Status == MessageStatus.AuthSuccess)
                    {
                        Log("DataReceiver successfully authenticated");
                    }

                    if (msg.Status == MessageStatus.AuthRequired)
                    {
                        Log("DataReceiver received authentication request, please authenticate using pre-shared key");
                    }

                    Task<bool> unawaited = Task.Run(() => MessageReceived(msg.Data));

                    #endregion
                }

                #endregion
            }
            catch (OperationCanceledException)
            {
                throw; // normal cancellation
            }
            catch (Exception)
            {
                Log("*** DataReceiver server disconnected");
            }
            finally
            {
                Connected = false;
                ServerDisconnected?.Invoke();
            }
        }

        private bool MessageWrite(byte[] data)
        {
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

                WatsonMessage msg = new WatsonMessage(data, Debug);
                byte[] msgBytes = msg.ToBytes();

                _SendLock.Wait();
                try
                {
                    if (_Mode == Mode.Tcp)
                    {
                        _Client.GetStream().Write(msgBytes, 0, msgBytes.Length);
                        _Client.GetStream().Flush();
                    }
                    else if (_Mode == Mode.Ssl)
                    {
                        _Ssl.Write(msgBytes, 0, msgBytes.Length);
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

        private bool MessageWrite(WatsonMessage msg)
        {
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
                 
                byte[] msgBytes = msg.ToBytes();

                _SendLock.Wait();
                try
                {
                    if (_Mode == Mode.Tcp)
                    {
                        _Client.GetStream().Write(msgBytes, 0, msgBytes.Length);
                        _Client.GetStream().Flush();
                    }
                    else if (_Mode == Mode.Ssl)
                    {
                        _Ssl.Write(msgBytes, 0, msgBytes.Length);
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
                byte[] msgBytes = msg.ToBytes();

                if (Debug) Log(msg.ToString());

                await _SendLock.WaitAsync();
                try
                {
                    if (_Mode == Mode.Tcp)
                    {
                        await _Client.GetStream().WriteAsync(msgBytes, 0, msgBytes.Length);
                        await _Client.GetStream().FlushAsync();
                    }
                    else if (_Mode == Mode.Ssl)
                    {
                        await _Ssl.WriteAsync(msgBytes, 0, msgBytes.Length);
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

        private async Task<bool> MessageWriteAsync(WatsonMessage msg)
        {
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
                
                byte[] msgBytes = msg.ToBytes();

                if (Debug) Log(msg.ToString());

                await _SendLock.WaitAsync();
                try
                {
                    if (_Mode == Mode.Tcp)
                    {
                        await _Client.GetStream().WriteAsync(msgBytes, 0, msgBytes.Length);
                        await _Client.GetStream().FlushAsync();
                    }
                    else if (_Mode == Mode.Ssl)
                    {
                        await _Ssl.WriteAsync(msgBytes, 0, msgBytes.Length);
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
