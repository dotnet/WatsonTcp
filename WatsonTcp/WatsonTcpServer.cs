using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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

        private bool Debug;
        private string ListenerIp;
        private int ListenerPort;
        private IPAddress ListenerIpAddress;
        private TcpListener Listener;
        private int ActiveClients;
        private ConcurrentDictionary<string, TcpClient> Clients;
        private CancellationTokenSource TokenSource;
        private CancellationToken Token;
        private Func<string, bool> ClientConnected;
        private Func<string, bool> ClientDisconnected;
        private Func<string, byte[], bool> MessageReceived;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize the Watson TCP client.
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
        {
            if (listenerPort < 1) throw new ArgumentOutOfRangeException(nameof(listenerPort));
            if (messageReceived == null) throw new ArgumentNullException(nameof(MessageReceived));

            if (clientConnected == null) ClientConnected = null;
            else ClientConnected = clientConnected;

            if (clientDisconnected == null) ClientDisconnected = null;
            else ClientDisconnected = clientDisconnected;

            MessageReceived = messageReceived;
            Debug = debug;

            if (String.IsNullOrEmpty(listenerIp))
            {
                ListenerIpAddress = System.Net.IPAddress.Any;
                ListenerIp = ListenerIpAddress.ToString();
            }
            else
            {
                ListenerIpAddress = IPAddress.Parse(listenerIp);
                ListenerIp = listenerIp;
            }

            ListenerPort = listenerPort;
            
            Log("WatsonTcpServer starting on " + ListenerIp + ":" + ListenerPort);

            Listener = new TcpListener(ListenerIpAddress, ListenerPort);
            TokenSource = new CancellationTokenSource();
            Token = TokenSource.Token;
            ActiveClients = 0;
            Clients = new ConcurrentDictionary<string, TcpClient>();
            Task.Run(() => AcceptConnections(), Token);
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
            TcpClient client;
            if (!Clients.TryGetValue(ipPort, out client))
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
            TcpClient client;
            if (!Clients.TryGetValue(ipPort, out client))
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
            TcpClient client;
            return (Clients.TryGetValue(ipPort, out client));
        }

        /// <summary>
        /// List the IP:port of each connected client.
        /// </summary>
        /// <returns>A string list containing each client IP:port.</returns>
        public List<string> ListClients()
        {
            Dictionary<string, TcpClient> clients = Clients.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            List<string> ret = new List<string>();
            foreach (KeyValuePair<string, TcpClient> curr in clients)
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
                TokenSource.Cancel();
            }
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

        private async Task AcceptConnections()
        {
            Listener.Start();
            while (true)
            {
                Token.ThrowIfCancellationRequested();
                // Log("TCPAcceptConnections waiting for next connection");

                TcpClient client = await Listener.AcceptTcpClientAsync();
                client.LingerState.Enabled = false;

                var unawaited = Task.Run(() =>
                {
                    #region Get-Tuple

                    string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    int clientPort = ((IPEndPoint)client.Client.RemoteEndPoint).Port;
                    Log("AcceptConnections accepted connection from " + clientIp + ":" + clientPort);

                    #endregion

                    #region Increment-Counters

                    ActiveClients++;

                    //
                    //
                    // Do not decrement in this block, decrement is done by the connection reader
                    //
                    //

                    #endregion

                    #region Add-to-Client-List
                    
                    if (!AddClient(client))
                    {
                        Log("*** AcceptConnections unable to add client " + clientIp + ":" + clientPort);
                        client.Close();
                        return;
                    }

                    #endregion

                    #region Start-Data-Receiver

                    // TODO consider replacing with another token source or with Token
                    CancellationToken dataReceiverToken = default(CancellationToken);

                    Log("AcceptConnections starting data receiver for " + clientIp + ":" + clientPort + " (now " + ActiveClients + " clients)");
                    if (ClientConnected != null)
                    {
                        Task.Run(() => ClientConnected(clientIp + ":" + clientPort));
                    }

                    // TODO not sure if part or all of "Task.Run async () => await" can be omitted.
                    Task.Run(async () => await DataReceiver(client, dataReceiverToken), dataReceiverToken);

                    #endregion
                    
                }, Token);
            }
        }

        private bool IsConnected(TcpClient client)
        {
            if (client.Connected)
            {
                if ((client.Client.Poll(0, SelectMode.SelectWrite)) && (!client.Client.Poll(0, SelectMode.SelectError)))
                {
                    byte[] buffer = new byte[1];
                    if (client.Client.Receive(buffer, SocketFlags.Peek) == 0)
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

        private async Task DataReceiver(TcpClient client, CancellationToken? cancelToken=null)
        {
            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            int clientPort = ((IPEndPoint)client.Client.RemoteEndPoint).Port;

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

                        if (MessageReceived != null)
                        {
                            var unawaited = Task.Run(() => MessageReceived(clientIp + ":" + clientPort, data));
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
                ActiveClients--;
                RemoveClient(client);
                if (ClientDisconnected != null)
                {
                    var unawaited = Task.Run(() => ClientDisconnected(clientIp + ":" + clientPort));
                }
                Log("DataReceiver client " + clientIp + ":" + clientPort + " disconnected (now " + ActiveClients + " clients active)");
            }
        }

        private bool AddClient(TcpClient client)
        {
            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            int clientPort = ((IPEndPoint)client.Client.RemoteEndPoint).Port;

            TcpClient removedClient;
            if (!Clients.TryRemove(clientIp + ":" + clientPort, out removedClient))
            {
                // do nothing, it probably did not exist anyway
            }

            Clients.TryAdd(clientIp + ":" + clientPort, client);
            Log("AddClient added client " + clientIp + ":" + clientPort);
            return true;
        }

        private bool RemoveClient(TcpClient client)
        {
            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            int clientPort = ((IPEndPoint)client.Client.RemoteEndPoint).Port;

            TcpClient removedClient;
            if (!Clients.TryRemove(clientIp + ":" + clientPort, out removedClient))
            {
                Log("RemoveClient unable to remove client " + clientIp + ":" + clientPort);
                return false;
            }
            else
            {
                Log("RemoveClient removed client " + clientIp + ":" + clientPort);
                return true;
            }
        }

        private byte[] MessageRead(TcpClient client)
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

            string sourceIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            int sourcePort = ((IPEndPoint)client.Client.RemoteEndPoint).Port;
            NetworkStream ClientStream = client.GetStream();

            byte[] headerBytes;
            string header = "";
            long contentLength;
            byte[] contentBytes;

            if (!ClientStream.CanRead) return null;
            if (!ClientStream.DataAvailable) return null;

            #endregion

            #region Read-Header

            using (MemoryStream headerMs = new MemoryStream())
            {
                #region Read-Header-Bytes

                byte[] headerBuffer = new byte[1];
                timeout = false;
                currentTimeout = 0;
                int read = 0;

                while ((read = ClientStream.ReadAsync(headerBuffer, 0, headerBuffer.Length).Result) > 0)
                {
                    if (read > 0)
                    {
                        headerMs.Write(headerBuffer, 0, read);
                        bytesRead += read;

                        //
                        // reset timeout since there was a successful read
                        //
                        currentTimeout = 0;
                    }

                    if (bytesRead > 1)
                    {
                        //
                        // check if end of headers reached
                        //
                        if ((int)headerBuffer[0] == 58)
                        {
                            // Log("MessageRead reached end of header after " + BytesRead + " bytes");
                            break;
                        }
                    }

                    if (!ClientStream.DataAvailable)
                    {
                        while (true)
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

                        if (timeout) break;
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
                    // Log("*** MessageRead " + sourceIp + ":" + sourcePort + " no byte data read from peer");
                    return null;
                }

                #endregion

                #region Process-Header

                header = Encoding.UTF8.GetString(headerBytes);
                header = header.Replace(":", "");

                if (!Int64.TryParse(header, out contentLength))
                {
                    Log("*** MessageRead malformed message from " + sourceIp + ":" + sourcePort + " (message header not an integer)");
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

                while ((read = ClientStream.ReadAsync(buffer, 0, buffer.Length).Result) > 0)
                {
                    if (read > 0)
                    {
                        dataMs.Write(buffer, 0, read);
                        bytesRead = bytesRead + read;
                        bytesRemaining = bytesRemaining - read;
                    }

                    //
                    // reduce buffer size if number of bytes remaining is
                    // less than the pre-defined buffer size of 2KB
                    //
                    // Console.WriteLine("Bytes remaining " + bytesRemaining + ", buffer size " + bufferSize);
                    if (bytesRemaining < bufferSize)
                    {
                        bufferSize = bytesRemaining;
                        // Console.WriteLine("Adjusting buffer size to " + bytesRemaining);
                    }

                    buffer = new byte[bufferSize];

                    //
                    // check if read fully
                    //
                    if (bytesRemaining == 0) break;
                    if (bytesRead == contentLength) break;

                    if (!ClientStream.DataAvailable)
                    {
                        while (true)
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

                        if (timeout) break;
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
                Log("*** MessageRead " + sourceIp + ":" + sourcePort + " no content read");
                return null;
            }

            if (contentBytes.Length != contentLength)
            {
                Log("*** MessageRead " + sourceIp + ":" + sourcePort + " content length " + contentBytes.Length + " bytes does not match header value " + contentLength + ", discarding");
                return null;
            }

            #endregion

            return contentBytes;
        }

        private async Task<byte[]> MessageReadAsync(TcpClient client)
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

            string sourceIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            int sourcePort = ((IPEndPoint)client.Client.RemoteEndPoint).Port;
            NetworkStream ClientStream = client.GetStream();

            byte[] headerBytes;
            string header = "";
            long contentLength;
            byte[] contentBytes;

            if (!ClientStream.CanRead) return null;
            if (!ClientStream.DataAvailable) return null;

            #endregion

            #region Read-Header

            using (MemoryStream headerMs = new MemoryStream())
            {
                #region Read-Header-Bytes

                byte[] headerBuffer = new byte[1];
                timeout = false;
                currentTimeout = 0;
                int read = 0;

                while ((read = await ClientStream.ReadAsync(headerBuffer, 0, headerBuffer.Length)) > 0)
                {
                    if (read > 0)
                    {
                        await headerMs.WriteAsync(headerBuffer, 0, read);
                        bytesRead += read;

                        //
                        // reset timeout since there was a successful read
                        //
                        currentTimeout = 0;
                    }
                        
                    if (bytesRead > 1)
                    {
                        //
                        // check if end of headers reached
                        //
                        if ((int)headerBuffer[0] == 58)
                        {
                            // Log("MessageRead reached end of header after " + BytesRead + " bytes");
                            break;
                        }
                    }

                    if (!ClientStream.DataAvailable)
                    {
                        while (true)
                        {
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
                        }

                        if (timeout) break;
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
                    // Log("*** MessageRead " + sourceIp + ":" + sourcePort + " no byte data read from peer");
                    return null;
                }

                #endregion

                #region Process-Header

                header = Encoding.UTF8.GetString(headerBytes);
                header = header.Replace(":", "");

                if (!Int64.TryParse(header, out contentLength))
                {
                    Log("*** MessageRead malformed message from " + sourceIp + ":" + sourcePort + " (message header not an integer)");
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

                while ((read = await ClientStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (read > 0)
                    {
                        dataMs.Write(buffer, 0, read);
                        bytesRead = bytesRead + read;
                        bytesRemaining = bytesRemaining - read;
                    }

                    //
                    // reduce buffer size if number of bytes remaining is
                    // less than the pre-defined buffer size of 2KB
                    //
                    // Console.WriteLine("Bytes remaining " + bytesRemaining + ", buffer size " + bufferSize);
                    if (bytesRemaining < bufferSize)
                    {
                        bufferSize = bytesRemaining;
                        // Console.WriteLine("Adjusting buffer size to " + bytesRemaining);
                    }

                    buffer = new byte[bufferSize];

                    //
                    // check if read fully
                    //
                    if (bytesRemaining == 0) break;
                    if (bytesRead == contentLength) break;

                    if (!ClientStream.DataAvailable)
                    {
                        while (true)
                        {
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
                        }

                        if (timeout) break;
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
                Log("*** MessageRead " + sourceIp + ":" + sourcePort + " no content read");
                return null;
            }

            if (contentBytes.Length != contentLength)
            {
                Log("*** MessageRead " + sourceIp + ":" + sourcePort + " content length " + contentBytes.Length + " bytes does not match header value " + contentLength + ", discarding");
                return null;
            }

            #endregion

            return contentBytes;
        }

        private bool MessageWrite(TcpClient client, byte[] data)
        {
            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            int clientPort = ((IPEndPoint)client.Client.RemoteEndPoint).Port;

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

                client.GetStream().Write(message, 0, message.Length);
                client.GetStream().Flush();
                return true;

                #endregion
            }
            catch (Exception)
            {
                Log("*** MessageWrite " + clientIp + ":" + clientPort + " disconnected due to exception");
                return false;
            }
        }

        private async Task<bool> MessageWriteAsync(TcpClient client, byte[] data)
        {
            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            int clientPort = ((IPEndPoint)client.Client.RemoteEndPoint).Port;

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
                var clientStream = client.GetStream();
                await clientStream.WriteAsync(message, 0, message.Length);
                await clientStream.FlushAsync();
                return true;

                #endregion
            }
            catch (Exception)
            {
                Log("*** MessageWrite " + clientIp + ":" + clientPort + " disconnected due to exception");
                return false;
            }
        }

        #endregion
    }
}
