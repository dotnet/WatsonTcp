namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonTcp;

    /// <summary>
    /// xUnit mirror of Test.Automated tests.
    /// Tests run sequentially within this collection to avoid port conflicts.
    /// </summary>
    public static class WatsonTcpScenarios
    {
        private static readonly string _hostname = "127.0.0.1";
        private const int DefaultConditionTimeoutMs = 3000;
        private const int DefaultEventTimeoutMs = 3000;
        private const int PollIntervalMs = 10;

        private static int GetNextPort()
        {
            // Ask the OS for a free ephemeral loopback port so separate test hosts do not reuse a fixed range.
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
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

        private static string ComputeChallengeResponseProof(string secret, string nonce)
        {
            if (secret == null) throw new ArgumentNullException(nameof(secret));
            if (nonce == null) throw new ArgumentNullException(nameof(nonce));

            byte[] secretBytes = Encoding.UTF8.GetBytes(secret);
            byte[] nonceBytes = Encoding.UTF8.GetBytes(nonce);

            using HMACSHA256 hmac = new HMACSHA256(secretBytes);
            return Convert.ToBase64String(hmac.ComputeHash(nonceBytes));
        }

        private static byte[] CreatePatternedPayload(int size)
        {
            if (size < 0) throw new ArgumentException("Size must be zero or greater.", nameof(size));

            byte[] data = new byte[size];
            for (int i = 0; i < size; i++) data[i] = (byte)(i % 251);
            return data;
        }

        private static MemoryStream CreatePatternedStream(int size)
        {
            return new MemoryStream(CreatePatternedPayload(size), writable: false);
        }

        private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken token = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            using MemoryStream ms = new MemoryStream();
            await stream.CopyToAsync(ms, 81920, token).ConfigureAwait(false);
            return ms.ToArray();
        }

        private static List<string> CreateLogCapture(out Action<Severity, string> logger)
        {
            object logLock = new object();
            List<string> messages = new List<string>();
            logger = (severity, message) =>
            {
                lock (logLock)
                {
                    messages.Add(severity.ToString() + "|" + message);
                }
            };
            return messages;
        }

        private static bool LogContains(IEnumerable<string> messages, string fragment)
        {
            if (messages == null) return false;
            return messages.Any(msg => msg != null && msg.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs = DefaultConditionTimeoutMs, string failureMessage = null)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            DateTime timeout = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < timeout)
            {
                if (condition()) return;
                await Task.Delay(PollIntervalMs).ConfigureAwait(false);
            }

            TestAssert.True(condition(), failureMessage ?? "Condition was not satisfied before the timeout expired.");
        }

        private static async Task WaitForServerListeningAsync(WatsonTcpServer server, int timeoutMs = DefaultConditionTimeoutMs)
        {
            await WaitForConditionAsync(() => server.IsListening, timeoutMs, "Server did not start listening in time.").ConfigureAwait(false);
        }

        private static async Task WaitForServerStoppedAsync(WatsonTcpServer server, int timeoutMs = DefaultConditionTimeoutMs)
        {
            await WaitForConditionAsync(() => !server.IsListening, timeoutMs, "Server did not stop listening in time.").ConfigureAwait(false);
        }

        private static async Task WaitForClientConnectedAsync(WatsonTcpClient client, WatsonTcpServer server = null, int expectedServerClients = 0, int timeoutMs = DefaultConditionTimeoutMs)
        {
            await WaitForConditionAsync(() =>
            {
                if (!client.Connected) return false;
                if (server == null) return true;
                return server.ListClients().Count() >= expectedServerClients;
            }, timeoutMs, "Client did not reach the expected connected state in time.").ConfigureAwait(false);
        }

        private static async Task WaitForClientDisconnectedAsync(WatsonTcpClient client, int timeoutMs = DefaultConditionTimeoutMs)
        {
            await WaitForConditionAsync(() => !client.Connected, timeoutMs, "Client did not disconnect in time.").ConfigureAwait(false);
        }

        private static async Task WaitForServerClientCountAsync(WatsonTcpServer server, int expectedClientCount, int timeoutMs = DefaultConditionTimeoutMs)
        {
            await WaitForConditionAsync(() => server.ListClients().Count() == expectedClientCount, timeoutMs, "Server did not reach the expected client count in time.").ConfigureAwait(false);
        }

        private static async Task WaitForPendingConnectionsAsync(WatsonTcpServer server, int expectedPendingConnections, int timeoutMs = DefaultConditionTimeoutMs)
        {
            await WaitForConditionAsync(() => server.PendingConnections == expectedPendingConnections, timeoutMs, "Server did not reach the expected pending-connection count in time.").ConfigureAwait(false);
        }

        private static void WaitForSignal(WaitHandle handle, int timeoutMs = DefaultEventTimeoutMs, string failureMessage = null)
        {
            TestAssert.True(handle.WaitOne(timeoutMs), failureMessage ?? "Expected event was not raised before the timeout expired.");
        }

        #region Basic-Connection-Tests
        public static async Task BasicServerStartStop()
        {
            int port = GetNextPort();
            using var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            TestAssert.True(server.IsListening);

            server.Stop();
            await WaitForServerStoppedAsync(server);

            TestAssert.False(server.IsListening);
        }
        public static async Task BasicClientConnection()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                TestAssert.True(client.Connected);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task ClientServerConnection()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                var clients = server.ListClients().ToList();
                TestAssert.Single(clients);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Message-Send-Receive-Tests
        public static async Task ClientSendServerReceive()
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
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                string testData = "Hello from client!";
                await client.SendAsync(testData);
                WaitForSignal(messageReceived, failureMessage: "Server did not receive message.");
                TestAssert.Equal(testData, receivedData);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task ServerSendClientReceive()
        {
            int port = GetNextPort();
            string receivedData = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Events.MessageReceived += (s, e) =>
            {
                receivedData = Encoding.UTF8.GetString(e.Data);
                messageReceived.Set();
            };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                string testData = "Hello from server!";
                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, testData);
                WaitForSignal(messageReceived, failureMessage: "Client did not receive message.");
                TestAssert.Equal(testData, receivedData);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task BidirectionalCommunication()
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
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Events.MessageReceived += (s, e) => { clientReceived = Encoding.UTF8.GetString(e.Data); clientGotMessage.Set(); };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                await client.SendAsync("From client");
                WaitForSignal(serverGotMessage);
                TestAssert.Equal("From client", serverReceived);

                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, "From server");
                WaitForSignal(clientGotMessage);
                TestAssert.Equal("From server", clientReceived);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task EmptyMessageWithMetadata()
        {
            int port = GetNextPort();
            Dictionary<string, object> receivedMetadata = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) => { receivedMetadata = e.Metadata; messageReceived.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                var metadata = new Dictionary<string, object> { { "test", "value" } };
                await client.SendAsync("", metadata);
                WaitForSignal(messageReceived);
                TestAssert.NotNull(receivedMetadata);
                TestAssert.True(receivedMetadata.ContainsKey("test"));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Metadata-Tests
        public static async Task SendWithMetadata()
        {
            int port = GetNextPort();
            Dictionary<string, object> receivedMetadata = null;
            string receivedData = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) => { receivedData = Encoding.UTF8.GetString(e.Data); receivedMetadata = e.Metadata; messageReceived.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                var metadata = new Dictionary<string, object> { { "key1", "value1" }, { "key2", 42 }, { "key3", true } };
                await client.SendAsync("Test data", metadata);
                WaitForSignal(messageReceived);
                TestAssert.Equal("Test data", receivedData);
                TestAssert.NotNull(receivedMetadata);
                TestAssert.Equal(3, receivedMetadata.Count);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task ReceiveWithMetadata()
        {
            int port = GetNextPort();
            Dictionary<string, object> receivedMetadata = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Events.MessageReceived += (s, e) => { receivedMetadata = e.Metadata; messageReceived.Set(); };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                var metadata = new Dictionary<string, object> { { "server", "data" } };
                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, "Server message", metadata);
                WaitForSignal(messageReceived);
                TestAssert.NotNull(receivedMetadata);
                TestAssert.True(receivedMetadata.ContainsKey("server"));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Sync-Request-Response-Tests
        public static async Task SyncRequestResponse()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.SyncRequestReceivedAsync = async (req) => { await Task.Delay(10); return new SyncResponse(req, "Response from server"); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                SyncResponse response = await client.SendAndWaitAsync(5000, "Request from client");
                TestAssert.NotNull(response);
                TestAssert.Equal("Response from server", Encoding.UTF8.GetString(response.Data));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task SyncRequestTimeout()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.SyncRequestReceivedAsync = async (req) => { await Task.Delay(3000); return new SyncResponse(req, "Too late"); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                await TestAssert.ThrowsAsync<TimeoutException>(() => client.SendAndWaitAsync(1000, "Request"));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Event-Tests
        public static async Task ServerConnectedEvent()
        {
            int port = GetNextPort();
            bool eventFired = false;
            var connectionEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Events.ServerConnected += (s, e) => { eventFired = true; connectionEvent.Set(); };
            client.Connect();

            try
            {
                WaitForSignal(connectionEvent);
                TestAssert.True(eventFired);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task ServerDisconnectedEvent()
        {
            int port = GetNextPort();
            bool eventFired = false;
            var disconnectionEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Settings.IdleServerTimeoutMs = 500;
            client.Settings.IdleServerEvaluationIntervalMs = 100;
            client.Events.ServerDisconnected += (s, e) => { eventFired = true; disconnectionEvent.Set(); };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                var clients = server.ListClients().ToList();
                await server.DisconnectClientAsync(clients[0].Guid);
                try { await client.SendAsync("disconnect-check"); } catch { }
                WaitForSignal(disconnectionEvent);
                TestAssert.True(eventFired);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task ClientConnectedEvent()
        {
            int port = GetNextPort();
            Guid? connectedGuid = null;
            var connectionEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.ClientConnected += (s, e) => { connectedGuid = e.Client.Guid; connectionEvent.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();

            try
            {
                WaitForSignal(connectionEvent);
                TestAssert.NotNull(connectedGuid);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task ClientDisconnectedEvent()
        {
            int port = GetNextPort();
            bool eventFired = false;
            var disconnectionEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.ClientDisconnected += (s, e) => { eventFired = true; disconnectionEvent.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                client.Disconnect();
                WaitForSignal(disconnectionEvent);
                TestAssert.True(eventFired);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task MessageReceivedEvent()
        {
            int port = GetNextPort();
            int messageCount = 0;
            var messageEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) => { messageCount++; messageEvent.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                await client.SendAsync("Test message");
                WaitForSignal(messageEvent);
                TestAssert.Equal(1, messageCount);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Stream-Tests
        public static async Task StreamSendReceive()
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
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                byte[] data = Encoding.UTF8.GetBytes("Stream data test");
                using var ms = new MemoryStream(data);
                await client.SendAsync(data.Length, ms);
                WaitForSignal(streamReceived);
                TestAssert.Equal(data.Length, receivedLength);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task LargeStreamTransfer()
        {
            int port = GetNextPort();
            long receivedLength = 0;
            bool dataVerified = false;
            var streamReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Settings.MaxProxiedStreamSize = 1024;
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
                        if (buffer[i] != (byte)((totalRead + i) % 251)) valid = false;
                    totalRead += bytesRead;
                }
                dataVerified = valid;
                streamReceived.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1, timeoutMs: 5000);

            try
            {
                const int dataSize = 512 * 1024;
                using MemoryStream ms = CreatePatternedStream(dataSize);
                await client.SendAsync(dataSize, ms);

                WaitForSignal(streamReceived, timeoutMs: 10000);
                TestAssert.Equal(dataSize, receivedLength);
                TestAssert.True(dataVerified);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static void ServerStartFailsWithoutAnyReceiveHandler()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);

            try
            {
                InvalidOperationException exception = TestAssert.Throws<InvalidOperationException>(() => server.Start());
                TestAssert.True(exception.Message.Contains("Callbacks.StreamReceivedAsync", StringComparison.Ordinal), "Expected updated validation message.");
            }
            finally
            {
                SafeDispose(server);
            }
        }

        public static async Task ClientConnectFailsWithoutAnyReceiveHandler()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);

            try
            {
                InvalidOperationException exception = TestAssert.Throws<InvalidOperationException>(() => client.Connect());
                TestAssert.True(exception.Message.Contains("Callbacks.StreamReceivedAsync", StringComparison.Ordinal), "Expected updated validation message.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ServerStartsWithOnlyAsyncStreamCallback()
        {
            int port = GetNextPort();
            bool callbackInvoked = false;
            var payloadReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                callbackInvoked = true;
                await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
                payloadReceived.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                byte[] payload = CreatePatternedPayload(32);
                using MemoryStream ms = new MemoryStream(payload);
                await client.SendAsync(payload.Length, ms);
                WaitForSignal(payloadReceived);
                TestAssert.True(callbackInvoked, "Async stream callback should be used as the sole receive handler.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ClientConnectsWithOnlyAsyncStreamCallback()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
            };

            try
            {
                client.Connect();
                await WaitForClientConnectedAsync(client, server, 1);
                TestAssert.True(client.Connected, "Client should connect when only StreamReceivedAsync is configured.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task MessageReceivedTakesPrecedenceOverAsyncStreamCallbackOnServer()
        {
            int port = GetNextPort();
            int messageReceivedCount = 0;
            int asyncCallbackCount = 0;
            var messageReceived = new ManualResetEvent(false);
            List<string> logs = CreateLogCapture(out Action<Severity, string> logger);

            var server = new WatsonTcpServer(_hostname, port);
            server.Settings.Logger = logger;
            server.Events.MessageReceived += (s, e) =>
            {
                Interlocked.Increment(ref messageReceivedCount);
                messageReceived.Set();
            };
            server.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                Interlocked.Increment(ref asyncCallbackCount);
                await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                byte[] payload = CreatePatternedPayload(96);
                using MemoryStream ms = new MemoryStream(payload);
                await client.SendAsync(payload.Length, ms);
                WaitForSignal(messageReceived);

                TestAssert.Equal(1, messageReceivedCount);
                TestAssert.Equal(0, asyncCallbackCount);
                TestAssert.True(LogContains(logs, "MessageReceived and Callbacks.StreamReceivedAsync"), "Expected precedence warning through the logger.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task MessageReceivedTakesPrecedenceOverAsyncStreamCallbackOnClient()
        {
            int port = GetNextPort();
            int messageReceivedCount = 0;
            int asyncCallbackCount = 0;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            List<string> logs = CreateLogCapture(out Action<Severity, string> logger);
            client.Settings.Logger = logger;
            client.Events.MessageReceived += (s, e) =>
            {
                Interlocked.Increment(ref messageReceivedCount);
                messageReceived.Set();
            };
            client.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                Interlocked.Increment(ref asyncCallbackCount);
                await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
            };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                Guid clientGuid = server.ListClients().First().Guid;
                byte[] payload = CreatePatternedPayload(96);
                using MemoryStream ms = new MemoryStream(payload);
                await server.SendAsync(clientGuid, payload.Length, ms);
                WaitForSignal(messageReceived);

                TestAssert.Equal(1, messageReceivedCount);
                TestAssert.Equal(0, asyncCallbackCount);
                TestAssert.True(LogContains(logs, "MessageReceived and Callbacks.StreamReceivedAsync"), "Expected precedence warning through the logger.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AsyncStreamCallbackTakesPrecedenceOverSyncStreamEventOnServer()
        {
            int port = GetNextPort();
            int syncEventCount = 0;
            int asyncCallbackCount = 0;
            var callbackReceived = new ManualResetEvent(false);
            List<string> logs = CreateLogCapture(out Action<Severity, string> logger);

            var server = new WatsonTcpServer(_hostname, port);
            server.Settings.Logger = logger;
            server.Events.StreamReceived += (s, e) =>
            {
                Interlocked.Increment(ref syncEventCount);
            };
            server.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                Interlocked.Increment(ref asyncCallbackCount);
                await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
                callbackReceived.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                byte[] payload = CreatePatternedPayload(128);
                using MemoryStream ms = new MemoryStream(payload);
                await client.SendAsync(payload.Length, ms);
                WaitForSignal(callbackReceived);

                TestAssert.Equal(0, syncEventCount);
                TestAssert.Equal(1, asyncCallbackCount);
                TestAssert.True(LogContains(logs, "Callbacks.StreamReceivedAsync and StreamReceived"), "Expected precedence warning through the logger.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AsyncStreamCallbackTakesPrecedenceOverSyncStreamEventOnClient()
        {
            int port = GetNextPort();
            int syncEventCount = 0;
            int asyncCallbackCount = 0;
            var callbackReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            List<string> logs = CreateLogCapture(out Action<Severity, string> logger);
            client.Settings.Logger = logger;
            client.Events.StreamReceived += (s, e) =>
            {
                Interlocked.Increment(ref syncEventCount);
            };
            client.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                Interlocked.Increment(ref asyncCallbackCount);
                await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
                callbackReceived.Set();
            };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                Guid clientGuid = server.ListClients().First().Guid;
                byte[] payload = CreatePatternedPayload(128);
                using MemoryStream ms = new MemoryStream(payload);
                await server.SendAsync(clientGuid, payload.Length, ms);
                WaitForSignal(callbackReceived);

                TestAssert.Equal(0, syncEventCount);
                TestAssert.Equal(1, asyncCallbackCount);
                TestAssert.True(LogContains(logs, "Callbacks.StreamReceivedAsync and StreamReceived"), "Expected precedence warning through the logger.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ServerAsyncStreamReceiveSmallPayload()
        {
            int port = GetNextPort();
            byte[] received = null;
            var payloadReceived = new ManualResetEvent(false);
            byte[] expected = CreatePatternedPayload(48);

            var server = new WatsonTcpServer(_hostname, port);
            server.Settings.MaxProxiedStreamSize = 256;
            server.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                received = await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
                payloadReceived.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                using MemoryStream ms = new MemoryStream(expected);
                await client.SendAsync(expected.Length, ms);
                WaitForSignal(payloadReceived);
                TestAssert.Equal(expected.Length, received.Length);
                TestAssert.True(expected.SequenceEqual(received), "Server should receive the exact small payload.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ServerAsyncStreamReceiveLargePayload()
        {
            int port = GetNextPort();
            byte[] received = null;
            var payloadReceived = new ManualResetEvent(false);
            byte[] expected = CreatePatternedPayload(4096);

            var server = new WatsonTcpServer(_hostname, port);
            server.Settings.MaxProxiedStreamSize = 64;
            server.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                received = await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
                payloadReceived.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                using MemoryStream ms = new MemoryStream(expected);
                await client.SendAsync(expected.Length, ms);
                WaitForSignal(payloadReceived);
                TestAssert.Equal(expected.Length, received.Length);
                TestAssert.True(expected.SequenceEqual(received), "Server should receive the exact large payload via the async callback.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ServerAsyncStreamReceiveExactThresholdPayload()
        {
            int port = GetNextPort();
            byte[] received = null;
            var payloadReceived = new ManualResetEvent(false);
            byte[] expected = CreatePatternedPayload(64);

            var server = new WatsonTcpServer(_hostname, port);
            server.Settings.MaxProxiedStreamSize = 64;
            server.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                received = await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
                payloadReceived.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                using MemoryStream ms = new MemoryStream(expected);
                await client.SendAsync(expected.Length, ms);
                WaitForSignal(payloadReceived);
                TestAssert.True(expected.SequenceEqual(received), "Exact-threshold payload should be delivered intact.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ClientAsyncStreamReceiveSmallPayload()
        {
            int port = GetNextPort();
            byte[] received = null;
            var payloadReceived = new ManualResetEvent(false);
            byte[] expected = CreatePatternedPayload(48);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.Settings.MaxProxiedStreamSize = 256;
            client.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                received = await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
                payloadReceived.Set();
            };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                Guid clientGuid = server.ListClients().First().Guid;
                using MemoryStream ms = new MemoryStream(expected);
                await server.SendAsync(clientGuid, expected.Length, ms);
                WaitForSignal(payloadReceived);
                TestAssert.Equal(expected.Length, received.Length);
                TestAssert.True(expected.SequenceEqual(received), "Client should receive the exact small payload.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ClientAsyncStreamReceiveLargePayload()
        {
            int port = GetNextPort();
            byte[] received = null;
            var payloadReceived = new ManualResetEvent(false);
            byte[] expected = CreatePatternedPayload(4096);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.Settings.MaxProxiedStreamSize = 64;
            client.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                received = await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
                payloadReceived.Set();
            };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                Guid clientGuid = server.ListClients().First().Guid;
                using MemoryStream ms = new MemoryStream(expected);
                await server.SendAsync(clientGuid, expected.Length, ms);
                WaitForSignal(payloadReceived);
                TestAssert.Equal(expected.Length, received.Length);
                TestAssert.True(expected.SequenceEqual(received), "Client should receive the exact large payload via the async callback.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ClientAsyncStreamReceiveExactThresholdPayload()
        {
            int port = GetNextPort();
            byte[] received = null;
            var payloadReceived = new ManualResetEvent(false);
            byte[] expected = CreatePatternedPayload(64);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.Settings.MaxProxiedStreamSize = 64;
            client.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                received = await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
                payloadReceived.Set();
            };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                Guid clientGuid = server.ListClients().First().Guid;
                using MemoryStream ms = new MemoryStream(expected);
                await server.SendAsync(clientGuid, expected.Length, ms);
                WaitForSignal(payloadReceived);
                TestAssert.True(expected.SequenceEqual(received), "Exact-threshold payload should be delivered intact.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ServerAsyncStreamReceivePartialReadThenReturnDrainsRemainder()
        {
            int port = GetNextPort();
            int invocation = 0;
            byte[] secondPayload = Encoding.UTF8.GetBytes("follow-up");
            byte[] secondReceived = null;
            var secondReceivedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Settings.MaxProxiedStreamSize = 64;
            server.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                int current = Interlocked.Increment(ref invocation);
                if (current == 1)
                {
                    byte[] partial = new byte[16];
                    int bytesRead = await args.DataStream.ReadAsync(partial, 0, partial.Length, token).ConfigureAwait(false);
                    TestAssert.Equal(16, bytesRead);
                    return;
                }

                secondReceived = await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
                secondReceivedEvent.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                using MemoryStream large = new MemoryStream(CreatePatternedPayload(512));
                await client.SendAsync((int)large.Length, large);

                using MemoryStream followUp = new MemoryStream(secondPayload);
                await client.SendAsync(secondPayload.Length, followUp);

                WaitForSignal(secondReceivedEvent);
                TestAssert.Equal(2, invocation);
                TestAssert.True(secondPayload.SequenceEqual(secondReceived), "Second stream payload should remain intact after remainder drain.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ClientAsyncStreamReceivePartialReadThenReturnDrainsRemainder()
        {
            int port = GetNextPort();
            int invocation = 0;
            byte[] secondPayload = Encoding.UTF8.GetBytes("follow-up");
            byte[] secondReceived = null;
            var secondReceivedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.Settings.MaxProxiedStreamSize = 64;
            client.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                int current = Interlocked.Increment(ref invocation);
                if (current == 1)
                {
                    byte[] partial = new byte[16];
                    int bytesRead = await args.DataStream.ReadAsync(partial, 0, partial.Length, token).ConfigureAwait(false);
                    TestAssert.Equal(16, bytesRead);
                    return;
                }

                secondReceived = await ReadAllBytesAsync(args.DataStream, token).ConfigureAwait(false);
                secondReceivedEvent.Set();
            };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                Guid clientGuid = server.ListClients().First().Guid;
                using MemoryStream large = new MemoryStream(CreatePatternedPayload(512));
                await server.SendAsync(clientGuid, (int)large.Length, large);

                using MemoryStream followUp = new MemoryStream(secondPayload);
                await server.SendAsync(clientGuid, secondPayload.Length, followUp);

                WaitForSignal(secondReceivedEvent);
                TestAssert.Equal(2, invocation);
                TestAssert.True(secondPayload.SequenceEqual(secondReceived), "Second stream payload should remain intact after remainder drain.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ServerAsyncStreamReceiveCallbackThrowsBeforeRead()
        {
            int port = GetNextPort();
            string exceptionMessage = null;
            var exceptionEncountered = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Settings.MaxProxiedStreamSize = 64;
            server.Callbacks.StreamReceivedAsync = (args, token) => throw new InvalidOperationException("server stream callback exploded");
            server.Events.ExceptionEncountered += (s, e) =>
            {
                exceptionMessage = e.Exception.Message;
                exceptionEncountered.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                using MemoryStream ms = new MemoryStream(CreatePatternedPayload(256));
                await client.SendAsync((int)ms.Length, ms);
                WaitForSignal(exceptionEncountered);
                await WaitForServerClientCountAsync(server, 0, timeoutMs: 5000);

                TestAssert.Equal("server stream callback exploded", exceptionMessage);
                TestAssert.Equal(0, server.ListClients().Count());
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ServerAsyncStreamReceiveCallbackThrowsAfterPartialRead()
        {
            int port = GetNextPort();
            string exceptionMessage = null;
            var exceptionEncountered = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Settings.MaxProxiedStreamSize = 64;
            server.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                byte[] partial = new byte[16];
                await args.DataStream.ReadAsync(partial, 0, partial.Length, token).ConfigureAwait(false);
                throw new InvalidOperationException("server partial stream callback exploded");
            };
            server.Events.ExceptionEncountered += (s, e) =>
            {
                exceptionMessage = e.Exception.Message;
                exceptionEncountered.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                using MemoryStream ms = new MemoryStream(CreatePatternedPayload(256));
                await client.SendAsync((int)ms.Length, ms);
                WaitForSignal(exceptionEncountered);
                await WaitForServerClientCountAsync(server, 0, timeoutMs: 5000);

                TestAssert.Equal("server partial stream callback exploded", exceptionMessage);
                TestAssert.Equal(0, server.ListClients().Count());
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ClientAsyncStreamReceiveCallbackThrowsBeforeRead()
        {
            int port = GetNextPort();
            string exceptionMessage = null;
            var exceptionEncountered = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.Settings.MaxProxiedStreamSize = 64;
            client.Callbacks.StreamReceivedAsync = (args, token) => throw new InvalidOperationException("client stream callback exploded");
            client.Events.ExceptionEncountered += (s, e) =>
            {
                exceptionMessage = e.Exception.Message;
                exceptionEncountered.Set();
            };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                Guid clientGuid = server.ListClients().First().Guid;
                using MemoryStream ms = new MemoryStream(CreatePatternedPayload(256));
                await server.SendAsync(clientGuid, (int)ms.Length, ms);
                WaitForSignal(exceptionEncountered);
                await WaitForClientDisconnectedAsync(client, timeoutMs: 5000);

                TestAssert.Equal("client stream callback exploded", exceptionMessage);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ClientAsyncStreamReceiveCallbackThrowsAfterPartialRead()
        {
            int port = GetNextPort();
            string exceptionMessage = null;
            var exceptionEncountered = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.Settings.MaxProxiedStreamSize = 64;
            client.Callbacks.StreamReceivedAsync = async (args, token) =>
            {
                byte[] partial = new byte[16];
                await args.DataStream.ReadAsync(partial, 0, partial.Length, token).ConfigureAwait(false);
                throw new InvalidOperationException("client partial stream callback exploded");
            };
            client.Events.ExceptionEncountered += (s, e) =>
            {
                exceptionMessage = e.Exception.Message;
                exceptionEncountered.Set();
            };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                Guid clientGuid = server.ListClients().First().Guid;
                using MemoryStream ms = new MemoryStream(CreatePatternedPayload(256));
                await server.SendAsync(clientGuid, (int)ms.Length, ms);
                WaitForSignal(exceptionEncountered);
                await WaitForClientDisconnectedAsync(client, timeoutMs: 5000);

                TestAssert.Equal("client partial stream callback exploded", exceptionMessage);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ServerSyncStreamPartialReadThenReturnDrainsRemainder()
        {
            int port = GetNextPort();
            int invocation = 0;
            byte[] secondPayload = Encoding.UTF8.GetBytes("follow-up");
            byte[] secondReceived = null;
            var secondReceivedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Settings.MaxProxiedStreamSize = 64;
            server.Events.StreamReceived += (s, e) =>
            {
                int current = Interlocked.Increment(ref invocation);
                if (current == 1)
                {
                    byte[] partial = new byte[16];
                    int bytesRead = e.DataStream.Read(partial, 0, partial.Length);
                    TestAssert.Equal(16, bytesRead);
                    return;
                }

                using MemoryStream ms = new MemoryStream();
                e.DataStream.CopyTo(ms);
                secondReceived = ms.ToArray();
                secondReceivedEvent.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                using MemoryStream large = new MemoryStream(CreatePatternedPayload(512));
                await client.SendAsync((int)large.Length, large);

                using MemoryStream followUp = new MemoryStream(secondPayload);
                await client.SendAsync(secondPayload.Length, followUp);

                WaitForSignal(secondReceivedEvent);
                TestAssert.Equal(2, invocation);
                TestAssert.True(secondPayload.SequenceEqual(secondReceived), "Second sync stream payload should remain intact after remainder drain.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task ClientSyncStreamPartialReadThenReturnDrainsRemainder()
        {
            int port = GetNextPort();
            int invocation = 0;
            byte[] secondPayload = Encoding.UTF8.GetBytes("follow-up");
            byte[] secondReceived = null;
            var secondReceivedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.Settings.MaxProxiedStreamSize = 64;
            client.Events.StreamReceived += (s, e) =>
            {
                int current = Interlocked.Increment(ref invocation);
                if (current == 1)
                {
                    byte[] partial = new byte[16];
                    int bytesRead = e.DataStream.Read(partial, 0, partial.Length);
                    TestAssert.Equal(16, bytesRead);
                    return;
                }

                using MemoryStream ms = new MemoryStream();
                e.DataStream.CopyTo(ms);
                secondReceived = ms.ToArray();
                secondReceivedEvent.Set();
            };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                Guid clientGuid = server.ListClients().First().Guid;
                using MemoryStream large = new MemoryStream(CreatePatternedPayload(512));
                await server.SendAsync(clientGuid, (int)large.Length, large);

                using MemoryStream followUp = new MemoryStream(secondPayload);
                await server.SendAsync(clientGuid, secondPayload.Length, followUp);

                WaitForSignal(secondReceivedEvent);
                TestAssert.Equal(2, invocation);
                TestAssert.True(secondPayload.SequenceEqual(secondReceived), "Second sync stream payload should remain intact after remainder drain.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Statistics-Tests
        public static async Task ClientStatistics()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                long initialSent = client.Statistics.SentBytes;
                await client.SendAsync("Test message");
                await WaitForConditionAsync(() => client.Statistics.SentBytes > initialSent);
                TestAssert.True(client.Statistics.SentBytes > initialSent);

                long initialReceived = client.Statistics.ReceivedBytes;
                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, "Response");
                await WaitForConditionAsync(() => client.Statistics.ReceivedBytes > initialReceived);
                TestAssert.True(client.Statistics.ReceivedBytes > initialReceived);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task ServerStatistics()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            long initialReceived = server.Statistics.ReceivedBytes;
            long initialSent = server.Statistics.SentBytes;

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                await client.SendAsync("Client message");
                await WaitForConditionAsync(() => server.Statistics.ReceivedBytes > initialReceived);
                TestAssert.True(server.Statistics.ReceivedBytes > initialReceived);

                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, "Server message");
                await WaitForConditionAsync(() => server.Statistics.SentBytes > initialSent);
                TestAssert.True(server.Statistics.SentBytes > initialSent);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Multiple-Client-Tests
        public static async Task MultipleClients()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var clients = new List<WatsonTcpClient>();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var c = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(c);
                    c.Connect();
                    clients.Add(c);
                    await WaitForClientConnectedAsync(c, server, i + 1);
                }
                await WaitForServerClientCountAsync(server, 3);
                TestAssert.Equal(3, server.ListClients().Count());
            }
            finally
            {
                foreach (var c in clients) SafeDispose(c);
                SafeDispose(server);
            }
        }
        public static async Task ListClients()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                var clientList = server.ListClients().ToList();
                TestAssert.Single(clientList);
                TestAssert.NotEqual(Guid.Empty, clientList[0].Guid);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Disconnection-Tests
        public static async Task ClientDisconnect()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            TestAssert.True(client.Connected);
            client.Disconnect();
            await WaitForClientDisconnectedAsync(client);
            TestAssert.False(client.Connected);

            SafeDispose(client);
            SafeDispose(server);
        }
        public static async Task ServerDisconnectClient()
        {
            int port = GetNextPort();
            var disconnectEvent = new ManualResetEvent(false);
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Settings.IdleServerTimeoutMs = 500;
            client.Settings.IdleServerEvaluationIntervalMs = 100;
            client.Events.ServerDisconnected += (s, e) => disconnectEvent.Set();
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                var clients = server.ListClients().ToList();
                await server.DisconnectClientAsync(clients[0].Guid);
                try { await client.SendAsync("disconnect-check"); } catch { }
                WaitForSignal(disconnectEvent);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task ServerStop()
        {
            int port = GetNextPort();
            var disconnectEvent = new ManualResetEvent(false);
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Settings.IdleServerTimeoutMs = 500;
            client.Settings.IdleServerEvaluationIntervalMs = 100;
            client.Events.ServerDisconnected += (s, e) => disconnectEvent.Set();
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                SafeDispose(server);
                // Trigger disconnect detection by attempting a send
                try { await client.SendAsync("trigger"); } catch { }
                WaitForSignal(disconnectEvent, timeoutMs: 5000);
            }
            finally
            {
                SafeDispose(client);
            }
        }

        #endregion

        #region Large-Data-Tests
        public static async Task LargeMessageTransfer()
        {
            int port = GetNextPort();
            byte[] receivedData = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) => { receivedData = e.Data; messageReceived.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                byte[] data = new byte[1024 * 1024]; // 1MB
                new Random(42).NextBytes(data);
                await client.SendAsync(data);
                WaitForSignal(messageReceived, timeoutMs: 30000);
                TestAssert.Equal(data.Length, receivedData.Length);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task ManyMessages()
        {
            int port = GetNextPort();
            int count = 0;
            var allReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) => { if (Interlocked.Increment(ref count) >= 100) allReceived.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                for (int i = 0; i < 100; i++) await client.SendAsync("Message " + i);
                WaitForSignal(allReceived, timeoutMs: 30000);
                TestAssert.Equal(100, count);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Error-Condition-Tests
        public static async Task SendToNonExistentClient()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            try
            {
                await TestAssert.ThrowsAsync<KeyNotFoundException>(() => server.SendAsync(Guid.NewGuid(), "Hello"));
            }
            finally
            {
                SafeDispose(server);
            }
        }
        public static async Task ConnectToNonExistentServer()
        {
            var client = new WatsonTcpClient("10.1.2.3", 1234);
            SetupDefaultClientHandlers(client);
            client.Settings.ConnectTimeoutSeconds = 1;

            try
            {
                TestAssert.ThrowsAny<Exception>(() => client.Connect());
            }
            finally
            {
                SafeDispose(client);
            }
        }

        #endregion

        #region Concurrent-Tests
        public static async Task ConcurrentClientConnections()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            var clients = new List<WatsonTcpClient>();
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var c = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(c);
                    c.Connect();
                    clients.Add(c);
                    await WaitForClientConnectedAsync(c, server, i + 1);
                }
                await WaitForServerClientCountAsync(server, 5);
                TestAssert.Equal(5, server.ListClients().Count());
            }
            finally
            {
                foreach (var c in clients) SafeDispose(c);
                SafeDispose(server);
            }
        }
        public static async Task ConcurrentMessageSends()
        {
            int port = GetNextPort();
            int count = 0;
            var allReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.MessageReceived += (s, e) => { if (Interlocked.Increment(ref count) >= 10) allReceived.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                var tasks = Enumerable.Range(0, 10).Select(i => client.SendAsync("Msg " + i)).ToArray();
                await Task.WhenAll(tasks);
                WaitForSignal(allReceived, timeoutMs: 10000);
                TestAssert.Equal(10, count);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Client-GUID-Tests
        public static async Task SpecifyClientGuid()
        {
            int port = GetNextPort();
            Guid customGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
            Guid? serverSawGuid = null;
            var connectedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Events.ClientConnected += (s, e) => { serverSawGuid = e.Client.Guid; connectedEvent.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Settings.Guid = customGuid;
            client.Connect();

            try
            {
                WaitForSignal(connectedEvent);
                TestAssert.Equal(customGuid, serverSawGuid);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Idle-Timeout-Tests
        public static async Task IdleClientTimeout()
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
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                WaitForSignal(disconnectEvent, timeoutMs: 15000);
                TestAssert.True(timedOut);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Authentication-Tests
        public static async Task AuthenticationSuccess()
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
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.Events.MessageReceived += (s, e) => { };
            client.Settings.PresharedKey = presharedKey;
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                WaitForSignal(authEvent);
                TestAssert.True(authSucceeded);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task AuthenticationFailure()
        {
            int port = GetNextPort();
            bool authFailed = false;
            var authEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Events.MessageReceived += (s, e) => { };
            server.Settings.PresharedKey = "correctkey123456";
            server.Events.AuthenticationFailed += (s, e) => { authFailed = true; authEvent.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.Events.MessageReceived += (s, e) => { };
            client.Settings.PresharedKey = "wrongkey12345678";

            try
            {
                try
                {
                    client.Connect();
                }
                catch (ConnectionRejectedException)
                {
                }

                WaitForSignal(authEvent);
                TestAssert.True(authFailed);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task AuthenticationCallback()
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
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.Events.MessageReceived += (s, e) => { };
            client.Callbacks.AuthenticationRequested = () => { callbackCalled = true; return presharedKey; };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                WaitForSignal(authEvent);
                TestAssert.True(authSucceeded);
                TestAssert.True(callbackCalled);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Throughput-Tests
        public static async Task ThroughputSmallMessages()
        {
            await RunThroughputTest(messageSize: 64, messageCount: 2000);
        }
        public static async Task ThroughputMediumMessages()
        {
            await RunThroughputTest(messageSize: 65536, messageCount: 250);
        }
        public static async Task ThroughputLargeMessages()
        {
            await RunThroughputTest(messageSize: 4 * 1024 * 1024, messageCount: 10);
        }

        private static async Task RunThroughputTest(int messageSize, int messageCount)
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
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                byte[] data = new byte[messageSize];
                new Random(42).NextBytes(data);

                for (int i = 0; i < messageCount; i++)
                {
                    bool sent = await client.SendAsync(data);
                    TestAssert.True(sent, $"Send failed at message {i}");
                }

                int timeoutMs = Math.Max(15000, messageCount * 25);
                WaitForSignal(allReceived, timeoutMs, $"Only {receivedCount}/{messageCount} messages received within timeout.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region v6.1.0-MaxConnections-Tests
        public static async Task MaxConnectionsEnforced()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Settings.MaxConnections = 2;
            server.Settings.EnforceMaxConnections = true;
            server.Start();
            await WaitForServerListeningAsync(server);

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
                    await WaitForClientConnectedAsync(c, server, i + 1);
                }
                TestAssert.Equal(2, server.Connections);

                bool rejected = false;
                string rejectionReason = null;
                var rejectedEvent = new ManualResetEvent(false);
                try
                {
                    thirdClient = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(thirdClient);
                    thirdClient.Settings.ConnectTimeoutSeconds = 1;
                    thirdClient.Events.ConnectionRejected += (s, e) =>
                    {
                        rejectionReason = e.Reason;
                        rejectedEvent.Set();
                    };
                    thirdClient.Connect();
                    await WaitForConditionAsync(() => server.Connections <= 2, timeoutMs: 1000);
                    rejected = server.Connections <= 2;
                }
                catch (Exception ex)
                {
                    rejectionReason = ex.Message;
                    rejectedEvent.Set();
                    rejected = true;
                }

                if (!rejected)
                {
                    rejected = rejectedEvent.WaitOne(1000);
                }

                TestAssert.True(rejected);
                TestAssert.Equal(2, server.Connections);
            }
            finally
            {
                SafeDispose(thirdClient);
                foreach (var c in clients) SafeDispose(c);
                SafeDispose(server);
            }
        }
        public static async Task MaxConnectionsNotEnforced()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Settings.MaxConnections = 2;
            server.Settings.EnforceMaxConnections = false;
            server.Start();
            await WaitForServerListeningAsync(server);

            var clients = new List<WatsonTcpClient>();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var c = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(c);
                    c.Connect();
                    clients.Add(c);
                    await WaitForClientConnectedAsync(c, server, i + 1);
                }
                await WaitForConditionAsync(() => server.Connections >= 3);
                TestAssert.True(server.Connections >= 3);
            }
            finally
            {
                foreach (var c in clients) SafeDispose(c);
                SafeDispose(server);
            }
        }

        #endregion

        #region v6.1.0-MaxHeaderSize-Tests
        public static void MaxHeaderSizeSetting()
        {
            var serverSettings = new WatsonTcpServerSettings();
            serverSettings.MaxHeaderSize = 1024;
            TestAssert.Equal(1024, serverSettings.MaxHeaderSize);

            var clientSettings = new WatsonTcpClientSettings();
            clientSettings.MaxHeaderSize = 2048;
            TestAssert.Equal(2048, clientSettings.MaxHeaderSize);

            TestAssert.Throws<ArgumentException>(() => serverSettings.MaxHeaderSize = 10);
        }

        #endregion

        #region v6.1.0-Rapid-Connect-Disconnect
        public static async Task RapidConnectDisconnect()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var client = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(client);
                    client.Connect();
                    await WaitForClientConnectedAsync(client, server, 1);
                    SafeDispose(client);
                }
                await WaitForConditionAsync(() => server.IsListening);
                TestAssert.True(server.IsListening);
            }
            finally
            {
                SafeDispose(server);
            }
        }

        #endregion

        #region v6.1.0-Concurrent-Sync-Requests
        public static async Task ConcurrentSyncRequests()
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
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                var tasks = Enumerable.Range(0, 5)
                    .Select(i => client.SendAndWaitAsync(10000, "Request" + i))
                    .ToArray();
                var responses = await Task.WhenAll(tasks);

                var expected = Enumerable.Range(0, 5).Select(i => "Reply:Request" + i).ToHashSet();
                var actual = responses.Select(r => Encoding.UTF8.GetString(r.Data)).ToHashSet();
                TestAssert.True(expected.SetEquals(actual));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region v6.1.0-SSL-Tests
        public static async Task SslConnectivity()
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
            await WaitForServerListeningAsync(server, timeoutMs: 5000);

            var client = new WatsonTcpClient(_hostname, port, pfxFile, "password");
            SetupDefaultClientHandlers(client);
            client.Settings.AcceptInvalidCertificates = true;
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1, timeoutMs: 5000);

            try
            {
                TestAssert.True(client.Connected);
                TestAssert.Single(server.ListClients());
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }
        public static async Task SslMessageExchange()
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
            await WaitForServerListeningAsync(server, timeoutMs: 5000);

            var client = new WatsonTcpClient(_hostname, port, pfxFile, "password");
            SetupDefaultClientHandlers(client);
            client.Settings.AcceptInvalidCertificates = true;
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1, timeoutMs: 5000);

            try
            {
                await client.SendAsync("Hello over SSL!");
                WaitForSignal(messageReceived, timeoutMs: 5000);
                TestAssert.Equal("Hello over SSL!", receivedData);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region v6.1.0-Edge-Cases
        public static async Task DuplicateClientGuid()
        {
            int port = GetNextPort();
            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            Guid sharedGuid = Guid.NewGuid();
            var client1 = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client1);
            client1.Settings.Guid = sharedGuid;
            client1.Connect();
            await WaitForClientConnectedAsync(client1, server, 1);

            var client2 = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client2);
            client2.Settings.Guid = sharedGuid;
            client2.Connect();
            await WaitForConditionAsync(() => client1.Connected || client2.Connected, timeoutMs: 3000);

            try
            {
                TestAssert.True(client1.Connected || client2.Connected);
            }
            finally
            {
                SafeDispose(client1);
                SafeDispose(client2);
                SafeDispose(server);
            }
        }
        public static async Task SendWithOffset()
        {
            int port = GetNextPort();
            byte[] receivedBytes = null;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.Events.MessageReceived += (s, e) => { receivedBytes = e.Data; messageReceived.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                byte[] fullData = Encoding.UTF8.GetBytes("HEADERHello World");
                await client.SendAsync(fullData, null, 6); // skip "HEADER"
                WaitForSignal(messageReceived);
                TestAssert.Equal("Hello World", Encoding.UTF8.GetString(receivedBytes));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Authorization-And-Handshake

        public static async Task AuthorizeConnectionAllow()
        {
            int port = GetNextPort();
            bool authorizeCalled = false;

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.AuthorizeConnectionAsync = (ctx, token) =>
            {
                authorizeCalled = true;
                return Task.FromResult(ConnectionAuthorizationResult.Allow());
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                TestAssert.True(authorizeCalled, "AuthorizeConnectionAsync should be invoked.");
                TestAssert.True(client.Connected, "Client should remain connected after authorization.");
                TestAssert.Single(server.ListClients());
                TestAssert.Equal(0, server.PendingConnections);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AuthorizeConnectionReject()
        {
            int port = GetNextPort();
            string serverRejectionReason = null;
            var serverRejectedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.AuthorizeConnectionAsync = (ctx, token) =>
            {
                return Task.FromResult(ConnectionAuthorizationResult.Reject("Rejected by authorization callback."));
            };
            server.Events.ConnectionRejected += (s, e) =>
            {
                serverRejectionReason = e.Reason;
                serverRejectedEvent.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);

            try
            {
                try
                {
                    client.Connect();
                }
                catch
                {
                }

                WaitForSignal(serverRejectedEvent);
                await WaitForPendingConnectionsAsync(server, 0, timeoutMs: 5000);

                TestAssert.Equal("Rejected by authorization callback.", serverRejectionReason);
                TestAssert.Equal(0, server.ListClients().Count());
                TestAssert.Equal(0, server.PendingConnections);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AuthorizationStateMachineApiKeySuccess()
        {
            int port = GetNextPort();
            bool serverHandshakeSucceeded = false;
            bool clientHandshakeSucceeded = false;

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.HandshakeAsync = async (session, token) =>
            {
                HandshakeMessage message = await session.ReceiveAsync(token);
                string apiKey = Encoding.UTF8.GetString(message.Data ?? Array.Empty<byte>());
                if (apiKey == "valid-api-key-123")
                {
                    return HandshakeResult.Succeed();
                }

                return HandshakeResult.Fail("Invalid API key.");
            };
            server.Events.HandshakeSucceeded += (s, e) => serverHandshakeSucceeded = true;
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Callbacks.HandshakeAsync = async (session, token) =>
            {
                await session.SendAsync(new HandshakeMessage
                {
                    Type = "api-key",
                    Data = Encoding.UTF8.GetBytes("valid-api-key-123")
                }, token);
                return HandshakeResult.Succeed();
            };
            client.Events.HandshakeSucceeded += (s, e) => clientHandshakeSucceeded = true;

            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1, timeoutMs: 5000);

            try
            {
                TestAssert.True(client.Connected, "Client should connect after successful handshake.");
                TestAssert.True(serverHandshakeSucceeded, "Server should report handshake success.");
                TestAssert.True(clientHandshakeSucceeded, "Client should report handshake success.");
                TestAssert.Single(server.ListClients());
                TestAssert.Equal(0, server.PendingConnections);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AuthorizationStateMachineApiKeyFailure()
        {
            int port = GetNextPort();
            string failureReason = null;
            var handshakeFailedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.HandshakeAsync = async (session, token) =>
            {
                HandshakeMessage message = await session.ReceiveAsync(token);
                string apiKey = Encoding.UTF8.GetString(message.Data ?? Array.Empty<byte>());
                if (apiKey == "valid-api-key-123")
                {
                    return HandshakeResult.Succeed();
                }

                return HandshakeResult.Fail("Invalid API key.");
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Callbacks.HandshakeAsync = async (session, token) =>
            {
                await session.SendAsync(new HandshakeMessage
                {
                    Type = "api-key",
                    Data = Encoding.UTF8.GetBytes("invalid-api-key")
                }, token);
                return HandshakeResult.Succeed();
            };
            client.Events.HandshakeFailed += (s, e) =>
            {
                failureReason = e.Reason;
                handshakeFailedEvent.Set();
            };

            try
            {
                try
                {
                    client.Connect();
                }
                catch (HandshakeFailedException ex)
                {
                    failureReason = ex.Message;
                    handshakeFailedEvent.Set();
                }

                WaitForSignal(handshakeFailedEvent);
                await WaitForClientDisconnectedAsync(client);
                await WaitForPendingConnectionsAsync(server, 0);

                TestAssert.False(client.Connected, "Client should not connect after failed handshake.");
                TestAssert.Equal("Invalid API key.", failureReason);
                TestAssert.Equal(0, server.ListClients().Count());
                TestAssert.Equal(0, server.PendingConnections);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AuthorizationStateMachineChallengeResponseSuccess()
        {
            int port = GetNextPort();
            const string sharedSecret = "challenge-secret";
            string serverReceived = null;
            bool serverHandshakeSucceeded = false;
            bool clientHandshakeSucceeded = false;
            var messageReceived = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.HandshakeAsync = async (session, token) =>
            {
                string nonce = Guid.NewGuid().ToString("N");
                await session.SendAsync(new HandshakeMessage
                {
                    Type = "challenge",
                    Data = Encoding.UTF8.GetBytes(nonce)
                }, token);

                HandshakeMessage response = await session.ReceiveAsync(token);
                string proof = Encoding.UTF8.GetString(response?.Data ?? Array.Empty<byte>());
                string expected = ComputeChallengeResponseProof(sharedSecret, nonce);

                if (response?.Type != "proof")
                {
                    return HandshakeResult.Fail("Unexpected challenge response type.");
                }

                return proof == expected
                    ? HandshakeResult.Succeed()
                    : HandshakeResult.Fail("Invalid challenge response.");
            };
            server.Events.HandshakeSucceeded += (s, e) => serverHandshakeSucceeded = true;
            server.Events.MessageReceived += (s, e) =>
            {
                serverReceived = Encoding.UTF8.GetString(e.Data);
                messageReceived.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Callbacks.HandshakeAsync = async (session, token) =>
            {
                HandshakeMessage challenge = await session.ReceiveAsync(token);
                string nonce = Encoding.UTF8.GetString(challenge?.Data ?? Array.Empty<byte>());

                await session.SendAsync(new HandshakeMessage
                {
                    Type = "proof",
                    Data = Encoding.UTF8.GetBytes(ComputeChallengeResponseProof(sharedSecret, nonce))
                }, token);

                return HandshakeResult.Succeed();
            };
            client.Events.HandshakeSucceeded += (s, e) => clientHandshakeSucceeded = true;

            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1, timeoutMs: 5000);

            try
            {
                await client.SendAsync("after-handshake");
                WaitForSignal(messageReceived);

                TestAssert.True(serverHandshakeSucceeded, "Server should report handshake success.");
                TestAssert.True(clientHandshakeSucceeded, "Client should report handshake success.");
                TestAssert.Equal("after-handshake", serverReceived);
                TestAssert.Single(server.ListClients());
                TestAssert.Equal(0, server.PendingConnections);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AuthorizationStateMachineChallengeResponseFailure()
        {
            int port = GetNextPort();
            const string sharedSecret = "challenge-secret";
            string failureReason = null;
            var handshakeFailedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.HandshakeAsync = async (session, token) =>
            {
                string nonce = Guid.NewGuid().ToString("N");
                await session.SendAsync(new HandshakeMessage
                {
                    Type = "challenge",
                    Data = Encoding.UTF8.GetBytes(nonce)
                }, token);

                HandshakeMessage response = await session.ReceiveAsync(token);
                string proof = Encoding.UTF8.GetString(response?.Data ?? Array.Empty<byte>());
                string expected = ComputeChallengeResponseProof(sharedSecret, nonce);

                if (response?.Type != "proof")
                {
                    return HandshakeResult.Fail("Unexpected challenge response type.");
                }

                return proof == expected
                    ? HandshakeResult.Succeed()
                    : HandshakeResult.Fail("Invalid challenge response.");
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Callbacks.HandshakeAsync = async (session, token) =>
            {
                HandshakeMessage challenge = await session.ReceiveAsync(token);
                string nonce = Encoding.UTF8.GetString(challenge?.Data ?? Array.Empty<byte>());

                await session.SendAsync(new HandshakeMessage
                {
                    Type = "proof",
                    Data = Encoding.UTF8.GetBytes(ComputeChallengeResponseProof(sharedSecret, nonce + "-wrong"))
                }, token);

                return HandshakeResult.Succeed();
            };
            client.Events.HandshakeFailed += (s, e) =>
            {
                failureReason = e.Reason;
                handshakeFailedEvent.Set();
            };

            try
            {
                try
                {
                    client.Connect();
                }
                catch (HandshakeFailedException ex)
                {
                    failureReason = ex.Message;
                    handshakeFailedEvent.Set();
                }

                WaitForSignal(handshakeFailedEvent);
                await WaitForPendingConnectionsAsync(server, 0);

                TestAssert.Equal("Invalid challenge response.", failureReason);
                TestAssert.Equal(0, server.ListClients().Count());
                TestAssert.Equal(0, server.PendingConnections);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AuthorizeConnectionTimeoutReject()
        {
            int port = GetNextPort();
            string serverRejectionReason = null;
            var serverRejectedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Settings.AuthorizationTimeoutMs = 100;
            server.Callbacks.AuthorizeConnectionAsync = async (ctx, token) =>
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
                return ConnectionAuthorizationResult.Allow();
            };
            server.Events.ConnectionRejected += (s, e) =>
            {
                serverRejectionReason = e.Reason;
                serverRejectedEvent.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);

            try
            {
                try
                {
                    client.Connect();
                }
                catch
                {
                }

                WaitForSignal(serverRejectedEvent, timeoutMs: 5000);
                await WaitForPendingConnectionsAsync(server, 0, timeoutMs: 5000);

                TestAssert.Equal("Connection authorization timed out.", serverRejectionReason);
                TestAssert.Equal(0, server.ListClients().Count());
                TestAssert.Equal(0, server.PendingConnections);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AuthorizeConnectionExceptionReject()
        {
            int port = GetNextPort();
            string serverRejectionReason = null;
            var serverRejectedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.AuthorizeConnectionAsync = (ctx, token) => throw new InvalidOperationException("authorize exploded");
            server.Events.ConnectionRejected += (s, e) =>
            {
                serverRejectionReason = e.Reason;
                serverRejectedEvent.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);

            try
            {
                try
                {
                    client.Connect();
                }
                catch
                {
                }

                WaitForSignal(serverRejectedEvent, timeoutMs: 5000);
                await WaitForPendingConnectionsAsync(server, 0, timeoutMs: 5000);

                TestAssert.Equal("Connection authorization failed: authorize exploded", serverRejectionReason);
                TestAssert.Equal(0, server.ListClients().Count());
                TestAssert.Equal(0, server.PendingConnections);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static void AuthorizationAndHandshakeTimeoutSettings()
        {
            var serverSettings = new WatsonTcpServerSettings();
            serverSettings.AuthorizationTimeoutMs = 123;
            serverSettings.HandshakeTimeoutMs = 456;
            TestAssert.Equal(123, serverSettings.AuthorizationTimeoutMs);
            TestAssert.Equal(456, serverSettings.HandshakeTimeoutMs);

            var clientSettings = new WatsonTcpClientSettings();
            clientSettings.HandshakeTimeoutMs = 789;
            TestAssert.Equal(789, clientSettings.HandshakeTimeoutMs);

            TestAssert.Throws<ArgumentException>(() => serverSettings.AuthorizationTimeoutMs = 0);
            TestAssert.Throws<ArgumentException>(() => serverSettings.HandshakeTimeoutMs = 0);
            TestAssert.Throws<ArgumentException>(() => clientSettings.HandshakeTimeoutMs = 0);
        }

        public static async Task AuthorizationStateMachineMissingClientCallbackFailure()
        {
            int port = GetNextPort();
            string clientFailureReason = null;
            string serverFailureReason = null;
            var clientHandshakeFailedEvent = new ManualResetEvent(false);
            var serverHandshakeFailedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.HandshakeAsync = async (session, token) =>
            {
                HandshakeMessage message = await session.ReceiveAsync(token).ConfigureAwait(false);
                return message != null ? HandshakeResult.Succeed() : HandshakeResult.Fail("Handshake payload missing.");
            };
            server.Events.HandshakeFailed += (s, e) =>
            {
                serverFailureReason = e.Reason;
                serverHandshakeFailedEvent.Set();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Events.HandshakeFailed += (s, e) =>
            {
                clientFailureReason = e.Reason;
                clientHandshakeFailedEvent.Set();
            };

            try
            {
                try
                {
                    client.Connect();
                }
                catch (HandshakeFailedException ex)
                {
                    clientFailureReason = ex.Message;
                    clientHandshakeFailedEvent.Set();
                }

                WaitForSignal(clientHandshakeFailedEvent);
                WaitForSignal(serverHandshakeFailedEvent);
                await WaitForClientDisconnectedAsync(client);
                await WaitForPendingConnectionsAsync(server, 0);

                TestAssert.Equal("Server requested a handshake but no handshake callback is configured.", clientFailureReason);
                TestAssert.True(!String.IsNullOrEmpty(serverFailureReason));
                TestAssert.Equal(0, server.ListClients().Count());
                TestAssert.Equal(0, server.PendingConnections);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AuthorizationStateMachineTimeoutFailure()
        {
            int port = GetNextPort();
            string failureReason = null;
            var handshakeFailedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Settings.HandshakeTimeoutMs = 100;
            server.Callbacks.HandshakeAsync = async (session, token) =>
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
                return HandshakeResult.Succeed();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Callbacks.HandshakeAsync = async (session, token) =>
            {
                await session.SendAsync(new HandshakeMessage
                {
                    Type = "api-key",
                    Data = Encoding.UTF8.GetBytes("valid-api-key-123")
                }, token).ConfigureAwait(false);
                return HandshakeResult.Succeed();
            };
            client.Events.HandshakeFailed += (s, e) =>
            {
                failureReason = e.Reason;
                handshakeFailedEvent.Set();
            };

            try
            {
                try
                {
                    client.Connect();
                }
                catch (HandshakeFailedException ex)
                {
                    failureReason = ex.Message;
                    handshakeFailedEvent.Set();
                }

                WaitForSignal(handshakeFailedEvent);
                await WaitForClientDisconnectedAsync(client);
                await WaitForPendingConnectionsAsync(server, 0);

                TestAssert.Equal("Handshake timed out.", failureReason);
                TestAssert.Equal(0, server.ListClients().Count());
                TestAssert.Equal(0, server.PendingConnections);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AuthorizationStateMachineServerExceptionFailure()
        {
            int port = GetNextPort();
            string failureReason = null;
            var handshakeFailedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.HandshakeAsync = async (session, token) =>
            {
                await session.ReceiveAsync(token).ConfigureAwait(false);
                throw new InvalidOperationException("server handshake exploded");
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Callbacks.HandshakeAsync = async (session, token) =>
            {
                await session.SendAsync(new HandshakeMessage
                {
                    Type = "api-key",
                    Data = Encoding.UTF8.GetBytes("valid-api-key-123")
                }, token).ConfigureAwait(false);
                return HandshakeResult.Succeed();
            };
            client.Events.HandshakeFailed += (s, e) =>
            {
                failureReason = e.Reason;
                handshakeFailedEvent.Set();
            };

            try
            {
                try
                {
                    client.Connect();
                }
                catch (HandshakeFailedException ex)
                {
                    failureReason = ex.Message;
                    handshakeFailedEvent.Set();
                }

                WaitForSignal(handshakeFailedEvent, timeoutMs: 5000);
                await WaitForClientDisconnectedAsync(client, timeoutMs: 5000);
                await WaitForPendingConnectionsAsync(server, 0, timeoutMs: 5000);

                TestAssert.Equal("Handshake failed: server handshake exploded", failureReason);
                TestAssert.Equal(0, server.ListClients().Count());
                TestAssert.Equal(0, server.PendingConnections);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AuthorizationStateMachineClientFailure()
        {
            int port = GetNextPort();
            string clientFailureReason = null;
            var clientHandshakeFailedEvent = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            SetupDefaultServerHandlers(server);
            server.Callbacks.HandshakeAsync = async (session, token) => await session.ReceiveAsync(token).ConfigureAwait(false) != null
                ? HandshakeResult.Succeed()
                : HandshakeResult.Fail("Handshake payload missing.");
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Callbacks.HandshakeAsync = async (session, token) =>
            {
                await session.SendAsync(new HandshakeMessage
                {
                    Type = "api-key",
                    Data = Encoding.UTF8.GetBytes("valid-api-key-123")
                }, token).ConfigureAwait(false);
                return HandshakeResult.Fail("Client rejected handshake.");
            };
            client.Events.HandshakeFailed += (s, e) =>
            {
                clientFailureReason = e.Reason;
                clientHandshakeFailedEvent.Set();
            };

            try
            {
                try
                {
                    client.Connect();
                }
                catch (HandshakeFailedException ex)
                {
                    clientFailureReason = ex.Message;
                    clientHandshakeFailedEvent.Set();
                }

                WaitForSignal(clientHandshakeFailedEvent);
                await WaitForClientDisconnectedAsync(client);
                await WaitForPendingConnectionsAsync(server, 0);

                TestAssert.Equal("Client rejected handshake.", clientFailureReason);
                TestAssert.Equal(0, server.ListClients().Count());
                TestAssert.Equal(0, server.PendingConnections);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion
    }
}

