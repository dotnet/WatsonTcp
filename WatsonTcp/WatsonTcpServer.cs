using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WatsonTcp
{
    /// <summary>
    /// Watson TCP server.
    /// </summary>
    public class WatsonTcpServer : IDisposable
    {
        #region Public-Members

        #endregion

        #region Private-Members
         
        private bool _Disposed = false;

        private bool _Debug;
        private string _ListenerIp;
        private int _ListenerPort;
        private IPAddress _ListenerIpAddress;
        private TcpListener _Listener;
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
        /// Initialize the Watson TCP server.
        /// </summary>
        /// <param name="listenerIp">The IP address on which the server should listen, nullable.</param>
        /// <param name="listenerPort">The TCP port on which the server should listen.</param>
        /// <param name="clientConnected">Function to be called when a client connects.</param>
        /// <param name="clientDisconnected">Function to be called when a client disconnects.</param>
        /// <param name="messageReceived">Function to be called when a message is received.</param>
        /// <param name="debug">Enable or debug logging messages.</param>
        public WatsonTcpServer(
            string listenerIp,
            int listenerPort,
            Func<string, bool> clientConnected,
            Func<string, bool> clientDisconnected,
            Func<string, byte[], bool> messageReceived,
            bool debug)
            : this(listenerIp, listenerPort, null, clientConnected, clientDisconnected, messageReceived, debug)
        {
        }

        /// <summary>
        /// Initialize the Watson TCP server.
        /// </summary>
        /// <param name="listenerIp">The IP address on which the server should listen, nullable.</param>
        /// <param name="listenerPort">The TCP port on which the server should listen.</param>
        /// <param name="permittedIps">List of IP address strings that are allowed to connect (null to permit all).</param>
        /// <param name="clientConnected">Function to be called when a client connects.</param>
        /// <param name="clientDisconnected">Function to be called when a client disconnects.</param>
        /// <param name="messageReceived">Function to be called when a message is received.</param>
        /// <param name="debug">Enable or debug logging messages.</param>
        public WatsonTcpServer(
            string listenerIp,
            int listenerPort,
            IEnumerable<string> permittedIps,
            Func<string, bool> clientConnected,
            Func<string, bool> clientDisconnected,
            Func<string, byte[], bool> messageReceived,
            bool debug)
        {
            if (listenerPort < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(listenerPort));
            }

            _ClientConnected = clientConnected;
            _ClientDisconnected = clientDisconnected;
            _MessageReceived = messageReceived ?? throw new ArgumentNullException(nameof(_MessageReceived));

            _Debug = debug;

            if (permittedIps != null && permittedIps.Count() > 0)
            {
                _PermittedIps = new List<string>(permittedIps);
            }

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

            Log("WatsonTcpServer starting on " + _ListenerIp + ":" + _ListenerPort);

            _Listener = new TcpListener(_ListenerIpAddress, _ListenerPort);
            _TokenSource = new CancellationTokenSource();
            _Token = _TokenSource.Token;
            _ActiveClients = 0;
            _Clients = new ConcurrentDictionary<string, ClientMetadata>();

            Task.Run(() => AcceptConnections(), _Token);
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
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Log("*** SendAsync unable to find client " + ipPort);
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
        public void DisconnectClient(string ipPort)
        {
            if (!_Clients.TryGetValue(ipPort, out ClientMetadata client))
            {
                Log("*** DisconnectClient unable to find client " + ipPort);
            }
            else
            {
                client.Dispose();
            }
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
                _TokenSource.Cancel();
                _TokenSource.Dispose();

                if (_Listener != null && _Listener.Server != null)
                {
                    _Listener.Server.Close();
                    _Listener.Server.Dispose();
                }

                if (_Clients != null && _Clients.Count > 0)
                {
                    foreach (KeyValuePair<string, ClientMetadata> currMetadata in _Clients)
                    {
                        currMetadata.Value.Dispose();
                    }
                }
            }

            _Disposed = true;
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
            if (data == null || data.Length < 1)
            {
                return "(null)";
            }

            return BitConverter.ToString(data).Replace("-", "");
        }

        private async Task AcceptConnections()
        {
            _Listener.Start();
            while (!_Token.IsCancellationRequested)
            {
                string clientIpPort = String.Empty;

                try
                {
                    #region Accept-Connection

                    TcpClient tcpClient = await _Listener.AcceptTcpClientAsync();
                    tcpClient.LingerState.Enabled = false;

                    #endregion

                    #region Get-Tuple-and-Check-IP

                    string clientIp = ((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address.ToString();

                    if (_PermittedIps != null && _PermittedIps.Count > 0)
                    {
                        if (!_PermittedIps.Contains(clientIp))
                        {
                            Log("*** AcceptConnections rejecting connection from " + clientIp + " (not permitted)");
                            tcpClient.Close();
                            continue;
                        }
                    }

                    #endregion

                    ClientMetadata client = new ClientMetadata(tcpClient);
                    clientIpPort = client.IpPort;

                    Log("*** AcceptConnections accepted connection from " + client.IpPort);

                    Task unawaited = Task.Run(() => {
                        FinalizeConnection(client);
                    }, _Token);
                }
                catch (ObjectDisposedException ex)
                {
                    // Listener stopped ? if so, clientIpPort will be empty
                    Log("*** AcceptConnections ObjectDisposedException from " + clientIpPort + Environment.NewLine + ex.ToString());
                }
                catch (SocketException ex)
                {
                    switch (ex.Message)
                    {
                        case "An existing connection was forcibly closed by the remote host":
                            Log("*** AcceptConnections SocketException " + clientIpPort + " closed the connection.");
                            break;
                        default:
                            Log("*** AcceptConnections SocketException from " + clientIpPort + Environment.NewLine + ex.ToString());
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log("*** AcceptConnections Exception from " + clientIpPort + Environment.NewLine + ex.ToString());
                }
            }
        }

        private void FinalizeConnection(ClientMetadata client)
        {
            #region Add-to-Client-List

            if (!AddClient(client))
            {
                Log("*** FinalizeConnection unable to add client " + client.IpPort);
                client.Dispose();
                return;
            }

            // Do not decrement in this block, decrement is done by the connection reader
            int activeCount = Interlocked.Increment(ref _ActiveClients);

            #endregion

            #region Start-Data-Receiver

            Log("*** FinalizeConnection starting data receiver for " + client.IpPort + " (now " + activeCount + " clients)");
            if (_ClientConnected != null)
            {
                Task.Run(() => _ClientConnected(client.IpPort));
            }

            Task.Run(async () => await DataReceiver(client));

            #endregion
        }

        private bool IsConnected(ClientMetadata client)
        {
            if (client.TcpClient.Connected)
            {
                if ((client.TcpClient.Client.Poll(0, SelectMode.SelectWrite)) && (!client.TcpClient.Client.Poll(0, SelectMode.SelectError)))
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
            else
            {
                return false;
            }
        }

        private async Task DataReceiver(ClientMetadata client)
        {
            try
            {
                #region Wait-for-Data

                while (true)
                {
                    try
                    {
                        if (!IsConnected(client))
                        {
                            break;
                        }

                        byte[] data = await MessageReadAsync(client);
                        if (data == null)
                        {
                            // no message available
                            await Task.Delay(30);
                            continue;
                        }

                        if (_MessageReceived != null)
                        {
                            Task<bool> unawaited = Task.Run(() => _MessageReceived(client.IpPort, data));
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
                int activeCount = Interlocked.Decrement(ref _ActiveClients);
                RemoveClient(client);
                if (_ClientDisconnected != null)
                {
                    Task<bool> unawaited = Task.Run(() => _ClientDisconnected(client.IpPort));
                }
                Log("*** DataReceiver client " + client.IpPort + " disconnected (now " + activeCount + " clients active)");

                client.Dispose();
            }
        }

        private bool AddClient(ClientMetadata client)
        {
            if (!_Clients.TryRemove(client.IpPort, out ClientMetadata removedClient))
            {
                // do nothing, it probably did not exist anyway
            }

            _Clients.TryAdd(client.IpPort, client);
            Log("*** AddClient added client " + client.IpPort);
            return true;
        }

        private bool RemoveClient(ClientMetadata client)
        {
            if (!_Clients.TryRemove(client.IpPort, out ClientMetadata removedClient))
            {
                Log("*** RemoveClient unable to remove client " + client.IpPort);
                return false;
            }
            else
            {
                Log("*** RemoveClient removed client " + client.IpPort);
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

            if (!client.NetworkStream.CanRead)
            {
                return null;
            }

            if (!client.NetworkStream.DataAvailable)
            {
                return null;
            }

            #endregion

            #region Read-Header

            using (MemoryStream headerMs = new MemoryStream())
            {
                #region Read-Header-Bytes

                byte[] headerBuffer = new byte[1];
                timeout = false;
                currentTimeout = 0;
                int read = 0;

                while ((read = client.NetworkStream.ReadAsync(headerBuffer, 0, headerBuffer.Length).Result) > 0)
                {
                    if (read > 0)
                    {
                        headerMs.Write(headerBuffer, 0, read);
                        bytesRead += read;
                        currentTimeout = 0;

                        if (bytesRead > 1)
                        {
                            // check if end of headers reached
                            if (headerBuffer[0] == 58)
                            {
                                break;
                            }
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
                if (bufferSize > bytesRemaining)
                {
                    bufferSize = bytesRemaining;
                }

                buffer = new byte[bufferSize];

                while ((read = client.NetworkStream.ReadAsync(buffer, 0, buffer.Length).Result) > 0)
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
                        if (bytesRemaining == 0)
                        {
                            break;
                        }

                        if (bytesRead == contentLength)
                        {
                            break;
                        }
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

            if (!client.NetworkStream.CanRead)
            {
                return null;
            }

            if (!client.NetworkStream.DataAvailable)
            {
                return null;
            }

            #endregion

            #region Read-Header

            using (MemoryStream headerMs = new MemoryStream())
            {
                #region Read-Header-Bytes

                byte[] headerBuffer = new byte[1];
                timeout = false;
                currentTimeout = 0;
                int read = 0;

                while ((read = await client.NetworkStream.ReadAsync(headerBuffer, 0, headerBuffer.Length)) > 0)
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

                        if (timeout)
                        {
                            break;
                        }

                        #endregion
                    }

                    if (bytesRead > 1)
                    {
                        // check if end of headers reached
                        if (headerBuffer[0] == 58)
                        {
                            break;
                        }
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

                        if (timeout)
                        {
                            break;
                        }

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
                if (bufferSize > bytesRemaining)
                {
                    bufferSize = bytesRemaining;
                }

                buffer = new byte[bufferSize];

                while ((read = await client.NetworkStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
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
                        if (bytesRemaining < bufferSize)
                        {
                            bufferSize = bytesRemaining;
                        }

                        buffer = new byte[bufferSize];

                        // check if read fully
                        if (bytesRemaining == 0)
                        {
                            break;
                        }

                        if (bytesRead == contentLength)
                        {
                            break;
                        }
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

                        if (timeout)
                        {
                            break;
                        }

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

                if (data == null || data.Length < 1)
                {
                    header += "0:";
                }
                else
                {
                    header += data.Length + ":";
                }

                headerBytes = Encoding.UTF8.GetBytes(header);
                int messageLen = headerBytes.Length;
                if (data != null && data.Length > 0)
                {
                    messageLen += data.Length;
                }

                message = new byte[messageLen];
                Buffer.BlockCopy(headerBytes, 0, message, 0, headerBytes.Length);

                if (data != null && data.Length > 0)
                {
                    Buffer.BlockCopy(data, 0, message, headerBytes.Length, data.Length);
                }

                #endregion

                #region Send-Message

                client.NetworkStream.Write(message, 0, message.Length);
                client.NetworkStream.Flush();
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

                if (data == null || data.Length < 1)
                {
                    header += "0:";
                }
                else
                {
                    header += data.Length + ":";
                }

                headerBytes = Encoding.UTF8.GetBytes(header);
                int messageLen = headerBytes.Length;
                if (data != null && data.Length > 0)
                {
                    messageLen += data.Length;
                }

                message = new byte[messageLen];
                Buffer.BlockCopy(headerBytes, 0, message, 0, headerBytes.Length);

                if (data != null && data.Length > 0)
                {
                    Buffer.BlockCopy(data, 0, message, headerBytes.Length, data.Length);
                }

                #endregion

                #region Send-Message-Async

                await client.NetworkStream.WriteAsync(message, 0, message.Length);
                await client.NetworkStream.FlushAsync();
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
