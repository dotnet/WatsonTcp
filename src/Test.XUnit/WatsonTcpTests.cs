namespace Test.XUnit
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonTcp;
    using Xunit;

    /// <summary>
    /// xUnit mirror of Test.Automated tests.
    /// Tests run sequentially within this collection to avoid port conflicts.
    /// </summary>
    [Collection("WatsonTcp")]
    public class WatsonTcpTests : IDisposable
    {
        private static int _portCounter = 20000 + (System.Diagnostics.Process.GetCurrentProcess().Id % 10000);
        private static readonly object _portLock = new object();
        private readonly string _hostname = "127.0.0.1";

        private static int GetNextPort()
        {
            lock (_portLock)
            {
                return _portCounter++;
            }
        }

        private static void SetupDefaultServerHandlers(WatsonTcpServer server)
        {
            server.Events.MessageReceived += (s, e) => { };
        }

        private static void SetupDefaultClientHandlers(WatsonTcpClient client)
        {
            client.Events.MessageReceived += (s, e) => { };
        }

        private static void SafeDispose(IDisposable obj)
        {
            try { obj?.Dispose(); }
            catch (AggregateException) { }
        }

        public void Dispose() { GC.SuppressFinalize(this); }

        #region Basic-Connection-Tests

        [Fact]
        public async Task BasicServerStartStop()
        {
            int port = GetNextPort();
            using var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            Assert.True(server.IsListening);

            server.Stop();
            await Task.Delay(200);

            Assert.False(server.IsListening);
        }

        [Fact]
        public async Task BasicClientConnection()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                Assert.True(client.Connected);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task ClientServerConnection()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                var clients = server.ListClients().ToList();
                Assert.Single(clients);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Message-Send-Receive-Tests

        [Fact]
        public async Task ClientSendServerReceive()
        {
            int port = GetNextPort();
            string receivedData = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) =>
            {
                receivedData = Encoding.UTF8.GetString(e.Data);
                messageReceived.Set();
            };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                string testData = "Hello from client!";
                await client.SendAsync(testData);
                Assert.True(messageReceived.WaitOne(5000), "Server did not receive message");
                Assert.Equal(testData, receivedData);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task ServerSendClientReceive()
        {
            int port = GetNextPort();
            string receivedData = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Events.MessageReceived += (s, e) =>
            {
                receivedData = Encoding.UTF8.GetString(e.Data);
                messageReceived.Set();
            };
            client.Connect();
            await Task.Delay(200);

            try
            {
                string testData = "Hello from server!";
                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, testData);
                Assert.True(messageReceived.WaitOne(5000), "Client did not receive message");
                Assert.Equal(testData, receivedData);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task BidirectionalCommunication()
        {
            int port = GetNextPort();
            string serverReceived = null;
            string clientReceived = null;
            var serverGotMessage = new ManualResetEvent(false);
            var clientGotMessage = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) => { serverReceived = Encoding.UTF8.GetString(e.Data); serverGotMessage.Set(); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Events.MessageReceived += (s, e) => { clientReceived = Encoding.UTF8.GetString(e.Data); clientGotMessage.Set(); };
            client.Connect();
            await Task.Delay(200);

            try
            {
                await client.SendAsync("From client");
                Assert.True(serverGotMessage.WaitOne(5000));
                Assert.Equal("From client", serverReceived);

                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, "From server");
                Assert.True(clientGotMessage.WaitOne(5000));
                Assert.Equal("From server", clientReceived);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task EmptyMessageWithMetadata()
        {
            int port = GetNextPort();
            Dictionary<string, object> receivedMetadata = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) => { receivedMetadata = e.Metadata; messageReceived.Set(); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                var metadata = new Dictionary<string, object> { { "test", "value" } };
                await client.SendAsync("", metadata);
                Assert.True(messageReceived.WaitOne(5000));
                Assert.NotNull(receivedMetadata);
                Assert.True(receivedMetadata.ContainsKey("test"));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Metadata-Tests

        [Fact]
        public async Task SendWithMetadata()
        {
            int port = GetNextPort();
            Dictionary<string, object> receivedMetadata = null;
            string receivedData = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) => { receivedData = Encoding.UTF8.GetString(e.Data); receivedMetadata = e.Metadata; messageReceived.Set(); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                var metadata = new Dictionary<string, object> { { "key1", "value1" }, { "key2", 42 }, { "key3", true } };
                await client.SendAsync("Test data", metadata);
                Assert.True(messageReceived.WaitOne(5000));
                Assert.Equal("Test data", receivedData);
                Assert.NotNull(receivedMetadata);
                Assert.Equal(3, receivedMetadata.Count);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task ReceiveWithMetadata()
        {
            int port = GetNextPort();
            Dictionary<string, object> receivedMetadata = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Events.MessageReceived += (s, e) => { receivedMetadata = e.Metadata; messageReceived.Set(); };
            client.Connect();
            await Task.Delay(200);

            try
            {
                var metadata = new Dictionary<string, object> { { "server", "data" } };
                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, "Server message", metadata);
                Assert.True(messageReceived.WaitOne(5000));
                Assert.NotNull(receivedMetadata);
                Assert.True(receivedMetadata.ContainsKey("server"));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Sync-Request-Response-Tests

        [Fact]
        public async Task SyncRequestResponse()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.SyncRequestReceivedAsync = async (req) => { await Task.Delay(10); return new SyncResponse(req, "Response from server"); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                SyncResponse response = await client.SendAndWaitAsync(5000, "Request from client");
                Assert.NotNull(response);
                Assert.Equal("Response from server", Encoding.UTF8.GetString(response.Data));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task SyncRequestTimeout()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.SyncRequestReceivedAsync = async (req) => { await Task.Delay(3000); return new SyncResponse(req, "Too late"); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                await Assert.ThrowsAsync<TimeoutException>(() => client.SendAndWaitAsync(1000, "Request"));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Event-Tests

        [Fact]
        public async Task ServerConnectedEvent()
        {
            int port = GetNextPort();
            bool eventFired = false;
            var connectionEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Events.ServerConnected += (s, e) => { eventFired = true; connectionEvent.Set(); };
            client.Connect();

            try
            {
                Assert.True(connectionEvent.WaitOne(5000));
                Assert.True(eventFired);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task ServerDisconnectedEvent()
        {
            int port = GetNextPort();
            bool eventFired = false;
            var disconnectionEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Events.ServerDisconnected += (s, e) => { eventFired = true; disconnectionEvent.Set(); };
            client.Connect();
            await Task.Delay(200);

            try
            {
                var clients = server.ListClients().ToList();
                await server.DisconnectClientAsync(clients[0].Guid);
                Assert.True(disconnectionEvent.WaitOne(5000));
                Assert.True(eventFired);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task ClientConnectedEvent()
        {
            int port = GetNextPort();
            Guid? connectedGuid = null;
            var connectionEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.ClientConnected += (s, e) => { connectedGuid = e.Client.Guid; connectionEvent.Set(); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();

            try
            {
                Assert.True(connectionEvent.WaitOne(5000));
                Assert.NotNull(connectedGuid);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task ClientDisconnectedEvent()
        {
            int port = GetNextPort();
            bool eventFired = false;
            var disconnectionEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.ClientDisconnected += (s, e) => { eventFired = true; disconnectionEvent.Set(); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                client.Disconnect();
                Assert.True(disconnectionEvent.WaitOne(5000));
                Assert.True(eventFired);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task MessageReceivedEvent()
        {
            int port = GetNextPort();
            int messageCount = 0;
            var messageEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) => { messageCount++; messageEvent.Set(); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                await client.SendAsync("Test message");
                Assert.True(messageEvent.WaitOne(5000));
                Assert.Equal(1, messageCount);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Stream-Tests

        [Fact]
        public async Task StreamSendReceive()
        {
            int port = GetNextPort();
            long receivedLength = 0;
            var streamReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Events.StreamReceived += (s, e) =>
            {
                receivedLength = e.ContentLength;
                byte[] buf = new byte[e.ContentLength];
                int totalRead = 0;
                while (totalRead < buf.Length)
                {
                    int n = e.DataStream.Read(buf, totalRead, buf.Length - totalRead);
                    if (n <= 0) break;
                    totalRead += n;
                }
                streamReceived.Set();
            };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                byte[] data = Encoding.UTF8.GetBytes("Stream data test");
                using var ms = new MemoryStream(data);
                await client.SendAsync(data.Length, ms);
                Assert.True(streamReceived.WaitOne(5000));
                Assert.Equal(data.Length, receivedLength);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task LargeStreamTransfer()
        {
            int port = GetNextPort();
            long receivedLength = 0;
            bool dataVerified = false;
            var streamReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Events.StreamReceived += (s, e) =>
            {
                receivedLength = e.ContentLength;
                byte[] buffer = new byte[8192];
                long totalRead = 0;
                bool valid = true;
                while (totalRead < e.ContentLength)
                {
                    int bytesRead = e.DataStream.Read(buffer, 0, buffer.Length);
                    if (bytesRead <= 0) break;
                    for (int i = 0; i < bytesRead && valid; i++)
                        if (buffer[i] != (byte)((totalRead + i) % 256)) valid = false;
                    totalRead += bytesRead;
                }
                dataVerified = valid;
                streamReceived.Set();
            };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                int dataSize = 10 * 1024 * 1024;
                using var ms = new MemoryStream();
                for (long i = 0; i < dataSize; i++) ms.WriteByte((byte)(i % 256));
                ms.Seek(0, SeekOrigin.Begin);
                await client.SendAsync(dataSize, ms);

                Assert.True(streamReceived.WaitOne(30000));
                Assert.Equal(dataSize, receivedLength);
                Assert.True(dataVerified);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Statistics-Tests

        [Fact]
        public async Task ClientStatistics()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                long initialSent = client.Statistics.SentBytes;
                await client.SendAsync("Test message");
                await Task.Delay(200);
                Assert.True(client.Statistics.SentBytes > initialSent);

                long initialReceived = client.Statistics.ReceivedBytes;
                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, "Response");
                await Task.Delay(200);
                Assert.True(client.Statistics.ReceivedBytes > initialReceived);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task ServerStatistics()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            long initialReceived = server.Statistics.ReceivedBytes;
            long initialSent = server.Statistics.SentBytes;

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                await client.SendAsync("Client message");
                await Task.Delay(200);
                Assert.True(server.Statistics.ReceivedBytes > initialReceived);

                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, "Server message");
                await Task.Delay(200);
                Assert.True(server.Statistics.SentBytes > initialSent);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Multiple-Client-Tests

        [Fact]
        public async Task MultipleClients()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            var clients = new List<WatsonTcpClient>();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var c = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(c);
                    c.Connect();
                    clients.Add(c);
                    await Task.Delay(200);
                }
                Assert.Equal(3, server.ListClients().Count());
            }
            finally
            {
                foreach (var c in clients) SafeDispose(c);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task ListClients()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                var clientList = server.ListClients().ToList();
                Assert.Single(clientList);
                Assert.NotEqual(Guid.Empty, clientList[0].Guid);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Disconnection-Tests

        [Fact]
        public async Task ClientDisconnect()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            Assert.True(client.Connected);
            client.Disconnect();
            await Task.Delay(200);
            Assert.False(client.Connected);

            SafeDispose(client);
            SafeDispose(server);
        }

        [Fact]
        public async Task ServerDisconnectClient()
        {
            int port = GetNextPort();
            var disconnectEvent = new ManualResetEvent(false);
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Events.ServerDisconnected += (s, e) => disconnectEvent.Set();
            client.Connect();
            await Task.Delay(200);

            try
            {
                var clients = server.ListClients().ToList();
                await server.DisconnectClientAsync(clients[0].Guid);
                Assert.True(disconnectEvent.WaitOne(5000));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task ServerStop()
        {
            int port = GetNextPort();
            var disconnectEvent = new ManualResetEvent(false);
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Events.ServerDisconnected += (s, e) => disconnectEvent.Set();
            client.Connect();
            await Task.Delay(200);

            try
            {
                SafeDispose(server);
                // Trigger disconnect detection by attempting a send
                try { await client.SendAsync("trigger"); } catch { }
                Assert.True(disconnectEvent.WaitOne(10000));
            }
            finally
            {
                SafeDispose(client);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Large-Data-Tests

        [Fact]
        public async Task LargeMessageTransfer()
        {
            int port = GetNextPort();
            byte[] receivedData = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) => { receivedData = e.Data; messageReceived.Set(); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                byte[] data = new byte[1024 * 1024]; // 1MB
                new Random(42).NextBytes(data);
                await client.SendAsync(data);
                Assert.True(messageReceived.WaitOne(30000));
                Assert.Equal(data.Length, receivedData.Length);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task ManyMessages()
        {
            int port = GetNextPort();
            int count = 0;
            var allReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) => { if (Interlocked.Increment(ref count) >= 100) allReceived.Set(); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                for (int i = 0; i < 100; i++) await client.SendAsync("Message " + i);
                Assert.True(allReceived.WaitOne(30000));
                Assert.Equal(100, count);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Error-Condition-Tests

        [Fact]
        public async Task SendToNonExistentClient()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            try
            {
                await Assert.ThrowsAsync<KeyNotFoundException>(() => server.SendAsync(Guid.NewGuid(), "Hello"));
            }
            finally
            {
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task ConnectToNonExistentServer()
        {
            var client = new WatsonTcpClient("10.1.2.3", 1234);
            SetupDefaultClientHandlers(client);
            client.Settings.ConnectTimeoutSeconds = 2;
            await Task.Delay(50);

            try
            {
                Assert.ThrowsAny<Exception>(() => client.Connect());
            }
            finally
            {
                SafeDispose(client);
            }
        }

        #endregion

        #region Concurrent-Tests

        [Fact]
        public async Task ConcurrentClientConnections()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            var clients = new List<WatsonTcpClient>();
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var c = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(c);
                    c.Connect();
                    clients.Add(c);
                    await Task.Delay(100);
                }
                await Task.Delay(500);
                Assert.Equal(5, server.ListClients().Count());
            }
            finally
            {
                foreach (var c in clients) SafeDispose(c);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task ConcurrentMessageSends()
        {
            int port = GetNextPort();
            int count = 0;
            var allReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) => { if (Interlocked.Increment(ref count) >= 10) allReceived.Set(); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                var tasks = Enumerable.Range(0, 10).Select(i => client.SendAsync("Msg " + i)).ToArray();
                await Task.WhenAll(tasks);
                Assert.True(allReceived.WaitOne(10000));
                Assert.Equal(10, count);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Client-GUID-Tests

        [Fact]
        public async Task SpecifyClientGuid()
        {
            int port = GetNextPort();
            Guid customGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
            Guid? serverSawGuid = null;
            var connectedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.ClientConnected += (s, e) => { serverSawGuid = e.Client.Guid; connectedEvent.Set(); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Settings.Guid = customGuid;
            client.Connect();

            try
            {
                Assert.True(connectedEvent.WaitOne(5000));
                Assert.Equal(customGuid, serverSawGuid);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Idle-Timeout-Tests

        [Fact]
        public async Task IdleClientTimeout()
        {
            int port = GetNextPort();
            bool timedOut = false;
            var disconnectEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Settings.IdleClientTimeoutSeconds = 3;
            server.Events.ClientDisconnected += (s, e) =>
            {
                if (e.Reason == DisconnectReason.Timeout) timedOut = true;
                disconnectEvent.Set();
            };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                Assert.True(disconnectEvent.WaitOne(15000));
                Assert.True(timedOut);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Authentication-Tests

        [Fact]
        public async Task AuthenticationSuccess()
        {
            int port = GetNextPort();
            string presharedKey = "0000000000000000";
            bool authSucceeded = false;
            var authEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Events.MessageReceived += (s, e) => { };
            server.Settings.PresharedKey = presharedKey;
            server.Events.AuthenticationSucceeded += (s, e) => { authSucceeded = true; authEvent.Set(); };
            server.Start();
            await Task.Delay(200);

            var client = new WatsonTcpClient(_hostname, port);
            client.Events.MessageReceived += (s, e) => { };
            client.Settings.PresharedKey = presharedKey;
            client.Connect();
            await Task.Delay(500);

            try
            {
                Assert.True(authEvent.WaitOne(5000));
                Assert.True(authSucceeded);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task AuthenticationFailure()
        {
            int port = GetNextPort();
            bool authFailed = false;
            var authEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Events.MessageReceived += (s, e) => { };
            server.Settings.PresharedKey = "correctkey123456";
            server.Events.AuthenticationFailed += (s, e) => { authFailed = true; authEvent.Set(); };
            server.Start();
            await Task.Delay(200);

            var client = new WatsonTcpClient(_hostname, port);
            client.Events.MessageReceived += (s, e) => { };
            client.Settings.PresharedKey = "wrongkey12345678";
            client.Connect();

            try
            {
                Assert.True(authEvent.WaitOne(5000));
                Assert.True(authFailed);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task AuthenticationCallback()
        {
            int port = GetNextPort();
            string presharedKey = "callback12345678";
            bool authSucceeded = false;
            bool callbackCalled = false;
            var authEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Events.MessageReceived += (s, e) => { };
            server.Settings.PresharedKey = presharedKey;
            server.Events.AuthenticationSucceeded += (s, e) => { authSucceeded = true; authEvent.Set(); };
            server.Start();
            await Task.Delay(200);

            var client = new WatsonTcpClient(_hostname, port);
            client.Events.MessageReceived += (s, e) => { };
            client.Callbacks.AuthenticationRequested = () => { callbackCalled = true; return presharedKey; };
            client.Connect();
            await Task.Delay(1000);

            try
            {
                Assert.True(authEvent.WaitOne(5000));
                Assert.True(authSucceeded);
                Assert.True(callbackCalled);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Throughput-Tests

        [Fact]
        public async Task ThroughputSmallMessages()
        {
            await RunThroughputTest(messageSize: 64, messageCount: 5000);
        }

        [Fact]
        public async Task ThroughputMediumMessages()
        {
            await RunThroughputTest(messageSize: 65536, messageCount: 500);
        }

        [Fact]
        public async Task ThroughputLargeMessages()
        {
            await RunThroughputTest(messageSize: 4 * 1024 * 1024, messageCount: 20);
        }

        private async Task RunThroughputTest(int messageSize, int messageCount)
        {
            int port = GetNextPort();
            int receivedCount = 0;
            var allReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) =>
            {
                if (Interlocked.Increment(ref receivedCount) >= messageCount)
                    allReceived.Set();
            };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                byte[] data = new byte[messageSize];
                new Random(42).NextBytes(data);

                for (int i = 0; i < messageCount; i++)
                {
                    bool sent = await client.SendAsync(data);
                    Assert.True(sent, $"Send failed at message {i}");
                }

                int timeoutMs = Math.Max(30000, messageCount * 100);
                Assert.True(allReceived.WaitOne(timeoutMs), $"Only {receivedCount}/{messageCount} messages received within timeout");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region v6.1.0-MaxConnections-Tests

        [Fact]
        public async Task MaxConnectionsEnforced()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Settings.MaxConnections = 2;
            server.Settings.EnforceMaxConnections = true;
            server.Start();
            await Task.Delay(100);

            var clients = new List<WatsonTcpClient>();
            WatsonTcpClient thirdClient = null;
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    var c = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(c);
                    c.Connect();
                    clients.Add(c);
                    await Task.Delay(200);
                }
                Assert.Equal(2, server.Connections);

                bool rejected = false;
                try
                {
                    thirdClient = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(thirdClient);
                    thirdClient.Connect();
                    await Task.Delay(500);
                    if (server.Connections <= 2) rejected = true;
                }
                catch { rejected = true; }

                Assert.True(rejected);
            }
            finally
            {
                SafeDispose(thirdClient);
                foreach (var c in clients) SafeDispose(c);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task MaxConnectionsNotEnforced()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Settings.MaxConnections = 2;
            server.Settings.EnforceMaxConnections = false;
            server.Start();
            await Task.Delay(100);

            var clients = new List<WatsonTcpClient>();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var c = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(c);
                    c.Connect();
                    clients.Add(c);
                    await Task.Delay(200);
                }
                await Task.Delay(500);
                Assert.True(server.Connections >= 3);
            }
            finally
            {
                foreach (var c in clients) SafeDispose(c);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region v6.1.0-MaxHeaderSize-Tests

        [Fact]
        public void MaxHeaderSizeSetting()
        {
            var serverSettings = new WatsonTcpServerSettings();
            serverSettings.MaxHeaderSize = 1024;
            Assert.Equal(1024, serverSettings.MaxHeaderSize);

            var clientSettings = new WatsonTcpClientSettings();
            clientSettings.MaxHeaderSize = 2048;
            Assert.Equal(2048, clientSettings.MaxHeaderSize);

            Assert.Throws<ArgumentException>(() => serverSettings.MaxHeaderSize = 10);
        }

        #endregion

        #region v6.1.0-Rapid-Connect-Disconnect

        [Fact]
        public async Task RapidConnectDisconnect()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            try
            {
                for (int i = 0; i < 10; i++)
                {
                    var client = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(client);
                    client.Connect();
                    await Task.Delay(50);
                    SafeDispose(client);
                    await Task.Delay(50);
                }
                await Task.Delay(500);
                Assert.True(server.IsListening);
            }
            finally
            {
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region v6.1.0-Concurrent-Sync-Requests

        [Fact]
        public async Task ConcurrentSyncRequests()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.SyncRequestReceivedAsync = async (req) =>
            {
                string data = Encoding.UTF8.GetString(req.Data);
                await Task.Delay(50);
                return new SyncResponse(req, "Reply:" + data);
            };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                var tasks = Enumerable.Range(0, 5)
                    .Select(i => client.SendAndWaitAsync(10000, "Request" + i))
                    .ToArray();
                var responses = await Task.WhenAll(tasks);

                var expected = Enumerable.Range(0, 5).Select(i => "Reply:Request" + i).ToHashSet();
                var actual = responses.Select(r => Encoding.UTF8.GetString(r.Data)).ToHashSet();
                Assert.True(expected.SetEquals(actual));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region v6.1.0-SSL-Tests

        [Fact]
        public async Task SslConnectivity()
        {
            string pfxFile = "test.pfx";
            if (!File.Exists(pfxFile))
            {
                // Skip if no certificate available
                return;
            }

            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port, pfxFile, "password");
            SetupDefaultServerHandlers(server);
            server.Settings.AcceptInvalidCertificates = true;
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port, pfxFile, "password");
            SetupDefaultClientHandlers(client);
            client.Settings.AcceptInvalidCertificates = true;
            client.Connect();
            await Task.Delay(200);

            try
            {
                Assert.True(client.Connected);
                Assert.Single(server.ListClients());
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task SslMessageExchange()
        {
            string pfxFile = "test.pfx";
            if (!File.Exists(pfxFile)) return;

            int port = GetNextPort();
            string receivedData = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port, pfxFile, "password");
            server.Settings.AcceptInvalidCertificates = true;
            server.Events.MessageReceived += (s, e) => { receivedData = Encoding.UTF8.GetString(e.Data); messageReceived.Set(); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port, pfxFile, "password");
            SetupDefaultClientHandlers(client);
            client.Settings.AcceptInvalidCertificates = true;
            client.Connect();
            await Task.Delay(200);

            try
            {
                await client.SendAsync("Hello over SSL!");
                Assert.True(messageReceived.WaitOne(5000));
                Assert.Equal("Hello over SSL!", receivedData);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region v6.1.0-Edge-Cases

        [Fact]
        public async Task DuplicateClientGuid()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await Task.Delay(100);

            Guid sharedGuid = Guid.NewGuid();
            var client1 = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client1);
            client1.Settings.Guid = sharedGuid;
            client1.Connect();
            await Task.Delay(300);

            var client2 = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client2);
            client2.Settings.Guid = sharedGuid;
            client2.Connect();
            await Task.Delay(500);

            try
            {
                Assert.True(client1.Connected || client2.Connected);
            }
            finally
            {
                SafeDispose(client1);
                SafeDispose(client2);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        [Fact]
        public async Task SendWithOffset()
        {
            int port = GetNextPort();
            byte[] receivedBytes = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Events.MessageReceived += (s, e) => { receivedBytes = e.Data; messageReceived.Set(); };
            server.Start();
            await Task.Delay(100);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await Task.Delay(200);

            try
            {
                byte[] fullData = Encoding.UTF8.GetBytes("HEADERHello World");
                await client.SendAsync(fullData, null, 6); // skip "HEADER"
                Assert.True(messageReceived.WaitOne(5000));
                Assert.Equal("Hello World", Encoding.UTF8.GetString(receivedBytes));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion
    }
}
