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
    /// Watson TCP client.
    /// </summary>
    public class WatsonTcpClient : IDisposable
    {
        #region Public-Members
        
        #endregion

        #region Private-Members

        private string SourceIp;
        private int SourcePort;
        private string ServerIp;
        private int ServerPort;
        private bool Debug;
        private TcpClient Client;
        private bool Connected;
        private Func<byte[], bool> MessageReceived;
        private Func<bool> ServerConnected;
        private Func<bool> ServerDisconnected;

        private readonly SemaphoreSlim SendLock;
        private CancellationTokenSource DataReceiverTokenSource;
        private CancellationToken DataReceiverToken;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize the Watson TCP client.
        /// </summary>
        /// <param name="serverIp">The IP address or hostname of the server.</param>
        /// <param name="serverPort">The TCP port on which the server is listening.</param>
        /// <param name="serverConnected">Function to be called when the server connects.</param>
        /// <param name="serverDisconnected">Function to be called when the connection is severed.</param>
        /// <param name="messageReceived">Function to be called when a message is received.</param>
        /// <param name="debug">Enable or debug logging messages.</param>
        public WatsonTcpClient(
            string serverIp, 
            int serverPort,
            Func<bool> serverConnected,
            Func<bool> serverDisconnected,
            Func<byte[], bool> messageReceived,
            bool debug)
        {
            if (String.IsNullOrEmpty(serverIp)) throw new ArgumentNullException(nameof(serverIp));
            if (serverPort < 1) throw new ArgumentOutOfRangeException(nameof(serverPort));
            if (messageReceived == null) throw new ArgumentNullException(nameof(messageReceived));

            if (serverConnected != null) ServerConnected = serverConnected;
            else ServerConnected = null;

            if (serverDisconnected != null) ServerDisconnected = serverDisconnected;
            else ServerDisconnected = null;

            ServerIp = serverIp;
            ServerPort = serverPort;
            Debug = debug;
            MessageReceived = messageReceived;
            SendLock = new SemaphoreSlim(1);

            Client = new TcpClient();
            IAsyncResult ar = Client.BeginConnect(ServerIp, ServerPort, null, null);
            WaitHandle wh = ar.AsyncWaitHandle;

            try
            {
                if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5), false))
                {
                    Client.Close();
                    throw new TimeoutException("Timeout connecting to " + ServerIp + ":" + ServerPort);
                }

                Client.EndConnect(ar);

                SourceIp = ((IPEndPoint)Client.Client.LocalEndPoint).Address.ToString();
                SourcePort = ((IPEndPoint)Client.Client.LocalEndPoint).Port;
                Connected = true;
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                wh.Close();
            }

            if (ServerConnected != null) Task.Run(() => ServerConnected());

            DataReceiverTokenSource = new CancellationTokenSource();
            DataReceiverToken = DataReceiverTokenSource.Token;
            Task.Run(async () => await DataReceiver(DataReceiverToken), DataReceiverToken);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Tear down the client and dispose of background workers.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
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
        /// Send data to the server asynchronously
        /// </summary>
        /// <param name="data">Byte array containing data.</param>
        /// <returns>Task with Boolean indicating if the message was sent successfully.</returns>
        public async Task<bool> SendAsync(byte[] data)
        {
            return await MessageWriteAsync(data);
        }


        /// <summary>
        /// Determine whether or not the client is connected to the server.
        /// </summary>
        /// <returns>Boolean indicating if the client is connected to the server.</returns>
        public bool IsConnected()
        {
            return Connected;
        }

        #endregion

        #region Private-Methods

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Client != null)
                {
                    if (Client.Connected)
                    {
                        NetworkStream ns = Client.GetStream();
                        if (ns != null)
                        {
                            ns.Close();
                        }
                    }

                    Client.Close();
                }

                DataReceiverTokenSource.Cancel();
                Connected = false;
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
        
        private async Task DataReceiver(CancellationToken? cancelToken=null)
        {
            try
            {
                #region Attach-to-Stream

                if (!Client.Connected)
                {
                    Log("*** DataReceiver server " + ServerIp + ":" + ServerPort + " disconnected");
                    return;
                }
                
                #endregion

                #region Wait-for-Data

                while (true)
                {
                    cancelToken?.ThrowIfCancellationRequested();

                    #region Check-if-Client-Connected-to-Server

                    if (Client == null)
                    {
                        Log("*** DataReceiver null TCP interface detected, disconnection or close assumed");
                        break;
                    }

                    if (!Client.Connected)
                    {
                        Log("*** DataReceiver server " + ServerIp + ":" + ServerPort + " disconnected");
                        break;
                    }

                    #endregion

                    #region Read-Message-and-Handle

                    byte[] data = await MessageReadAsync();
                    if (data == null)
                    {
                        // Log("DataReceiver unable to read message from server " + ServerIp + ":" + ServerPort);
                        await Task.Delay(30);
                        continue;
                    }

                    var unawaited = Task.Run(() => MessageReceived(data));
                    
                    #endregion
                }

                #endregion
            }
            catch (OperationCanceledException)
            {
                throw; //normal cancellation
            }
            catch (Exception)
            {
                Log("*** DataReceiver server " + ServerIp + ":" + ServerPort + " disconnected");
            }
            finally
            {
                Connected = false;
            }
        }

        private byte[] MessageRead()
        {
            string sourceIp = "";
            int sourcePort = 0;
            
            try
            {
                #region Check-for-Null-Values

                if (Client == null)
                {
                    Log("*** MessageRead null client supplied");
                    return null;
                }

                if (!Client.Connected)
                {
                    Log("*** MessageRead supplied client is not connected");
                    return null;
                }

                #endregion

                #region Variables

                int bytesRead = 0;
                int sleepInterval = 25;
                int maxTimeout = 500;
                int currentTimeout = 0;
                bool timeout = false;

                sourceIp = ((IPEndPoint)Client.Client.RemoteEndPoint).Address.ToString();
                sourcePort = ((IPEndPoint)Client.Client.RemoteEndPoint).Port;
                NetworkStream ClientStream = null;

                try
                {
                    ClientStream = Client.GetStream();
                }
                catch (Exception e)
                {
                    Log("*** MessageRead disconnected while attaching to stream for " + sourceIp + ":" + sourcePort + ": " + e.Message);
                    return null;
                }

                byte[] headerBytes;
                string header = "";
                long contentLength;
                byte[] contentBytes;

                #endregion

                #region Read-Header
                
                if (!ClientStream.CanRead && !ClientStream.DataAvailable)
                {
                    return null;
                }
                
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
                    Log("*** MessageRead " + sourceIp + ":" + sourcePort + " content length " + contentBytes.Length + " bytes does not match header value of " + contentLength);
                    return null;
                }

                #endregion

                return contentBytes;
            }
            catch (Exception)
            {
                Log("*** MessageRead " + sourceIp + ":" + sourcePort + " disconnected");
                return null;
            }
        }

        private async Task<byte[]> MessageReadAsync()
        {
            string sourceIp = "";
            int sourcePort = 0;

            try
            {
                #region Check-for-Null-Values

                if (Client == null)
                {
                    Log("*** MessageRead null client supplied");
                    return null;
                }

                if (!Client.Connected)
                {
                    Log("*** MessageRead supplied client is not connected");
                    return null;
                }

                #endregion

                #region Variables

                int bytesRead = 0;
                int sleepInterval = 25;
                int maxTimeout = 500;
                int currentTimeout = 0;
                bool timeout = false;

                sourceIp = ((IPEndPoint)Client.Client.RemoteEndPoint).Address.ToString();
                sourcePort = ((IPEndPoint)Client.Client.RemoteEndPoint).Port;
                NetworkStream ClientStream = null;

                try
                {
                    ClientStream = Client.GetStream();
                }
                catch (Exception e)
                {
                    Log("*** MessageRead disconnected while attaching to stream for " + sourceIp + ":" + sourcePort + ": " + e.Message);
                    return null;
                }

                byte[] headerBytes;
                string header = "";
                long contentLength;
                byte[] contentBytes;

                #endregion

                #region Read-Header

                if (!ClientStream.CanRead && !ClientStream.DataAvailable)
                {
                    return null;
                }

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
                            await dataMs.WriteAsync(buffer, 0, read);
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
                    Log("*** MessageRead " + sourceIp + ":" + sourcePort + " content length " + contentBytes.Length + " bytes does not match header value of " + contentLength);
                    return null;
                }

                #endregion

                return contentBytes;
            }
            catch (Exception)
            {
                Log("*** MessageRead " + sourceIp + ":" + sourcePort + " disconnected");
                return null;
            }
        }
        
        private bool MessageWrite(byte[] data)
        {
            bool disconnectDetected = false;

            try
            {
                #region Check-if-Connected

                if (Client == null)
                {
                    Log("MessageWrite client is null");
                    disconnectDetected = true;
                    return false;
                }

                #endregion

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

                SendLock.Wait();
                try
                {
                    Client.GetStream().Write(message, 0, message.Length);
                    Client.GetStream().Flush();
                }
                finally
                {
                    SendLock.Release();
                }


                return true;

                #endregion
            }
            catch (ObjectDisposedException ObjDispInner)
            {
                Log("*** MessageWrite " + SourceIp + ":" + SourcePort + " disconnected (obj disposed exception): " + ObjDispInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (SocketException SockInner)
            {
                Log("*** MessageWrite " + SourceIp + ":" + SourcePort + " disconnected (socket exception): " + SockInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (InvalidOperationException InvOpInner)
            {
                Log("*** MessageWrite " + SourceIp + ":" + SourcePort + " disconnected (invalid operation exception): " + InvOpInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (IOException IOInner)
            {
                Log("*** MessageWrite " + SourceIp + ":" + SourcePort + " disconnected (IO exception): " + IOInner.Message);
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
                    if (ServerDisconnected != null) Task.Run(() => ServerDisconnected());
                }
            }
        }

        private async Task<bool> MessageWriteAsync(byte[] data)
        {
            bool disconnectDetected = false;

            try
            {
                #region Check-if-Connected

                if (Client == null)
                {
                    Log("MessageWrite client is null");
                    disconnectDetected = true;
                    return false;
                }

                #endregion

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

                await SendLock.WaitAsync();
                try
                {
                    await Client.GetStream().WriteAsync(message, 0, message.Length);
                    await Client.GetStream().FlushAsync();
                }
                finally
                {
                    SendLock.Release();
                }

                return true;

                #endregion
            }
            catch (ObjectDisposedException ObjDispInner)
            {
                Log("*** MessageWrite " + SourceIp + ":" + SourcePort + " disconnected (obj disposed exception): " + ObjDispInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (SocketException SockInner)
            {
                Log("*** MessageWrite " + SourceIp + ":" + SourcePort + " disconnected (socket exception): " + SockInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (InvalidOperationException InvOpInner)
            {
                Log("*** MessageWrite " + SourceIp + ":" + SourcePort + " disconnected (invalid operation exception): " + InvOpInner.Message);
                disconnectDetected = true;
                return false;
            }
            catch (IOException IOInner)
            {
                Log("*** MessageWrite " + SourceIp + ":" + SourcePort + " disconnected (IO exception): " + IOInner.Message);
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
                    if (ServerDisconnected != null)
                    {
                        var unawaited = Task.Run(() => ServerDisconnected());
                    }
                }
            }
        }

        #endregion
    }
}
