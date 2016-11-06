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
    public class WatsonTcpServer
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
                ListenerIpAddress = IPAddress.Parse(ListenerIp);
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

        public bool IsClientConnected(string ipPort)
        {
            TcpClient client;
            return (Clients.TryGetValue(ipPort, out client));
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

        private void AcceptConnections()
        {
            Listener.Start();
            while (!Token.IsCancellationRequested)
            {
                // Log("TCPAcceptConnections waiting for next connection");

                TcpClient client = Listener.AcceptTcpClientAsync().Result;
                client.LingerState.Enabled = false;

                Task.Run(() =>
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

                    CancellationTokenSource dataReceiverTokenSource = new CancellationTokenSource();
                    CancellationToken dataReceiverToken = dataReceiverTokenSource.Token;
                    Log("AcceptConnections starting data receiver for " + clientIp + ":" + clientPort + " (now " + ActiveClients + " clients)");
                    if (ClientConnected != null) Task.Run(() => ClientConnected(clientIp + ":" + clientPort));
                    Task.Run(() => DataReceiver(client), dataReceiverToken);

                    #endregion
                    
                }, Token);
            }
        }

        private bool IsPeerConnected(TcpClient client)
        {
            // see http://stackoverflow.com/questions/6993295/how-to-determine-if-the-tcp-is-connected-or-not

            bool success = false;
            string sourceIp = "";
            int sourcePort = 0;

            try
            {
                #region Check-if-Client-Connected

                success = false;
                sourceIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                sourcePort = ((IPEndPoint)client.Client.RemoteEndPoint).Port;

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

        private void DataReceiver(TcpClient client)
        {
            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            int clientPort = ((IPEndPoint)client.Client.RemoteEndPoint).Port;

            try
            {
                #region Wait-for-Data
                
                if (!client.Connected)
                {
                    Log("*** DataReceiver client " + clientIp + ":" + clientPort + " is no longer connected");
                    return;
                }
                
                while (true)
                {
                    #region Check-if-Client-Connected

                    if (!client.Connected || !IsPeerConnected(client))
                    {
                        Log("DataReceiver client " + clientIp + ":" + clientPort + " disconnected");
                        if (!RemoveClient(client))
                        {
                            Log("*** DataReceiver unable to remove client " + clientIp + ":" + clientPort);
                        }

                        if (ClientDisconnected != null) Task.Run(() => ClientDisconnected(clientIp + ":" + clientPort));
                        break;
                    }

                    #endregion

                    #region Retrieve-and-Process-Data

                    byte[] data = MessageRead(client);
                    if (data == null)
                    {
                        // no message available
                        Thread.Sleep(30);
                        continue;
                    }
                    
                    if (MessageReceived != null) Task.Run(() => MessageReceived(clientIp + ":" + clientPort, data));

                    #endregion
                }

                #endregion
            }
            catch (Exception EOuter)
            {
                if (client != null)
                {
                    LogException("DataReceiver (" + clientIp + ":" + clientPort + ")", EOuter);
                }
                else
                {
                    LogException("DataReceiver (null)", EOuter);
                }
            }
            finally
            {
                ActiveClients--;
                Log("DataReceiver closed data receiver for " + clientIp + ":" + clientPort + " (now " + ActiveClients + " clients active)");
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
            string sourceIp = "";
            int sourcePort = 0;

            try
            {
                #region Check-for-Null-Values
                
                if (!client.Connected)
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

                sourceIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                sourcePort = ((IPEndPoint)client.Client.RemoteEndPoint).Port;
                NetworkStream ClientStream = null;

                try
                {
                    ClientStream = client.GetStream();
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

                if (!IsPeerConnected(client))
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

        private bool MessageWrite(TcpClient client, byte[] data)
        {
            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            int clientPort = ((IPEndPoint)client.Client.RemoteEndPoint).Port;

            try
            {
                #region Check-if-Connected

                if (!IsPeerConnected(client))
                {
                    Log("MessageWrite client " + clientIp + ":" + clientPort + " not connected");
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

                client.GetStream().Write(message, 0, message.Length);
                client.GetStream().Flush();
                return true;

                #endregion
            }
            catch (ObjectDisposedException ObjDispInner)
            {
                Log("*** MessageWrite " + clientIp + ":" + clientPort + " disconnected (obj disposed exception): " + ObjDispInner.Message);
                return false;
            }
            catch (SocketException SockInner)
            {
                Log("*** MessageWrite " + clientIp + ":" + clientPort + " disconnected (socket exception): " + SockInner.Message);
                return false;
            }
            catch (InvalidOperationException InvOpInner)
            {
                Log("*** MessageWrite " + clientIp + ":" + clientPort + " disconnected (invalid operation exception): " + InvOpInner.Message);
                return false;
            }
            catch (IOException IOInner)
            {
                Log("*** MessageWrite " + clientIp + ":" + clientPort + " disconnected (IO exception): " + IOInner.Message);
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
