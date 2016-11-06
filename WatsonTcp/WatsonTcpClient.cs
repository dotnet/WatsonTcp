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
    public class WatsonTcpClient
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

        private CancellationTokenSource DataReceiverTokenSource;
        private CancellationToken DataReceiverToken;

        #endregion

        #region Constructors-and-Factories

        public WatsonTcpClient(
            string serverIp, 
            int serverPort, 
            bool debug, 
            Func<byte[], bool> messageReceived,
            Func<bool> serverConnected,
            Func<bool> serverDisconnected)
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
            }
            catch (Exception e)
            {
                throw e;
            }
            finally
            {
                wh.Close();
            }

            if (ServerConnected != null) Task.Run(() => ServerConnected());

            DataReceiverTokenSource = new CancellationTokenSource();
            DataReceiverToken = DataReceiverTokenSource.Token;
            Task.Run(() => DataReceiver(), DataReceiverToken);
        }

        #endregion

        #region Public-Methods

        public bool IsConnected()
        {
            return Connected;
        }
        
        public bool Send(byte[] data)
        {
            return MessageWrite(data);
        }

        #endregion

        #region Private-Methods

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

        private bool IsPeerConnected(TcpClient client)
        {
            // see http://stackoverflow.com/questions/6993295/how-to-determine-if-the-tcp-is-connected-or-not

            bool success = false;

            try
            {
                #region Check-if-Client-Connected

                success = false;

                if (client != null
                    && client.Client != null
                    && client.Client.Connected)
                {
                    if (client.Client.Poll(0, SelectMode.SelectRead))
                    {
                        byte[] buff = new byte[1];
                        if (client.Client.Receive(buff, SocketFlags.Peek) == 0) success = false;
                        else success = true;
                    }

                    success = true;
                }
                else
                {
                    success = false;
                }

                return success;

                #endregion
            }
            catch
            {
                return false;
            }
        }

        private void DataReceiver()
        {
            bool disconnectDetected = false;

            try
            {
                #region Attach-to-Stream

                if (!Client.Connected)
                {
                    Log("*** DataReceiver server " + ServerIp + ":" + ServerPort + " is no longer connected");
                    return;
                }
                
                #endregion

                #region Wait-for-Data

                while (true)
                {
                    #region Check-if-Client-Connected-to-Server

                    if (Client == null)
                    {
                        Log("*** DataReceiver null TCP interface detected, disconnection or close assumed");
                        Connected = false;
                        disconnectDetected = true;
                        break;
                    }

                    if (!Client.Connected || !IsPeerConnected(Client))
                    {
                        Log("*** DataReceiver server " + ServerIp + ":" + ServerPort + " disconnected");
                        Connected = false;
                        disconnectDetected = true;
                        break;
                    }

                    #endregion

                    #region Read-Message-and-Handle

                    byte[] data = MessageRead();
                    if (data == null)
                    {
                        // Log("DataReceiver unable to read message from server " + ServerIp + ":" + ServerPort);
                        Thread.Sleep(30);
                        continue;
                    }

                    Task.Run(() => MessageReceived(data));
                    
                    #endregion
                }

                #endregion
            }
            catch (ObjectDisposedException)
            {
                Log("*** DataReceiver no longer connected (object disposed exception)");
            }
            catch (Exception EOuter)
            {
                Log("*** DataReceiver outer exception detected");
                LogException("DataReceiver", EOuter);
            }
            finally
            {
                if (disconnectDetected || !Connected)
                {
                    if (ServerDisconnected != null) Task.Run(() => ServerDisconnected());
                }
            }
        }

        private byte[] MessageRead()
        {
            string sourceIp = "";
            int sourcePort = 0;

            try
            {
                #region Check-for-Null-Values

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

                if (!IsPeerConnected(Client))
                {
                    Log("*** MessageRead " + sourceIp + ":" + sourcePort + " disconnected while attempting to read header");
                    return null;
                }

                if (!ClientStream.CanRead)
                {
                    return null;
                }

                if (!ClientStream.DataAvailable)
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
                                    Thread.Sleep(sleepInterval);
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
                                    Thread.Sleep(sleepInterval);
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
            catch (ObjectDisposedException ObjDispInner)
            {
                Log("*** MessageRead " + sourceIp + ":" + sourcePort + " disconnected (obj disposed exception): " + ObjDispInner.Message);
                return null;
            }
            catch (SocketException SockInner)
            {
                Log("*** MessageRead " + sourceIp + ":" + sourcePort + " disconnected (socket exception): " + SockInner.Message);
                return null;
            }
            catch (InvalidOperationException InvOpInner)
            {
                Log("*** MessageRead " + sourceIp + ":" + sourcePort + " disconnected (invalid operation exception): " + InvOpInner.Message);
                return null;
            }
            catch (AggregateException AEInner)
            {
                Log("*** MessageRead " + sourceIp + ":" + sourcePort + " disconnected (aggregate exception): " + AEInner.Message);
                return null;
            }
            catch (IOException IOInner)
            {
                Log("*** MessageRead " + sourceIp + ":" + sourcePort + " disconnected (IO exception): " + IOInner.Message);
                return null;
            }
            catch (Exception EInner)
            {
                Log("*** MessageRead " + sourceIp + ":" + sourcePort + " disconnected (general exception): " + EInner.Message);
                LogException("MessageRead " + sourceIp + ":" + sourcePort, EInner);
                return null;
            }
        }
        
        private bool MessageWrite(byte[] data)
        {
            try
            {
                #region Check-if-Connected

                if (!IsPeerConnected(Client))
                {
                    Log("MessageWrite server " + ServerIp + ":" + ServerPort + " not connected");
                    Connected = false;
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

                Client.GetStream().Write(message, 0, message.Length);
                Client.GetStream().Flush();
                return true;

                #endregion
            }
            catch (ObjectDisposedException ObjDispInner)
            {
                Log("*** MessageWrite " + SourceIp + ":" + SourcePort + " disconnected (obj disposed exception): " + ObjDispInner.Message);
                return false;
            }
            catch (SocketException SockInner)
            {
                Log("*** MessageWrite " + SourceIp + ":" + SourcePort + " disconnected (socket exception): " + SockInner.Message);
                return false;
            }
            catch (InvalidOperationException InvOpInner)
            {
                Log("*** MessageWrite " + SourceIp + ":" + SourcePort + " disconnected (invalid operation exception): " + InvOpInner.Message);
                return false;
            }
            catch (IOException IOInner)
            {
                Log("*** MessageWrite " + SourceIp + ":" + SourcePort + " disconnected (IO exception): " + IOInner.Message);
                return false;
            }
            catch (Exception e)
            {
                LogException("MessageWrite", e);
                return false;
            }
        }

        #endregion
    }
}
