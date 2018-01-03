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

namespace WatsonTcp
{
    /// <summary>
    /// Watson TCP server with SSL.
    /// </summary>
    public class WatsonTcpSslServer : IDisposable
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private bool _Debug;
        private string _ListenerIp;
        private int _ListenerPort;
        private IPAddress _ListenerIpAddress;
        private TcpListener _Listener;
        private X509Certificate2 _SslCertificate;
        private bool _AcceptInvalidCerts;
        private bool _MutuallyAuthenticate;
        private int _ActiveClients;
        private ConcurrentDictionary<string, ClientMetadata> _Clients;
        private List<string> _PermittedIps;
        private CancellationTokenSource _TokenSource;
        private CancellationToken _Token;
        private Func<string, bool> _ClientConnected = null;
        private Func<string, bool> _ClientDisconnected = null;
        private Func<string, byte[], bool> _MessageReceived = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize the Watson TCP server with SSL.
        /// </summary>
        /// <param name="listenerIp">The IP address on which the server should listen, nullable.</param>
        /// <param name="listenerPort">The TCP port on which the server should listen.</param>
        /// <param name="pfxCertFile">The file containing the SSL certificate.</param>
        /// <param name="pfxCertPass">The password for the SSL certificate.</param>
        /// <param name="acceptInvalidCerts">True to accept invalid or expired SSL certificates.</param>
        /// <param name="mutualAuthentication">True to mutually authenticate client and server.</param>
        /// <param name="clientConnected">Function to be called when a client connects.</param>
        /// <param name="clientDisconnected">Function to be called when a client disconnects.</param>
        /// <param name="messageReceived">Function to be called when a message is received.</param>
        /// <param name="debug">Enable or debug logging messages.</param>
        public WatsonTcpSslServer(
            string listenerIp,
            int listenerPort,
            string pfxCertFile,
            string pfxCertPass,
            bool acceptInvalidCerts,
            bool mutualAuthentication,
            Func<string, bool> clientConnected,
            Func<string, bool> clientDisconnected,
            Func<string, byte[], bool> messageReceived,
            bool debug)
        {
            if (listenerPort < 1) throw new ArgumentOutOfRangeException(nameof(listenerPort));
            if (messageReceived == null) throw new ArgumentNullException(nameof(_MessageReceived));
            if (String.IsNullOrEmpty(pfxCertFile)) throw new ArgumentNullException(nameof(pfxCertFile));

            _AcceptInvalidCerts = acceptInvalidCerts;
            _MutuallyAuthenticate = mutualAuthentication;

            _ClientConnected = clientConnected;
            _ClientDisconnected = clientDisconnected;
            _MessageReceived = messageReceived;

            _Debug = debug;

            _PermittedIps = null;

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

            _SslCertificate = null;
            if (String.IsNullOrEmpty(pfxCertPass)) _SslCertificate = new X509Certificate2(pfxCertFile);
            else _SslCertificate = new X509Certificate2(pfxCertFile, pfxCertPass);

            Log("WatsonTcpSslServer starting on " + _ListenerIp + ":" + _ListenerPort);

            _Listener = new TcpListener(_ListenerIpAddress, _ListenerPort);
            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;
            _ActiveClients = 0;
            _Clients = new ConcurrentDictionary<string, ClientMetadata>();

            _Listener.Start();
            WaitForClients();
        }

        /// <summary>
        /// Initialize the Watson TCP server with SSL.
        /// </summary>
        /// <param name="listenerIp">The IP address on which the server should listen, nullable.</param>
        /// <param name="listenerPort">The TCP port on which the server should listen.</param>
        /// <param name="pfxCertFile">The file containing the SSL certificate.</param>
        /// <param name="pfxCertPass">The password for the SSL certificate.</param>
        /// <param name="acceptInvalidCerts">True to accept invalid or expired SSL certificates.</param>
        /// <param name="mutualAuthentication">True to mutually authenticate client and server.</param>
        /// <param name="permittedIps">List of IP address strings that are allowed to connect (null to permit all).</param>
        /// <param name="clientConnected">Function to be called when a client connects.</param>
        /// <param name="clientDisconnected">Function to be called when a client disconnects.</param>
        /// <param name="messageReceived">Function to be called when a message is received.</param>
        /// <param name="debug">Enable or debug logging messages.</param>
        public WatsonTcpSslServer(
            string listenerIp,
            int listenerPort,
            string pfxCertFile,
            string pfxCertPass,
            bool acceptInvalidCerts,
            bool mutualAuthentication,
            IEnumerable<string> permittedIps,
            Func<string, bool> clientConnected,
            Func<string, bool> clientDisconnected,
            Func<string, byte[], bool> messageReceived,
            bool debug)
        {
            if (listenerPort < 1) throw new ArgumentOutOfRangeException(nameof(listenerPort));
            if (messageReceived == null) throw new ArgumentNullException(nameof(_MessageReceived));

            _AcceptInvalidCerts = acceptInvalidCerts;
            _MutuallyAuthenticate = mutualAuthentication;

            _ClientConnected = clientConnected;
            _ClientDisconnected = clientDisconnected;
            _MessageReceived = messageReceived;

            _Debug = debug;

            if (permittedIps != null && permittedIps.Count() > 0) _PermittedIps = new List<string>(permittedIps);

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

            _SslCertificate = null;
            if (String.IsNullOrEmpty(pfxCertPass)) _SslCertificate = new X509Certificate2(pfxCertFile);
            else _SslCertificate = new X509Certificate2(pfxCertFile, pfxCertPass);

            Log("WatsonTcpSslServer starting on " + _ListenerIp + ":" + _ListenerPort);

            _Listener = new TcpListener(_ListenerIpAddress, _ListenerPort);
            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;
            _ActiveClients = 0;
            _Clients = new ConcurrentDictionary<string, ClientMetadata>();

            _Listener.Start();
            WaitForClients();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tear down the server and dispose of background workers.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Send data to the specified client.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Boolean indicating if the message was sent successfully.</returns>
        public bool Send(string ipPort, byte[] data)
        {
            ClientMetadata client;
            if (!_Clients.TryGetValue(ipPort, out client))
            {
                Log("Send unable to find client " + ipPort);
                return false;
            }

            return MessageWrite(client, data);
        }

        /// <summary>
        /// Send data to the specified client, asynchronously.
        /// </summary>
        /// <param name="ipPort">IP:port of the recipient client.</param>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(string ipPort, byte[] data)
        {
            ClientMetadata client;
            if (!_Clients.TryGetValue(ipPort, out client))
            {
                Log("Send unable to find client " + ipPort);
                return false;
            }

            return await MessageWriteAsync(client, data);
        }

        /// <summary>
        /// Determine whether or not the specified client is connected to the server.
        /// </summary>
        /// <returns>Boolean indicating if the client is connected to the server.</returns>
        public bool IsClientConnected(string ipPort)
        {
            ClientMetadata client;
            return (_Clients.TryGetValue(ipPort, out client));
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

        #endregion

        #region Private-Methods

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _TokenSource.Cancel();
            }
        }

        private void Log(string msg)
        {
            if (_Debug)
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

        private string BytesToHex(byte[] data)
        {
            if (data == null || data.Length < 1) return "(null)";
            return BitConverter.ToString(data).Replace("-", "");
        }

        private bool AcceptCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // return true; // Allow untrusted certificates.
            return _AcceptInvalidCerts;
        }

        private void WaitForClients()
        {
            _Listener.BeginAcceptTcpClient(new AsyncCallback(OnClientConnected), null);
        }

        private void OnClientConnected(IAsyncResult asyncResult)
        {
            ClientMetadata client = null;
            try
            {
                TcpClient clientSocket = _Listener.EndAcceptTcpClient(asyncResult);
                client = new ClientMetadata(clientSocket);

                Log("OnClientConnected received connection from: " + client.IpPort);

                string clientIp = ((IPEndPoint)client.Tcp.Client.RemoteEndPoint).Address.ToString();
                if (IsAllowedIp(clientIp))
                {
                    if (_AcceptInvalidCerts)
                    {
                        // accept invalid certs
                        client.Ssl = new SslStream(client.Tcp.GetStream(), false, new RemoteCertificateValidationCallback(AcceptCertificate));
                    }
                    else
                    {
                        // do not accept invalid SSL certificates
                        client.Ssl = new SslStream(client.Tcp.GetStream(), false);
                    }

                    client.Ssl.BeginAuthenticateAsServer(_SslCertificate, true, SslProtocols.Tls12, true, OnAuthenticateAsServer, client);
                }
                else
                {
                    Log("*** OnClientConnected rejecting connection from " + clientIp + " (not permitted)");
                    client.Tcp.Close();
                }
            }
            catch (SocketException ex)
            {
                Log("OnClientConnected socket exception from " + client.IpPort + Environment.NewLine + ex.ToString());
            }
            catch (Exception ex)
            {
                Log("OnClientConnected general exception from " + client.IpPort + Environment.NewLine + ex.ToString());
            }

            WaitForClients();
        }

        private bool IsAllowedIp(string clientIp)
        {
            if (_PermittedIps != null && _PermittedIps.Count > 0)
            {
                if (!_PermittedIps.Contains(clientIp))
                {
                    return false;
                }
            }

            return true;
        }

        private void OnAuthenticateAsServer(IAsyncResult asyncResult)
        {
            ClientMetadata client = null;
            try
            {
                client = asyncResult.AsyncState as ClientMetadata;
                client.Ssl.EndAuthenticateAsServer(asyncResult);

                if (!client.Ssl.IsEncrypted)
                {
                    Log("*** OnAuthenticateAsServer stream from " + client.IpPort + " not encrypted");
                    client.Tcp.Close();
                    return;
                }

                if (!client.Ssl.IsAuthenticated)
                {
                    Log("*** OnAuthenticateAsServer stream from " + client.IpPort + " not authenticated");
                    client.Tcp.Close();
                    return;
                }

                if (_MutuallyAuthenticate && !client.Ssl.IsMutuallyAuthenticated)
                {
                    Log("*** OnAuthenticateAsServer stream from " + client.IpPort + " failed mutual authentication");
                    client.Tcp.Close();
                    return;
                }

                FinaliseConnection(client);
            }
            catch (IOException)
            {
                // Some type of problem initiating the SSL connection
                Log("OnAuthenticateAsServer rejected due to IOException " + client.IpPort + " (now " + _ActiveClients + " clients)");
                if (client != null)
                {
                    client.Tcp.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());

                if (client != null)
                {
                    client.Tcp.Close();
                }
            }
        }

        private void FinaliseConnection(ClientMetadata client)
        {
            var unawaited = Task.Run(() =>
            {
                #region Add-to-Client-List

                if (!AddClient(client))
                {
                    Log("*** FinaliseConnection unable to add client " + client.IpPort);
                    client.Tcp.Close();
                    return;
                }

                // Do not decrement in this block, decrement is done by the connection reader
                _ActiveClients++;

                #endregion

                #region Start-Data-Receiver

                CancellationToken dataReceiverToken = default(CancellationToken);

                Log("FinaliseConnection starting data receiver for " + client.IpPort + " (now " + _ActiveClients + " clients)");
                if (_ClientConnected != null)
                {
                    Task.Run(() => _ClientConnected(client.IpPort));
                }

                Task.Run(async () => await DataReceiver(client, dataReceiverToken), dataReceiverToken);

                #endregion

            }, _Token);
        }

        private bool IsConnected(ClientMetadata client)
        {
            if (client.Tcp.Connected)
            {
                if ((client.Tcp.Client.Poll(0, SelectMode.SelectWrite)) && (!client.Tcp.Client.Poll(0, SelectMode.SelectError)))
                {
                    byte[] buffer = new byte[1];
                    if (client.Tcp.Client.Receive(buffer, SocketFlags.Peek) == 0)
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
            else
            {
                return false;
            }
        }

        private async Task DataReceiver(ClientMetadata client, CancellationToken? cancelToken=null)
        {
            try
            {
                #region Wait-for-Data

                while (true)
                {
                    cancelToken?.ThrowIfCancellationRequested();

                    try
                    {
                        if (!IsConnected(client)) break;

                        byte[] data = await MessageReadAsync(client);
                        if (data == null)
                        {
                            // no message available
                            await Task.Delay(30);
                            continue;
                        }

                        if (_MessageReceived != null)
                        {
                            var unawaited = Task.Run(() => _MessageReceived(client.IpPort, data));
                        }
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }

                #endregion
            }
            finally
            {
                _ActiveClients--;
                RemoveClient(client);
                if (_ClientDisconnected != null)
                {
                    var unawaited = Task.Run(() => _ClientDisconnected(client.IpPort));
                }
                Log("DataReceiver client " + client.IpPort + " disconnected (now " + _ActiveClients + " clients active)");
            }
        }

        private bool AddClient(ClientMetadata client)
        {
            ClientMetadata removed;
            if (!_Clients.TryRemove(client.IpPort, out removed))
            {
                // do nothing, it probably did not exist anyway
            }

            _Clients.TryAdd(client.IpPort, client);
            Log("AddClient added client " + client.IpPort);
            return true;
        }

        private bool RemoveClient(ClientMetadata client)
        {
            ClientMetadata removedClient;
            if (!_Clients.TryRemove(client.IpPort, out removedClient))
            {
                Log("RemoveClient unable to remove client " + client.IpPort);
                return false;
            }
            else
            {
                Log("RemoveClient removed client " + client.IpPort);
                return true;
            }
        }

        private byte[] MessageRead(ClientMetadata client)
        {
            /*
             *
             * Do not catch exceptions, let them get caught by the data reader
             * to destroy the connection
             *
             */

            #region Variables

            int bytesRead = 0;
            int sleepInterval = 25;
            int maxTimeout = 500;
            int currentTimeout = 0;
            bool timeout = false;

            byte[] headerBytes;
            string header = "";
            long contentLength;
            byte[] contentBytes;

            if (!client.Ssl.CanRead) return null;

            #endregion

            #region Read-Header

            using (MemoryStream headerMs = new MemoryStream())
            {
                #region Read-Header-Bytes

                byte[] headerBuffer = new byte[1];
                timeout = false;
                currentTimeout = 0;
                int read = 0;

                while ((read = client.Ssl.ReadAsync(headerBuffer, 0, headerBuffer.Length).Result) > 0)
                {
                    if (read > 0)
                    {
                        headerMs.Write(headerBuffer, 0, read);
                        bytesRead += read;
                        currentTimeout = 0;

                        if (bytesRead > 1)
                        {
                            // check if end of headers reached
                            if ((int)headerBuffer[0] == 58) break;
                        }
                        else
                        {
                            if (currentTimeout >= maxTimeout)
                            {
                                timeout = true;
                                break;
                            }
                            else
                            {
                                currentTimeout += sleepInterval;
                                Task.Delay(sleepInterval).Wait();
                            }
                        }
                    }
                }

                if (timeout)
                {
                    Log("*** MessageRead timeout " + currentTimeout + "ms/" + maxTimeout + "ms exceeded while reading header after reading " + bytesRead + " bytes");
                    return null;
                }

                headerBytes = headerMs.ToArray();
                if (headerBytes == null || headerBytes.Length < 1)
                {
                    return null;
                }

                #endregion

                #region Process-Header

                header = Encoding.UTF8.GetString(headerBytes);
                header = header.Replace(":", "");

                if (!Int64.TryParse(header, out contentLength))
                {
                    Log("*** MessageRead malformed message from " + client.IpPort + " (message header not an integer)");
                    return null;
                }

                #endregion
            }

            #endregion

            #region Read-Data

            using (MemoryStream dataMs = new MemoryStream())
            {
                long bytesRemaining = contentLength;
                timeout = false;
                currentTimeout = 0;

                int read = 0;
                byte[] buffer;
                long bufferSize = 2048;
                if (bufferSize > bytesRemaining) bufferSize = bytesRemaining;
                buffer = new byte[bufferSize];

                while ((read = client.Ssl.ReadAsync(buffer, 0, buffer.Length).Result) > 0)
                {
                    if (read > 0)
                    {
                        dataMs.Write(buffer, 0, read);
                        bytesRead = bytesRead + read;
                        bytesRemaining = bytesRemaining - read;
                        currentTimeout = 0;

                        // reduce buffer size if number of bytes remaining is
                        // less than the pre-defined buffer size of 2KB
                        if (bytesRemaining < bufferSize)
                        {
                            bufferSize = bytesRemaining;
                            // Console.WriteLine("Adjusting buffer size to " + bytesRemaining);
                        }

                        buffer = new byte[bufferSize];

                        // check if read fully
                        if (bytesRemaining == 0) break;
                        if (bytesRead == contentLength) break;
                    }
                    else
                    {
                        if (currentTimeout >= maxTimeout)
                        {
                            timeout = true;
                            break;
                        }
                        else
                        {
                            currentTimeout += sleepInterval;
                            Task.Delay(sleepInterval).Wait();
                        }
                    }
                }

                if (timeout)
                {
                    Log("*** MessageRead timeout " + currentTimeout + "ms/" + maxTimeout + "ms exceeded while reading content after reading " + bytesRead + " bytes");
                    return null;
                }

                contentBytes = dataMs.ToArray();
            }

            #endregion

            #region Check-Content-Bytes

            if (contentBytes == null || contentBytes.Length < 1)
            {
                Log("*** MessageRead " + client.IpPort + " no content read");
                return null;
            }

            if (contentBytes.Length != contentLength)
            {
                Log("*** MessageRead " + client.IpPort + " content length " + contentBytes.Length + " bytes does not match header value " + contentLength + ", discarding");
                return null;
            }

            #endregion

            return contentBytes;
        }

        private async Task<byte[]> MessageReadAsync(ClientMetadata client)
        {
            /*
             *
             * Do not catch exceptions, let them get caught by the data reader
             * to destroy the connection
             *
             */

            #region Variables

            int bytesRead = 0;
            int sleepInterval = 25;
            int maxTimeout = 500;
            int currentTimeout = 0;
            bool timeout = false;

            byte[] headerBytes;
            string header = "";
            long contentLength;
            byte[] contentBytes;

            if (!client.Ssl.CanRead) return null;

            #endregion

            #region Read-Header

            using (MemoryStream headerMs = new MemoryStream())
            {
                #region Read-Header-Bytes

                byte[] headerBuffer = new byte[1];
                timeout = false;
                currentTimeout = 0;
                int read = 0;

                while ((read = await client.Ssl.ReadAsync(headerBuffer, 0, headerBuffer.Length)) > 0)
                {
                    if (read > 0)
                    {
                        await headerMs.WriteAsync(headerBuffer, 0, read);
                        bytesRead += read;

                        // reset timeout since there was a successful read
                        currentTimeout = 0;
                    }
                    else
                    {
                        #region Check-for-Timeout

                        if (currentTimeout >= maxTimeout)
                        {
                            timeout = true;
                            break;
                        }
                        else
                        {
                            currentTimeout += sleepInterval;
                            await Task.Delay(sleepInterval);
                        }

                        if (timeout) break;

                        #endregion
                    }

                    if (bytesRead > 1)
                    {
                        // check if end of headers reached
                        if ((int)headerBuffer[0] == 58) break;
                    }
                    else
                    {
                        #region Check-for-Timeout

                        if (currentTimeout >= maxTimeout)
                        {
                            timeout = true;
                            break;
                        }
                        else
                        {
                            currentTimeout += sleepInterval;
                            await Task.Delay(sleepInterval);
                        }

                        if (timeout) break;

                        #endregion
                    }
                }

                if (timeout)
                {
                    Log("*** MessageReadAsync timeout " + currentTimeout + "ms/" + maxTimeout + "ms exceeded while reading header after reading " + bytesRead + " bytes");
                    return null;
                }

                headerBytes = headerMs.ToArray();
                if (headerBytes == null || headerBytes.Length < 1)
                {
                    return null;
                }

                #endregion

                #region Process-Header

                header = Encoding.UTF8.GetString(headerBytes);
                header = header.Replace(":", "");

                if (!Int64.TryParse(header, out contentLength))
                {
                    Log("*** MessageReadAsync malformed message from " + client.IpPort + " (message header not an integer)");
                    return null;
                }

                #endregion
            }

            #endregion

            #region Read-Data

            using (MemoryStream dataMs = new MemoryStream())
            {
                long bytesRemaining = contentLength;
                timeout = false;
                currentTimeout = 0;

                int read = 0;
                byte[] buffer;
                long bufferSize = 2048;
                if (bufferSize > bytesRemaining) bufferSize = bytesRemaining;
                buffer = new byte[bufferSize];

                while ((read = await client.Ssl.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (read > 0)
                    {
                        dataMs.Write(buffer, 0, read);
                        bytesRead = bytesRead + read;
                        bytesRemaining = bytesRemaining - read;

                        // reset timeout
                        currentTimeout = 0;

                        // reduce buffer size if number of bytes remaining is
                        // less than the pre-defined buffer size of 2KB
                        if (bytesRemaining < bufferSize) bufferSize = bytesRemaining;
                        buffer = new byte[bufferSize];

                        // check if read fully
                        if (bytesRemaining == 0) break;
                        if (bytesRead == contentLength) break;
                    }
                    else
                    {
                        #region Check-for-Timeout

                        if (currentTimeout >= maxTimeout)
                        {
                            timeout = true;
                            break;
                        }
                        else
                        {
                            currentTimeout += sleepInterval;
                            await Task.Delay(sleepInterval);
                        }

                        if (timeout) break;

                        #endregion
                    }
                }

                if (timeout)
                {
                    Log("*** MessageReadAsync timeout " + currentTimeout + "ms/" + maxTimeout + "ms exceeded while reading content after reading " + bytesRead + " bytes");
                    return null;
                }

                contentBytes = dataMs.ToArray();
            }

            #endregion

            #region Check-Content-Bytes

            if (contentBytes == null || contentBytes.Length < 1)
            {
                Log("*** MessageReadAsync " + client.IpPort + " no content read");
                return null;
            }

            if (contentBytes.Length != contentLength)
            {
                Log("*** MessageReadAsync " + client.IpPort + " content length " + contentBytes.Length + " bytes does not match header value " + contentLength + ", discarding");
                return null;
            }

            #endregion

            return contentBytes;
        }

        private bool MessageWrite(ClientMetadata client, byte[] data)
        {
            try
            {
                #region Format-Message

                string header = "";
                byte[] headerBytes;
                byte[] message;

                if (data == null || data.Length < 1) header += "0:";
                else header += data.Length + ":";

                headerBytes = Encoding.UTF8.GetBytes(header);
                int messageLen = headerBytes.Length;
                if (data != null && data.Length > 0) messageLen += data.Length;

                message = new byte[messageLen];
                Buffer.BlockCopy(headerBytes, 0, message, 0, headerBytes.Length);

                if (data != null && data.Length > 0) Buffer.BlockCopy(data, 0, message, headerBytes.Length, data.Length);

                #endregion

                #region Send-Message

                client.Ssl.Write(message, 0, message.Length);
                client.Ssl.Flush();
                return true;

                #endregion
            }
            catch (Exception)
            {
                Log("*** MessageWrite " + client.IpPort + " disconnected due to exception");
                return false;
            }
        }

        private async Task<bool> MessageWriteAsync(ClientMetadata client, byte[] data)
        {
            try
            {
                #region Format-Message

                string header = "";
                byte[] headerBytes;
                byte[] message;

                if (data == null || data.Length < 1) header += "0:";
                else header += data.Length + ":";

                headerBytes = Encoding.UTF8.GetBytes(header);
                int messageLen = headerBytes.Length;
                if (data != null && data.Length > 0) messageLen += data.Length;

                message = new byte[messageLen];
                Buffer.BlockCopy(headerBytes, 0, message, 0, headerBytes.Length);

                if (data != null && data.Length > 0) Buffer.BlockCopy(data, 0, message, headerBytes.Length, data.Length);

                #endregion

                #region Send-Message-Async

                await client.Ssl.WriteAsync(message, 0, message.Length);
                await client.Ssl.FlushAsync();
                return true;

                #endregion
            }
            catch (Exception)
            {
                Log("*** MessageWriteAsync " + client.IpPort + " disconnected due to exception");
                return false;
            }
        }

        #endregion
    }
}
