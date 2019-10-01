using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        /// Enable or disable console debugging.
        /// </summary>
        public bool Debug = false;

        /// <summary>
        /// Permitted IP addresses.
        /// </summary>
        public List<string> PermittedIPs = null;

        /// <summary>
        /// Method to call when a client connects to the server.
        /// The IP:port is passed to this method as a string.
        /// </summary>
        public Func<string, Task> ClientConnected = null;

        /// <summary>
        /// Method to call when a client disconnects from the server.
        /// The IP:port is passed to this method as a string.
        /// </summary>
        public Func<string, Task> ClientDisconnected = null;

        /// <summary>
        /// Use the 'MessageReceived' callback only when 'ReadDataStream' is set to 'true'.
        /// This callback is called when a message is received from a connected client. 
        /// The entire message payload is passed to your application in a byte array, along with the IP:port of the client.
        /// You cannot set both 'MessageReceived' and 'StreamReceived' simultaneously.
        /// </summary>
        public Func<string, byte[], Task> MessageReceived
        {
            get
            {
                return _MessageReceived;
            }
            set
            {
                if (_StreamReceived != null) throw new InvalidOperationException("Only one of 'MessageReceived' and 'StreamReceived' can be set.");
                _MessageReceived = value;
            }
        }

        /// <summary>
        /// Use the 'StreamReceived' callback only when 'ReadDataStream' is set to 'false'.
        /// This callback is called when a message is received from a connected client.
        /// The IP:port of the client is passed to your application, along with the number of bytes to read and the stream from which the data should be read.
        /// You cannot set both 'MessageReceived' and 'StreamReceived' simultaneously.
        /// </summary>
        public Func<string, long, Stream, Task> StreamReceived
        {
            get
            {
                return _StreamReceived;
            }
            set
            {
                if (_MessageReceived != null) throw new InvalidOperationException("Only one of 'MessageReceived' and 'StreamReceived' can be set.");
                _StreamReceived = value;
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

        #endregion Public-Members

        #region Private-Members
         
        private int _ReadStreamBufferSize = 65536;
        private int _IdleClientTimeoutSeconds = 0;
        private Mode _Mode;
        private string _ListenerIp;
        private int _ListenerPort;
        private IPAddress _ListenerIpAddress;
        private TcpListener _Listener;

        private X509Certificate2 _SslCertificate;

        private ConcurrentDictionary<string, ClientMetadata> _Clients = new ConcurrentDictionary<string, ClientMetadata>();
        private ConcurrentDictionary<string, DateTime> _ClientsLastSeen = new ConcurrentDictionary<string, DateTime>();
        private ConcurrentDictionary<string, DateTime> _UnauthenticatedClients = new ConcurrentDictionary<string, DateTime>();

        private CancellationTokenSource _TokenSource = new CancellationTokenSource();
        private CancellationToken _Token;

        private Func<string, byte[], Task> _MessageReceived = null;
        private Func<string, long, Stream, Task> _StreamReceived = null;

        #endregion Private-Members

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
        }

        #endregion Constructors-and-Factories

        #region Public-Methods

        /// <summary>
        /// Tear down the server and dispose of background workers.
        /// </summary>
        public void Dispose()
        {
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
                discMsg.Data = null;
                discMsg.ContentLength = 0;

                foreach (KeyValuePair<string, ClientMetadata> currMetadata in _Clients)
                {
                    MessageWrite(currMetadata.Value, discMsg, null);
                    currMetadata.Value.Dispose();
                }
                 
                _Clients = null;
                _UnauthenticatedClients = null;
            } 

            Log("WatsonTcpServer Disposed"); 
        }

        /// <summary>
        /// Start the server.
        /// </summary>
        public void Start()
        {
            if (_StreamReceived == null && _MessageReceived == null)
            {
                throw new InvalidOperationException("Either 'MessageReceived' or 'StreamReceived' must first be set.");
            }

            if (_Mode == Mode.Tcp)
            {
                Log("Watson TCP server starting on " + _ListenerIp + ":" + _ListenerPort);
            }
            else if (_Mode == Mode.Ssl)
            {
                Log("Watson TCP SSL server starting on " + _ListenerIp + ":" + _ListenerPort);
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
            if (_StreamReceived == null && _MessageReceived == null)
            {
                throw new InvalidOperationException("Either 'MessageReceived' or 'StreamReceived' must first be set.");
            }

            if (_Mode == Mode.Tcp)
            {
                Log("Watson TCP server starting on " + _ListenerIp + ":" + _ListenerPort);
            }
            else if (_Mode == Mode.Ssl)
            {
                Log("Watson TCP SSL server starting on " + _ListenerIp + ":" + _ListenerPort);
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
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(string ipPort, byte[] data)
        {
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Log("*** Send unable to find client " + ipPort);
                return false;
            }

            WatsonMessage msg = new WatsonMessage(data, Debug);
            return MessageWrite(client, msg, data);
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
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Log("*** Send unable to find client " + ipPort);
                return false;
            }

            WatsonMessage msg = new WatsonMessage(contentLength, stream, Debug);
            return MessageWrite(client, msg, contentLength, stream);
        }

        /// <summary>
        /// Send data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, byte[] data)
        {
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Log("*** SendAsync unable to find client " + ipPort);
                return false;
            }

            WatsonMessage msg = new WatsonMessage(data, Debug);
            return await MessageWriteAsync(client, msg, data);
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
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Log("*** SendAsync unable to find client " + ipPort);
                return false;
            }

            WatsonMessage msg = new WatsonMessage(contentLength, stream, Debug);
            return await MessageWriteAsync(client, msg, contentLength, stream);
        }

        /// <summary>
        /// Determine whether or not the specified client is connected to the server.
        /// </summary>
        /// <returns>Boolean indicating if the client is connected to the server.</returns>
        public bool IsClientConnected(string ipPort)
        {
            return (_Clients.TryGetValue(ipPort, out ClientMetadata client));
        }

        /// <summary>
        /// List the IP:port of each connected client.
        /// </summary>
        /// <returns>A string list containing each client IP:port.</returns>
        public List<string> ListClients()
        {
            Dictionary<string, ClientMetadata> clients = _Clients.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            List<string> ret = new List<string>();
            foreach (KeyValuePair<string, ClientMetadata> curr in clients)
            {
                ret.Add(curr.Key);
            }
            return ret;
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
                Log("*** DisconnectClient unable to find client " + ipPort);
            }
            else
            {
                byte[] data = Encoding.UTF8.GetBytes("Removed from server");
                WatsonMessage removeMsg = new WatsonMessage();
                removeMsg.Status = MessageStatus.Removed;
                removeMsg.Data = null;
                removeMsg.ContentLength = 0;
                MessageWrite(client, removeMsg, null); 
                client.Dispose();
                _Clients.TryRemove(ipPort, out ClientMetadata removed);
            }
        }

        #endregion Public-Methods

        #region Private-Methods
         
        private void Log(string msg)
        {
            if (Debug) Console.WriteLine(msg);
        }

        private void LogException(string method, Exception e)
        {
            Log("");
            Log("An exception was encountered.");
            Log("   Method        : " + method);
            Log("   Type          : " + e.GetType().ToString());
            Log("   Data          : " + e.Data);
            Log("   Inner         : " + e.InnerException);
            Log("   Message       : " + e.Message);
            Log("   Source        : " + e.Source);
            Log("   StackTrace    : " + e.StackTrace);
            Log("");
        }

        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // return true; // Allow untrusted certificates.
            return AcceptInvalidCertificates;
        }

        private async Task AcceptConnections()
        {
            _Listener.Start();

            while (!_Token.IsCancellationRequested)
            {
                string clientIpPort = String.Empty;

                try
                {
                    #region Accept-Connection-and-Validate-IP

                    TcpClient tcpClient = await _Listener.AcceptTcpClientAsync();
                    tcpClient.LingerState.Enabled = false;

                    string clientIp = ((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address.ToString();
                    if (PermittedIPs != null && PermittedIPs.Count > 0)
                    {
                        if (!PermittedIPs.Contains(clientIp))
                        {
                            Log("*** AcceptConnections rejecting connection from " + clientIp + " (not permitted)");
                            tcpClient.Close();
                            continue;
                        }
                    }

                    ClientMetadata client = new ClientMetadata(tcpClient);
                    clientIpPort = client.IpPort;

                    #endregion Accept-Connection-and-Validate-IP

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

                    Log("*** AcceptConnections accepted connection from " + client.IpPort);
                }
                catch (Exception e)
                {
                    Log("*** AcceptConnections exception " + clientIpPort + " " + e.Message);
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
                    Log("*** StartTls stream from " + client.IpPort + " not encrypted");
                    client.Dispose();
                    return false;
                }

                if (!client.SslStream.IsAuthenticated)
                {
                    Log("*** StartTls stream from " + client.IpPort + " not authenticated");
                    client.Dispose();
                    return false;
                }

                if (MutuallyAuthenticate && !client.SslStream.IsMutuallyAuthenticated)
                {
                    Log("*** StartTls stream from " + client.IpPort + " failed mutual authentication");
                    client.Dispose();
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
                        Log("*** StartTls IOException " + client.IpPort + " closed the connection.");
                        break;

                    case "The handshake failed due to an unexpected packet format.":
                        Log("*** StartTls IOException " + client.IpPort + " disconnected, invalid handshake.");
                        break;

                    default:
                        Log("*** StartTls IOException from " + client.IpPort + Environment.NewLine + ex.ToString());
                        break;
                }

                client.Dispose();
                return false;
            }
            catch (Exception ex)
            {
                Log("*** StartTls Exception from " + client.IpPort + Environment.NewLine + ex.ToString());
                client.Dispose();
                return false;
            }

            return true;
        }

        private void FinalizeConnection(ClientMetadata client)
        {
            #region Add-to-Client-List

            ClientMetadata removed = null;
            DateTime ts;

            _Clients.TryRemove(client.IpPort, out removed);
            _ClientsLastSeen.TryRemove(client.IpPort, out ts);

            _Clients.TryAdd(client.IpPort, client);
            _ClientsLastSeen.TryAdd(client.IpPort, DateTime.Now);
             
            #endregion Add-to-Client-List

            #region Request-Authentication

            if (!String.IsNullOrEmpty(PresharedKey))
            {
                Log("*** FinalizeConnection soliciting authentication material from " + client.IpPort);
                _UnauthenticatedClients.TryAdd(client.IpPort, DateTime.Now);

                byte[] data = Encoding.UTF8.GetBytes("Authentication required");
                WatsonMessage authMsg = new WatsonMessage();
                authMsg.Status = MessageStatus.AuthRequired;
                authMsg.Data = null;
                authMsg.ContentLength = 0;
                MessageWrite(client, authMsg, null);
            }

            #endregion Request-Authentication

            #region Start-Data-Receiver

            Log("*** FinalizeConnection starting data receiver for " + client.IpPort);
            if (ClientConnected != null)
            {
                Task.Run(() => ClientConnected(client.IpPort));
            }

            Task.Run(async () => await DataReceiver(client, client.Token));

            #endregion Start-Data-Receiver 
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
                    Log("*** IsConnected " + client.IpPort + " exception using send: " + e.Message);
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
                    Log("*** IsConnected " + client.IpPort + " exception using poll/peek: " + e.Message);
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
            string header = "[" + client.IpPort + "]";

            while (true)
            {
                try
                { 
                    token.ThrowIfCancellationRequested();

                    if (!IsConnected(client)) break;

                    WatsonMessage msg = null;
                    bool buildSuccess = false;

                    if (_Mode == Mode.Ssl)
                    {
                        msg = new WatsonMessage(client.SslStream, Debug); 
                    }
                    else if (_Mode == Mode.Tcp)
                    {
                        msg = new WatsonMessage(client.NetworkStream, Debug); 
                    } 
                     
                    if (_MessageReceived != null)
                    {
                        buildSuccess = await msg.Build();
                    }
                    else if (_StreamReceived != null)
                    {
                        buildSuccess = await msg.BuildStream();
                    }
                    else
                    {
                        break;
                    }

                    if (!buildSuccess)
                    {
                        break;
                    }
                     
                    if (msg == null)
                    {
                        // no message available
                        await Task.Delay(30);
                        continue;
                    }

                    if (!String.IsNullOrEmpty(PresharedKey))
                    {
                        if (_UnauthenticatedClients.ContainsKey(client.IpPort))
                        {
                            Log(header + " message received from unauthenticated endpoint");

                            if (msg.Status == MessageStatus.AuthRequested)
                            {
                                // check preshared key
                                if (msg.PresharedKey != null && msg.PresharedKey.Length > 0)
                                {
                                    string clientPsk = Encoding.UTF8.GetString(msg.PresharedKey).Trim();
                                    if (PresharedKey.Trim().Equals(clientPsk))
                                    {
                                        Log(header + " accepted authentication");
                                        _UnauthenticatedClients.TryRemove(client.IpPort, out DateTime dt);
                                        byte[] data = Encoding.UTF8.GetBytes("Authentication successful");
                                        WatsonMessage authMsg = new WatsonMessage(data, Debug);
                                        authMsg.Status = MessageStatus.AuthSuccess;
                                        MessageWrite(client, authMsg, null);
                                        continue;
                                    }
                                    else
                                    {
                                        Log(header + " declined authentication");
                                        byte[] data = Encoding.UTF8.GetBytes("Authentication declined");
                                        WatsonMessage authMsg = new WatsonMessage(data, Debug);
                                        authMsg.Status = MessageStatus.AuthFailure;
                                        MessageWrite(client, authMsg, null);
                                        continue;
                                    }
                                }
                                else
                                {
                                    Log(header + " no authentication material");
                                    byte[] data = Encoding.UTF8.GetBytes("No authentication material");
                                    WatsonMessage authMsg = new WatsonMessage(data, Debug);
                                    authMsg.Status = MessageStatus.AuthFailure;
                                    MessageWrite(client, authMsg, null);
                                    continue;
                                }
                            }
                            else
                            {
                                // decline the message
                                Log(header + " no authentication material");
                                byte[] data = Encoding.UTF8.GetBytes("Authentication required");
                                WatsonMessage authMsg = new WatsonMessage(data, Debug);
                                authMsg.Status = MessageStatus.AuthRequired;
                                MessageWrite(client, authMsg, null);
                                continue;
                            }
                        }
                    }

                    if (msg.Status == MessageStatus.Disconnecting)
                    {
                        Log(header + " sent notification of disconnection");
                        break;
                    }

                    if (msg.Status == MessageStatus.Removed)
                    {
                        Log(header + " sent notification of removal");
                        break;
                    }
                     
                    if (_MessageReceived != null)
                    {
                        // does not need to be awaited, because the stream has been fully read
                        Task unawaited = Task.Run(() => _MessageReceived(client.IpPort, msg.Data));
                    }
                    else if (_StreamReceived != null)
                    {
                        // must be awaited, the stream has not been fully read
                        await _StreamReceived(client.IpPort, msg.ContentLength, msg.DataStream);
                    }
                    else
                    {
                        break;
                    }

                    UpdateClientLastSeen(client.IpPort);
                }  
                catch (Exception e)
                {
                    Log(Environment.NewLine +
                        "[" + client.IpPort + "] Data receiver exception:" +
                        Environment.NewLine +
                        e.ToString() +
                        Environment.NewLine);
                    break;
                }
            }
             
            Log(header + " data receiver terminated");

            _Clients.TryRemove(client.IpPort, out ClientMetadata removedClient);
            _ClientsLastSeen.TryRemove(client.IpPort, out DateTime ts);
            _UnauthenticatedClients.TryRemove(client.IpPort, out DateTime removedDt);
                 
            if (ClientDisconnected != null)
            {
                Task unawaited = Task.Run(() => ClientDisconnected(client.IpPort));
            }

            Log(header + " disposing"); 
            client.Dispose();  
        }
          
        private bool MessageWrite(ClientMetadata client, WatsonMessage msg, byte[] data)
        {
            int dataLen = 0;
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

            return MessageWrite(client, msg, dataLen, ms);
        }

        private bool MessageWrite(ClientMetadata client, WatsonMessage msg, long contentLength, Stream stream)
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

            byte[] headerBytes = msg.ToHeaderBytes(contentLength);

            int bytesRead = 0;
            long bytesRemaining = contentLength;
            byte[] buffer = new byte[_ReadStreamBufferSize];

            client.WriteLock.Wait(1);

            try
            {
                if (_Mode == Mode.Tcp)
                {
                    client.NetworkStream.Write(headerBytes, 0, headerBytes.Length);

                    if (contentLength > 0)
                    {
                        while (bytesRemaining > 0)
                        {
                            bytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead > 0)
                            {
                                client.NetworkStream.Write(buffer, 0, bytesRead);
                                bytesRemaining -= bytesRead;
                            }
                        }
                    }

                    client.NetworkStream.Flush();
                }
                else if (_Mode == Mode.Ssl)
                {
                    client.SslStream.Write(headerBytes, 0, headerBytes.Length);

                    if (contentLength > 0)
                    {
                        while (bytesRemaining > 0)
                        {
                            bytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead > 0)
                            {
                                client.SslStream.Write(buffer, 0, bytesRead);
                                bytesRemaining -= bytesRead;
                            }
                        }
                    }

                    client.SslStream.Flush();
                }
                 
                return true;
            }
            catch (Exception e)
            {
                Log("*** MessageWrite " + client.IpPort + " disconnected due to exception: " + e.Message);
                return false;
            }
            finally
            {
                client.WriteLock.Release();
            }
        }

        private async Task<bool> MessageWriteAsync(ClientMetadata client, WatsonMessage msg, byte[] data)
        {
            int dataLen = 0;
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

            return await MessageWriteAsync(client, msg, dataLen, ms);
        }

        private async Task<bool> MessageWriteAsync(ClientMetadata client, WatsonMessage msg, long contentLength, Stream stream)
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

            byte[] headerBytes = msg.ToHeaderBytes(contentLength);

            int bytesRead = 0;
            long bytesRemaining = contentLength;
            byte[] buffer = new byte[_ReadStreamBufferSize];

            client.WriteLock.Wait(1);

            try
            {
                if (_Mode == Mode.Tcp)
                {
                    await client.NetworkStream.WriteAsync(headerBytes, 0, headerBytes.Length);

                    if (contentLength > 0)
                    {
                        while (bytesRemaining > 0)
                        {
                            bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                            if (bytesRead > 0)
                            {
                                await client.NetworkStream.WriteAsync(buffer, 0, bytesRead);
                                bytesRemaining -= bytesRead;
                            }
                        }
                    }

                    await client.NetworkStream.FlushAsync();
                }
                else if (_Mode == Mode.Ssl)
                {
                    await client.SslStream.WriteAsync(headerBytes, 0, headerBytes.Length);

                    if (contentLength > 0)
                    {
                        while (bytesRemaining > 0)
                        {
                            bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                            if (bytesRead > 0)
                            {
                                await client.SslStream.WriteAsync(buffer, 0, bytesRead);
                                bytesRemaining -= bytesRead;
                            }
                        }
                    }

                    await client.SslStream.FlushAsync();
                }
                 
                return true;
            }
            catch (Exception e)
            {
                Log("*** MessageWriteAsync " + client.IpPort + " disconnected due to exception: " + e.Message);
                return false;
            }
            finally
            {
                client.WriteLock.Release();
            }
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
                    Log("Disconnecting client " + curr.Key + " due to idle");
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

        #endregion Private-Methods
    }
}