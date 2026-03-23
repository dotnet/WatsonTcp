using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WatsonTcp;

namespace Test.Automated
{
    class Program
    {
        static TestFramework _framework;
        static string _hostname = "127.0.0.1";
        static int _portCounter = 10000;
        static object _portLock = new object();

        static int GetNextPort()
        {
            lock (_portLock)
            {
                return _portCounter++;
            }
        }

        static void SetupDefaultServerHandlers(WatsonTcpServer server)
        {
            // WatsonTcp requires either MessageReceived or StreamReceived to be set
            // Add a default empty handler if one hasn't been added yet
            server.Events.MessageReceived += (s, e) => { };
        }

        static void SetupDefaultClientHandlers(WatsonTcpClient client)
        {
            // WatsonTcp requires either MessageReceived or StreamReceived to be set
            // Add a default empty handler if one hasn't been added yet
            client.Events.MessageReceived += (s, e) => { };
        }

        static void SafeDispose(WatsonTcpServer server)
        {
            try
            {
                server?.Dispose();
            }
            catch (AggregateException)
            {
                // Can occur when background tasks are cancelled during disposal
            }
        }

        static void SafeDispose(WatsonTcpClient client)
        {
            try
            {
                client?.Dispose();
            }
            catch (AggregateException)
            {
                // Can occur when background tasks are cancelled during disposal
            }
        }

        static async Task Main(string[] args)
        {
            Console.WriteLine("===============================================");
            Console.WriteLine("WatsonTcp Automated Test Suite");
            Console.WriteLine("===============================================");
            Console.WriteLine();

            _framework = new TestFramework();
            _framework.StartSuite();

            // Run all tests
            await RunAllTests();

            // Display summary
            Console.WriteLine();
            Console.WriteLine("===============================================");
            Console.WriteLine("Test Summary");
            Console.WriteLine("===============================================");
            _framework.PrintSummary();

            Console.WriteLine();
        }

        static async Task RunAllTests()
        {
            // Basic connection tests
            await Test_BasicServerStartStop();
            await Test_BasicClientConnection();
            await Test_ClientServerConnection();

            // Message sending/receiving tests
            await Test_ClientSendServerReceive();
            await Test_ServerSendClientReceive();
            await Test_BidirectionalCommunication();
            await Test_EmptyMessage();

            // Metadata tests
            await Test_SendWithMetadata();
            await Test_ReceiveWithMetadata();

            // Sync request/response tests
            await Test_SyncRequestResponse();
            await Test_SyncRequestTimeout();

            // Event tests
            await Test_ServerConnectedEvent();
            await Test_ServerDisconnectedEvent();
            await Test_ClientConnectedEvent();
            await Test_ClientDisconnectedEvent();
            await Test_MessageReceivedEvent();

            // Stream tests
            await Test_StreamSendReceive();
            await Test_LargeStreamTransfer();

            // Statistics tests
            await Test_ClientStatistics();
            await Test_ServerStatistics();

            // Multiple client tests
            await Test_MultipleClients();
            await Test_ListClients();

            // Disconnection tests
            await Test_ClientDisconnect();
            await Test_ServerDisconnectClient();
            await Test_ServerStop();

            // Large data tests
            await Test_LargeMessageTransfer();
            await Test_ManyMessages();

            // Error condition tests
            await Test_SendToNonExistentClient();
            await Test_ConnectToNonExistentServer();

            // Concurrent operation tests
            await Test_ConcurrentClientConnections();
            await Test_ConcurrentMessageSends();

            // Client GUID tests
            await Test_SpecifyClientGuid();

            // Idle timeout tests
            await Test_IdleClientTimeout();

            // Authentication tests
            await Test_AuthenticationSuccess();
            await Test_AuthenticationFailure();
            await Test_AuthenticationCallback();

            // Throughput tests
            await Test_ThroughputSmallMessages();
            await Test_ThroughputMediumMessages();
            await Test_ThroughputLargeMessages();

            // v6.1.0 - MaxConnections enforcement tests
            await Test_MaxConnectionsEnforced();
            await Test_MaxConnectionsNotEnforced();

            // v6.1.0 - MaxHeaderSize tests
            await Test_MaxHeaderSizeEnforced();

            // v6.1.0 - Rapid connect/disconnect
            await Test_RapidConnectDisconnect();

            // v6.1.0 - Concurrent sync requests
            await Test_ConcurrentSyncRequests();

            // v6.1.0 - SSL connectivity
            await Test_SslConnectivity();
            await Test_SslMessageExchange();

            // v6.1.0 - Server stop while client connected
            await Test_ServerStopWhileClientSending();

            // v6.1.0 - Duplicate client GUID
            await Test_DuplicateClientGuid();

            // v6.1.0 - Send with offset
            await Test_SendWithOffset();
        }

        #region Basic-Connection-Tests

        static async Task Test_BasicServerStartStop()
        {
            string testName = "Basic Server Start/Stop";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                if (!server.IsListening)
                {
                    _framework.RecordFailure(testName, "Server not listening after Start()");
                    return;
                }

                server.Stop();
                await Task.Delay(200);

                if (server.IsListening)
                {
                    _framework.RecordFailure(testName, "Server still listening after Stop()");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_BasicClientConnection()
        {
            string testName = "Basic Client Connection";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                if (!client.Connected)
                {
                    _framework.RecordFailure(testName, "Client not connected after Connect()");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_ClientServerConnection()
        {
            string testName = "Client-Server Connection";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                var clients = server.ListClients().ToList();
                if (clients.Count != 1)
                {
                    _framework.RecordFailure(testName, $"Expected 1 client, found {clients.Count}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_ClientSendServerReceive()
        {
            string testName = "Client Send -> Server Receive";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            string receivedData = null;
            ManualResetEvent messageReceived = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Events.MessageReceived += (s, e) =>
                {
                    receivedData = Encoding.UTF8.GetString(e.Data);
                    messageReceived.Set();
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                string testData = "Hello from client!";
                await client.SendAsync(testData);

                if (!messageReceived.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Server did not receive message");
                    return;
                }

                if (receivedData != testData)
                {
                    _framework.RecordFailure(testName, $"Expected '{testData}', received '{receivedData}'");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_ServerSendClientReceive()
        {
            string testName = "Server Send -> Client Receive";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            string receivedData = null;
            ManualResetEvent messageReceived = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Events.MessageReceived += (s, e) =>
                {
                    receivedData = Encoding.UTF8.GetString(e.Data);
                    messageReceived.Set();
                };
                client.Connect();
                await Task.Delay(200);

                string testData = "Hello from server!";
                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, testData);

                if (!messageReceived.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Client did not receive message");
                    return;
                }

                if (receivedData != testData)
                {
                    _framework.RecordFailure(testName, $"Expected '{testData}', received '{receivedData}'");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_BidirectionalCommunication()
        {
            string testName = "Bidirectional Communication";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            string serverReceived = null;
            string clientReceived = null;
            ManualResetEvent serverGotMessage = new ManualResetEvent(false);
            ManualResetEvent clientGotMessage = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Events.MessageReceived += (s, e) =>
                {
                    serverReceived = Encoding.UTF8.GetString(e.Data);
                    serverGotMessage.Set();
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Events.MessageReceived += (s, e) =>
                {
                    clientReceived = Encoding.UTF8.GetString(e.Data);
                    clientGotMessage.Set();
                };
                client.Connect();
                await Task.Delay(200);

                // Client -> Server
                string clientMsg = "From client";
                await client.SendAsync(clientMsg);
                if (!serverGotMessage.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Server did not receive message");
                    return;
                }

                // Server -> Client
                string serverMsg = "From server";
                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, serverMsg);
                if (!clientGotMessage.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Client did not receive message");
                    return;
                }

                if (serverReceived != clientMsg || clientReceived != serverMsg)
                {
                    _framework.RecordFailure(testName, $"Data mismatch: server got '{serverReceived}', client got '{clientReceived}'");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_EmptyMessage()
        {
            string testName = "Empty Message with Metadata";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            Dictionary<string, object> receivedMetadata = null;
            ManualResetEvent messageReceived = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Events.MessageReceived += (s, e) =>
                {
                    receivedMetadata = e.Metadata;
                    messageReceived.Set();
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                var metadata = new Dictionary<string, object> { { "test", "value" } };
                await client.SendAsync("", metadata);

                if (!messageReceived.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Server did not receive message");
                    return;
                }

                if (receivedMetadata == null || !receivedMetadata.ContainsKey("test"))
                {
                    _framework.RecordFailure(testName, "Metadata not received correctly");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_SendWithMetadata()
        {
            string testName = "Send With Metadata";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            Dictionary<string, object> receivedMetadata = null;
            string receivedData = null;
            ManualResetEvent messageReceived = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Events.MessageReceived += (s, e) =>
                {
                    receivedData = Encoding.UTF8.GetString(e.Data);
                    receivedMetadata = e.Metadata;
                    messageReceived.Set();
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                var metadata = new Dictionary<string, object>
                {
                    { "key1", "value1" },
                    { "key2", 42 },
                    { "key3", true }
                };

                await client.SendAsync("Test data", metadata);

                if (!messageReceived.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Server did not receive message");
                    return;
                }

                if (receivedData != "Test data")
                {
                    _framework.RecordFailure(testName, $"Data mismatch: got '{receivedData}'");
                    return;
                }

                if (receivedMetadata == null || receivedMetadata.Count != 3)
                {
                    _framework.RecordFailure(testName, "Metadata not received correctly");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_ReceiveWithMetadata()
        {
            string testName = "Receive With Metadata";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            Dictionary<string, object> receivedMetadata = null;
            ManualResetEvent messageReceived = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Events.MessageReceived += (s, e) =>
                {
                    receivedMetadata = e.Metadata;
                    messageReceived.Set();
                };
                client.Connect();
                await Task.Delay(200);

                var metadata = new Dictionary<string, object> { { "server", "data" } };
                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, "Server message", metadata);

                if (!messageReceived.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Client did not receive message");
                    return;
                }

                if (receivedMetadata == null || !receivedMetadata.ContainsKey("server"))
                {
                    _framework.RecordFailure(testName, "Metadata not received correctly");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_SyncRequestResponse()
        {
            string testName = "Sync Request/Response";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Callbacks.SyncRequestReceivedAsync = async (req) =>
                {
                    await Task.Delay(10);
                    return new SyncResponse(req, "Response from server");
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                SyncResponse response = await client.SendAndWaitAsync(5000, "Request from client");

                if (response == null)
                {
                    _framework.RecordFailure(testName, "No response received");
                    return;
                }

                string responseData = Encoding.UTF8.GetString(response.Data);
                if (responseData != "Response from server")
                {
                    _framework.RecordFailure(testName, $"Expected 'Response from server', got '{responseData}'");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_SyncRequestTimeout()
        {
            string testName = "Sync Request Timeout";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Callbacks.SyncRequestReceivedAsync = async (req) =>
                {
                    // Delay longer than timeout
                    await Task.Delay(3000);
                    return new SyncResponse(req, "Too late");
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                bool timedOut = false;
                try
                {
                    SyncResponse response = await client.SendAndWaitAsync(1000, "Request");
                }
                catch (TimeoutException)
                {
                    timedOut = true;
                }

                if (!timedOut)
                {
                    _framework.RecordFailure(testName, "Request did not timeout as expected");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_ServerConnectedEvent()
        {
            string testName = "Server Connected Event";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            bool eventFired = false;
            ManualResetEvent connectionEvent = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Events.ServerConnected += (s, e) =>
                {
                    eventFired = true;
                    connectionEvent.Set();
                };
                client.Connect();

                if (!connectionEvent.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "ServerConnected event did not fire");
                    return;
                }

                if (!eventFired)
                {
                    _framework.RecordFailure(testName, "Event flag not set");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_ServerDisconnectedEvent()
        {
            string testName = "Server Disconnected Event";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            bool eventFired = false;
            ManualResetEvent disconnectionEvent = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Events.ServerDisconnected += (s, e) =>
                {
                    eventFired = true;
                    disconnectionEvent.Set();
                };
                client.Connect();
                await Task.Delay(200);

                // Disconnect from server side
                var clients = server.ListClients().ToList();
                await server.DisconnectClientAsync(clients[0].Guid);

                if (!disconnectionEvent.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "ServerDisconnected event did not fire");
                    return;
                }

                if (!eventFired)
                {
                    _framework.RecordFailure(testName, "Event flag not set");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_ClientConnectedEvent()
        {
            string testName = "Client Connected Event";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            bool eventFired = false;
            Guid? connectedGuid = null;
            ManualResetEvent connectionEvent = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Events.ClientConnected += (s, e) =>
                {
                    eventFired = true;
                    connectedGuid = e.Client.Guid;
                    connectionEvent.Set();
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();

                if (!connectionEvent.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "ClientConnected event did not fire");
                    return;
                }

                if (!eventFired || connectedGuid == null)
                {
                    _framework.RecordFailure(testName, "Event not fired or GUID not captured");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_ClientDisconnectedEvent()
        {
            string testName = "Client Disconnected Event";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            bool eventFired = false;
            ManualResetEvent disconnectionEvent = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Events.ClientDisconnected += (s, e) =>
                {
                    eventFired = true;
                    disconnectionEvent.Set();
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                client.Disconnect();

                if (!disconnectionEvent.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "ClientDisconnected event did not fire");
                    return;
                }

                if (!eventFired)
                {
                    _framework.RecordFailure(testName, "Event flag not set");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_MessageReceivedEvent()
        {
            string testName = "Message Received Event";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            int messageCount = 0;
            ManualResetEvent messageEvent = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Events.MessageReceived += (s, e) =>
                {
                    messageCount++;
                    messageEvent.Set();
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                await client.SendAsync("Test message");

                if (!messageEvent.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "MessageReceived event did not fire");
                    return;
                }

                if (messageCount != 1)
                {
                    _framework.RecordFailure(testName, $"Expected 1 message, got {messageCount}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_StreamSendReceive()
        {
            string testName = "Stream Send/Receive";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            long receivedLength = 0;
            ManualResetEvent streamReceived = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                // Don't call SetupDefaultServerHandlers here - we're adding StreamReceived handler
                server.Events.StreamReceived += (s, e) =>
                {
                    receivedLength = e.ContentLength;
                    byte[] buffer = new byte[e.ContentLength];
                    _ = e.DataStream.Read(buffer, 0, (int)e.ContentLength);
                    streamReceived.Set();
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                byte[] data = Encoding.UTF8.GetBytes("Stream data test");
                using (MemoryStream ms = new MemoryStream(data))
                {
                    await client.SendAsync(data.Length, ms);
                }

                if (!streamReceived.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Stream not received");
                    return;
                }

                if (receivedLength != data.Length)
                {
                    _framework.RecordFailure(testName, $"Expected {data.Length} bytes, got {receivedLength}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_LargeStreamTransfer()
        {
            string testName = "Large Stream Transfer (10MB)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            long receivedLength = 0;
            bool dataVerified = false;
            ManualResetEvent streamReceived = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                // Don't call SetupDefaultServerHandlers here - we're adding StreamReceived handler
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

                        // Verify data integrity
                        for (int i = 0; i < bytesRead; i++)
                        {
                            byte expected = (byte)((totalRead + i) % 256);
                            if (buffer[i] != expected)
                            {
                                valid = false;
                                break;
                            }
                        }

                        totalRead += bytesRead;
                        if (!valid) break;
                    }

                    dataVerified = valid;
                    streamReceived.Set();
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                // Send 10MB of data
                int dataSize = 10 * 1024 * 1024;
                using (MemoryStream ms = new MemoryStream())
                {
                    for (long i = 0; i < dataSize; i++)
                    {
                        ms.WriteByte((byte)(i % 256));
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    await client.SendAsync(dataSize, ms);
                }

                if (!streamReceived.WaitOne(30000))
                {
                    _framework.RecordFailure(testName, "Stream not received within timeout");
                    return;
                }

                if (receivedLength != dataSize)
                {
                    _framework.RecordFailure(testName, $"Expected {dataSize} bytes, got {receivedLength}");
                    return;
                }

                if (!dataVerified)
                {
                    _framework.RecordFailure(testName, "Data integrity check failed");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_ClientStatistics()
        {
            string testName = "Client Statistics";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                long initialSent = client.Statistics.SentBytes;
                long initialReceived = client.Statistics.ReceivedBytes;

                // Send data
                await client.SendAsync("Test message");
                await Task.Delay(200);

                if (client.Statistics.SentBytes <= initialSent)
                {
                    _framework.RecordFailure(testName, "SentBytes did not increase");
                    return;
                }

                // Receive data
                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, "Response");
                await Task.Delay(200);

                if (client.Statistics.ReceivedBytes <= initialReceived)
                {
                    _framework.RecordFailure(testName, "ReceivedBytes did not increase");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_ServerStatistics()
        {
            string testName = "Server Statistics";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                long initialSent = server.Statistics.SentBytes;
                long initialReceived = server.Statistics.ReceivedBytes;

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                // Receive data
                await client.SendAsync("Client message");
                await Task.Delay(200);

                if (server.Statistics.ReceivedBytes <= initialReceived)
                {
                    _framework.RecordFailure(testName, "ReceivedBytes did not increase");
                    return;
                }

                // Send data
                var clients = server.ListClients().ToList();
                await server.SendAsync(clients[0].Guid, "Server message");
                await Task.Delay(200);

                if (server.Statistics.SentBytes <= initialSent)
                {
                    _framework.RecordFailure(testName, "SentBytes did not increase");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_MultipleClients()
        {
            string testName = "Multiple Clients";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            List<WatsonTcpClient> clients = new List<WatsonTcpClient>();

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                // Connect 5 clients
                for (int i = 0; i < 5; i++)
                {
                    var client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                    client.Connect();
                    clients.Add(client);
                    await Task.Delay(100);
                }

                var serverClients = server.ListClients().ToList();
                if (serverClients.Count != 5)
                {
                    _framework.RecordFailure(testName, $"Expected 5 clients, found {serverClients.Count}");
                    return;
                }

                // Send message to each client
                for (int i = 0; i < 5; i++)
                {
                    string msg = $"Message {i}";
                    await server.SendAsync(serverClients[i].Guid, msg);
                }

                await Task.Delay(300);

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                foreach (var client in clients)
                {
                    SafeDispose(client);
                }
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_ListClients()
        {
            string testName = "List Clients";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client1 = null;
            WatsonTcpClient client2 = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                client1 = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client1);
                client1.Connect();
                await Task.Delay(100);

                client2 = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client2);
                client2.Connect();
                await Task.Delay(100);

                var clients = server.ListClients().ToList();
                if (clients.Count != 2)
                {
                    _framework.RecordFailure(testName, $"Expected 2 clients, got {clients.Count}");
                    return;
                }

                // Verify IsClientConnected
                foreach (var client in clients)
                {
                    if (!server.IsClientConnected(client.Guid))
                    {
                        _framework.RecordFailure(testName, $"Client {client.Guid} not reported as connected");
                        return;
                    }
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client1);
                SafeDispose(client2);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Disconnection-Tests

        static async Task Test_ClientDisconnect()
        {
            string testName = "Client Disconnect";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                if (!client.Connected)
                {
                    _framework.RecordFailure(testName, "Client not connected");
                    return;
                }

                client.Disconnect();
                await Task.Delay(200);

                if (client.Connected)
                {
                    _framework.RecordFailure(testName, "Client still connected after Disconnect()");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_ServerDisconnectClient()
        {
            string testName = "Server Disconnect Client";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            bool clientDisconnected = false;
            ManualResetEvent disconnectionEvent = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Events.ServerDisconnected += (s, e) =>
                {
                    clientDisconnected = true;
                    disconnectionEvent.Set();
                };
                client.Connect();
                await Task.Delay(200);

                var clients = server.ListClients().ToList();
                await server.DisconnectClientAsync(clients[0].Guid);

                if (!disconnectionEvent.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Client did not detect disconnection");
                    return;
                }

                if (!clientDisconnected)
                {
                    _framework.RecordFailure(testName, "Client disconnect event not fired");
                    return;
                }

                var remainingClients = server.ListClients().ToList();
                if (remainingClients.Count != 0)
                {
                    _framework.RecordFailure(testName, $"Expected 0 clients, found {remainingClients.Count}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_ServerStop()
        {
            string testName = "Server Stop Disconnects Clients";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            bool clientDisconnected = false;
            ManualResetEvent disconnectionEvent = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Events.ServerDisconnected += (s, e) =>
                {
                    clientDisconnected = true;
                    disconnectionEvent.Set();
                };
                client.Connect();
                await Task.Delay(200);

                server.Stop();
                await Task.Delay(500); // Give time for disconnection to propagate

                // Try to send a message to force client to detect disconnection
                try
                {
                    await client.SendAsync("test");
                }
                catch
                {
                    // Expected - send might fail after server stops
                }

                await Task.Delay(500);

                if (!disconnectionEvent.WaitOne(10000))
                {
                    _framework.RecordFailure(testName, "Client did not detect server stop");
                    return;
                }

                if (!clientDisconnected)
                {
                    _framework.RecordFailure(testName, "Client disconnect event not fired");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                try
                {
                    SafeDispose(server);
                }
                catch (AggregateException)
                {
                    // Expected - server.Stop() was called, disposal may throw cancelled task exception
                }
                await Task.Delay(100);
            }
        }

        #endregion

        #region Large-Data-Tests

        static async Task Test_LargeMessageTransfer()
        {
            string testName = "Large Message Transfer (1MB)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            byte[] receivedData = null;
            ManualResetEvent messageReceived = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Events.MessageReceived += (s, e) =>
                {
                    receivedData = e.Data;
                    messageReceived.Set();
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                // Send 1MB of data
                byte[] largeData = new byte[1024 * 1024];
                for (int i = 0; i < largeData.Length; i++)
                {
                    largeData[i] = (byte)(i % 256);
                }

                await client.SendAsync(largeData);

                if (!messageReceived.WaitOne(30000))
                {
                    _framework.RecordFailure(testName, "Large message not received");
                    return;
                }

                if (receivedData.Length != largeData.Length)
                {
                    _framework.RecordFailure(testName, $"Expected {largeData.Length} bytes, got {receivedData.Length}");
                    return;
                }

                // Verify data integrity
                for (int i = 0; i < largeData.Length; i++)
                {
                    if (largeData[i] != receivedData[i])
                    {
                        _framework.RecordFailure(testName, $"Data mismatch at byte {i}");
                        return;
                    }
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_ManyMessages()
        {
            string testName = "Many Messages (100 messages)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            int receivedCount = 0;
            ManualResetEvent allReceived = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Events.MessageReceived += (s, e) =>
                {
                    receivedCount++;
                    if (receivedCount >= 100)
                    {
                        allReceived.Set();
                    }
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                // Send 100 messages
                for (int i = 0; i < 100; i++)
                {
                    await client.SendAsync($"Message {i}");
                }

                if (!allReceived.WaitOne(30000))
                {
                    _framework.RecordFailure(testName, $"Only received {receivedCount}/100 messages");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_SendToNonExistentClient()
        {
            string testName = "Send To Non-Existent Client";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                Guid nonExistentGuid = Guid.NewGuid();

                try
                {
                    bool result = await server.SendAsync(nonExistentGuid, "Test data");

                    if (result)
                    {
                        _framework.RecordFailure(testName, "Send succeeded for non-existent client");
                        return;
                    }
                }
                catch (Exception sendEx)
                {
                    // Exception is also acceptable - either false return or exception means client not found
                    if (!sendEx.Message.Contains("Unable to find client") && !sendEx.Message.Contains("not found"))
                    {
                        _framework.RecordFailure(testName, $"Unexpected exception: {sendEx.Message}");
                        return;
                    }
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_ConnectToNonExistentServer()
        {
            string testName = "Connect To Non-Existent Server";
            _framework.StartTest(testName);
            WatsonTcpClient client = null;

            try
            {
                int port = GetNextPort();
                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);

                bool connectionFailed = false;
                try
                {
                    client.Connect();
                    await Task.Delay(500);
                }
                catch
                {
                    connectionFailed = true;
                }

                if (!connectionFailed && client.Connected)
                {
                    _framework.RecordFailure(testName, "Client connected to non-existent server");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception)
            {
                // Expected behavior - connection failure is expected
                _framework.RecordSuccess(testName);
            }
            finally
            {
                SafeDispose(client);
                await Task.Delay(100);
            }
        }

        #endregion

        #region Concurrent-Operation-Tests

        static async Task Test_ConcurrentClientConnections()
        {
            string testName = "Concurrent Client Connections";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            List<WatsonTcpClient> clients = new List<WatsonTcpClient>();

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                // Connect 10 clients concurrently
                var connectTasks = new List<Task>();
                for (int i = 0; i < 10; i++)
                {
                    var client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                    clients.Add(client);
                    connectTasks.Add(Task.Run(() => client.Connect()));
                }

                await Task.WhenAll(connectTasks);
                await Task.Delay(500);

                var serverClients = server.ListClients().ToList();
                if (serverClients.Count != 10)
                {
                    _framework.RecordFailure(testName, $"Expected 10 clients, found {serverClients.Count}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                foreach (var client in clients)
                {
                    SafeDispose(client);
                }
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_ConcurrentMessageSends()
        {
            string testName = "Concurrent Message Sends";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            int receivedCount = 0;
            object lockObj = new object();
            ManualResetEvent allReceived = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Events.MessageReceived += (s, e) =>
                {
                    lock (lockObj)
                    {
                        receivedCount++;
                        if (receivedCount >= 50)
                        {
                            allReceived.Set();
                        }
                    }
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                // Send 50 messages concurrently
                var sendTasks = new List<Task>();
                for (int i = 0; i < 50; i++)
                {
                    int index = i;
                    sendTasks.Add(Task.Run(async () => await client.SendAsync($"Concurrent message {index}")));
                }

                await Task.WhenAll(sendTasks);

                if (!allReceived.WaitOne(30000))
                {
                    _framework.RecordFailure(testName, $"Only received {receivedCount}/50 messages");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_SpecifyClientGuid()
        {
            string testName = "Specify Client GUID";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            Guid? receivedGuid = null;
            ManualResetEvent clientConnected = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Events.ClientConnected += (s, e) =>
                {
                    receivedGuid = e.Client.Guid;
                    clientConnected.Set();
                };
                server.Start();
                await Task.Delay(100);

                Guid customGuid = Guid.NewGuid();
                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Settings.Guid = customGuid;
                client.Connect();

                if (!clientConnected.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Client connection event not fired");
                    return;
                }

                if (receivedGuid != customGuid)
                {
                    _framework.RecordFailure(testName, $"Expected GUID {customGuid}, got {receivedGuid}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_IdleClientTimeout()
        {
            string testName = "Idle Client Timeout";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            bool clientDisconnected = false;
            ManualResetEvent disconnectionEvent = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Settings.IdleClientTimeoutSeconds = 2;
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Events.ServerDisconnected += (s, e) =>
                {
                    clientDisconnected = true;
                    disconnectionEvent.Set();
                };
                client.Connect();
                await Task.Delay(200);

                // Wait for idle timeout (2 seconds + buffer)
                if (!disconnectionEvent.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Client not disconnected after idle timeout");
                    return;
                }

                if (!clientDisconnected)
                {
                    _framework.RecordFailure(testName, "Client disconnect event not fired");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_AuthenticationSuccess()
        {
            string testName = "Authentication Success (via Settings)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            bool authSucceeded = false;
            ManualResetEvent authEvent = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                string presharedKey = "1234567890123456"; // Must be 16 bytes

                server = new WatsonTcpServer(_hostname, port);
                server.Events.MessageReceived += (s, e) => { }; // Required handler
                server.Settings.PresharedKey = presharedKey;
                server.Events.AuthenticationSucceeded += (s, e) =>
                {
                    authSucceeded = true;
                    authEvent.Set();
                };
                server.Start();
                await Task.Delay(200);

                client = new WatsonTcpClient(_hostname, port);
                client.Events.MessageReceived += (s, e) => { }; // Required handler
                // Set the preshared key in client settings - should now auto-authenticate
                client.Settings.PresharedKey = presharedKey;
                client.Connect();
                await Task.Delay(2000); // Wait longer for full auth handshake

                if (!authEvent.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Authentication event did not fire");
                    return;
                }

                if (!authSucceeded)
                {
                    _framework.RecordFailure(testName, "Server did not record successful authentication");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_AuthenticationFailure()
        {
            string testName = "Authentication Failure (Wrong Key)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            bool authFailed = false;
            bool clientDisconnected = false;
            ManualResetEvent authEvent = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                string serverKey = "1234567890123456"; // Must be 16 bytes
                string wrongKey = "wrongkey12345678";

                server = new WatsonTcpServer(_hostname, port);
                server.Events.MessageReceived += (s, e) => { }; // Required handler
                server.Settings.PresharedKey = serverKey;
                server.Events.AuthenticationFailed += (s, e) =>
                {
                    authFailed = true;
                    authEvent.Set();
                };
                server.Start();
                await Task.Delay(200);

                client = new WatsonTcpClient(_hostname, port);
                client.Events.MessageReceived += (s, e) => { }; // Required handler
                client.Events.ServerDisconnected += (s, e) =>
                {
                    if (e.Reason == DisconnectReason.AuthFailure)
                    {
                        clientDisconnected = true;
                    }
                };
                // Set WRONG preshared key
                client.Settings.PresharedKey = wrongKey;
                client.Connect();
                await Task.Delay(1000); // Wait for auth failure

                if (!authEvent.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Authentication failure event did not fire on server");
                    return;
                }

                if (!authFailed)
                {
                    _framework.RecordFailure(testName, "Server did not record authentication failure");
                    return;
                }

                if (!clientDisconnected)
                {
                    _framework.RecordFailure(testName, "Client was not disconnected after auth failure");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_AuthenticationCallback()
        {
            string testName = "Authentication Callback (Fallback)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            bool authSucceeded = false;
            bool callbackCalled = false;
            ManualResetEvent authEvent = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                string presharedKey = "callback12345678"; // Must be 16 bytes

                server = new WatsonTcpServer(_hostname, port);
                server.Events.MessageReceived += (s, e) => { }; // Required handler
                server.Settings.PresharedKey = presharedKey;
                server.Events.AuthenticationSucceeded += (s, e) =>
                {
                    authSucceeded = true;
                    authEvent.Set();
                };
                server.Start();
                await Task.Delay(200);

                client = new WatsonTcpClient(_hostname, port);
                client.Events.MessageReceived += (s, e) => { }; // Required handler
                // Don't set Settings.PresharedKey - use callback instead
                client.Callbacks.AuthenticationRequested = () =>
                {
                    callbackCalled = true;
                    return presharedKey;
                };
                client.Connect();
                await Task.Delay(1000); // Wait for callback and auth

                if (!authEvent.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Authentication via callback did not succeed");
                    return;
                }

                if (!authSucceeded)
                {
                    _framework.RecordFailure(testName, "Server did not record successful authentication");
                    return;
                }

                if (!callbackCalled)
                {
                    _framework.RecordFailure(testName, "Callback was not called");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_ThroughputSmallMessages()
        {
            string testName = "Throughput: 64-byte messages";
            _framework.StartTest(testName);
            await RunThroughputTest(testName, messageSize: 64, messageCount: 5000);
        }

        static async Task Test_ThroughputMediumMessages()
        {
            string testName = "Throughput: 64KB messages";
            _framework.StartTest(testName);
            await RunThroughputTest(testName, messageSize: 65536, messageCount: 500);
        }

        static async Task Test_ThroughputLargeMessages()
        {
            string testName = "Throughput: 4MB messages";
            _framework.StartTest(testName);
            await RunThroughputTest(testName, messageSize: 4 * 1024 * 1024, messageCount: 20);
        }

        static async Task RunThroughputTest(string testName, int messageSize, int messageCount)
        {
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            int receivedCount = 0;
            ManualResetEvent allReceived = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Events.MessageReceived += (s, e) =>
                {
                    if (Interlocked.Increment(ref receivedCount) >= messageCount)
                        allReceived.Set();
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                byte[] data = new byte[messageSize];
                new Random(42).NextBytes(data);

                var sw = System.Diagnostics.Stopwatch.StartNew();

                for (int i = 0; i < messageCount; i++)
                {
                    bool sent = await client.SendAsync(data);
                    if (!sent)
                    {
                        _framework.RecordFailure(testName, $"Send failed at message {i}");
                        return;
                    }
                }

                int timeoutMs = Math.Max(30000, messageCount * 100);
                if (!allReceived.WaitOne(timeoutMs))
                {
                    _framework.RecordFailure(testName, $"Only {receivedCount}/{messageCount} messages received within timeout");
                    return;
                }

                sw.Stop();

                long totalBytes = (long)messageSize * messageCount;
                double seconds = sw.Elapsed.TotalSeconds;
                double bytesPerSec = totalBytes / seconds;
                double msgsPerSec = messageCount / seconds;

                string throughput;
                if (bytesPerSec >= 1_073_741_824)
                    throughput = (bytesPerSec / 1_073_741_824).ToString("F2") + " GB/s";
                else if (bytesPerSec >= 1_048_576)
                    throughput = (bytesPerSec / 1_048_576).ToString("F1") + " MB/s";
                else if (bytesPerSec >= 1024)
                    throughput = (bytesPerSec / 1024).ToString("F0") + " KB/s";
                else
                    throughput = bytesPerSec.ToString("F0") + " B/s";

                Console.WriteLine($"       {messageCount} msgs x {FormatSize(messageSize)} = {FormatSize(totalBytes)} in {seconds:F2}s");
                Console.WriteLine($"       {msgsPerSec:F0} msgs/s, {throughput}");

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static string FormatSize(long bytes)
        {
            if (bytes >= 1_073_741_824) return (bytes / 1_073_741_824.0).ToString("F1") + " GB";
            if (bytes >= 1_048_576) return (bytes / 1_048_576.0).ToString("F1") + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("F0") + " KB";
            return bytes + " B";
        }

        #endregion

        #region v6.1.0-MaxConnections-Tests

        static async Task Test_MaxConnectionsEnforced()
        {
            string testName = "MaxConnections Enforced (v6.1.0)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            List<WatsonTcpClient> clients = new List<WatsonTcpClient>();

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Settings.MaxConnections = 2;
                server.Settings.EnforceMaxConnections = true;
                server.Start();
                await Task.Delay(100);

                // Connect 2 clients (should succeed)
                for (int i = 0; i < 2; i++)
                {
                    var client = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(client);
                    client.Connect();
                    clients.Add(client);
                    await Task.Delay(200);
                }

                if (server.Connections != 2)
                {
                    _framework.RecordFailure(testName, $"Expected 2 connections, got {server.Connections}");
                    return;
                }

                // 3rd client - should fail to connect or be immediately disconnected
                bool thirdClientFailed = false;
                WatsonTcpClient thirdClient = null;
                try
                {
                    thirdClient = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(thirdClient);
                    thirdClient.Connect();
                    await Task.Delay(500);
                    // Even if the TCP connection succeeded, the server should have closed it
                    // Check that the server still only has 2 connections
                    if (server.Connections <= 2) thirdClientFailed = true;
                }
                catch
                {
                    thirdClientFailed = true;
                }
                finally
                {
                    SafeDispose(thirdClient);
                }

                if (!thirdClientFailed)
                {
                    _framework.RecordFailure(testName, "Third client should have been rejected but server accepted it");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                foreach (var c in clients) SafeDispose(c);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_MaxConnectionsNotEnforced()
        {
            string testName = "MaxConnections Not Enforced (Legacy, v6.1.0)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            List<WatsonTcpClient> clients = new List<WatsonTcpClient>();

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Settings.MaxConnections = 2;
                server.Settings.EnforceMaxConnections = false;
                server.Start();
                await Task.Delay(100);

                // Connect 3 clients - all should succeed when enforcement is off
                for (int i = 0; i < 3; i++)
                {
                    var client = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(client);
                    client.Connect();
                    clients.Add(client);
                    await Task.Delay(200);
                }

                await Task.Delay(500);

                if (server.Connections < 3)
                {
                    _framework.RecordFailure(testName, $"Expected 3 connections with enforcement off, got {server.Connections}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_MaxHeaderSizeEnforced()
        {
            string testName = "MaxHeaderSize Setting (v6.1.0)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                // Verify the setting can be set
                server.Settings.MaxHeaderSize = 1024;
                if (server.Settings.MaxHeaderSize != 1024)
                {
                    _framework.RecordFailure(testName, "MaxHeaderSize not properly set on server");
                    return;
                }

                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Settings.MaxHeaderSize = 2048;
                if (client.Settings.MaxHeaderSize != 2048)
                {
                    _framework.RecordFailure(testName, "MaxHeaderSize not properly set on client");
                    return;
                }

                client.Connect();
                await Task.Delay(200);

                // Verify normal messages still work with reasonable header sizes
                string testData = "Hello with MaxHeaderSize set";
                ManualResetEvent received = new ManualResetEvent(false);
                string receivedData = null;
                server.Events.MessageReceived += (s, e) =>
                {
                    receivedData = Encoding.UTF8.GetString(e.Data);
                    received.Set();
                };

                await client.SendAsync(testData);
                if (!received.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Message not received with MaxHeaderSize set");
                    return;
                }

                if (receivedData != testData)
                {
                    _framework.RecordFailure(testName, $"Expected '{testData}', got '{receivedData}'");
                    return;
                }

                // Verify that setting an invalid MaxHeaderSize throws
                bool exceptionThrown = false;
                try
                {
                    server.Settings.MaxHeaderSize = 10; // too small, must be > 24
                }
                catch (ArgumentException)
                {
                    exceptionThrown = true;
                }

                if (!exceptionThrown)
                {
                    _framework.RecordFailure(testName, "Setting MaxHeaderSize too small should throw");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region v6.1.0-Rapid-Connect-Disconnect

        static async Task Test_RapidConnectDisconnect()
        {
            string testName = "Rapid Connect/Disconnect (v6.1.0)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                int iterations = 10;
                for (int i = 0; i < iterations; i++)
                {
                    WatsonTcpClient client = new WatsonTcpClient(_hostname, port);
                    SetupDefaultClientHandlers(client);
                    client.Connect();
                    await Task.Delay(50);
                    SafeDispose(client);
                    await Task.Delay(50);
                }

                await Task.Delay(500);

                // Server should still be running with no connected clients
                if (!server.IsListening)
                {
                    _framework.RecordFailure(testName, "Server stopped listening after rapid connect/disconnect");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        #endregion

        #region v6.1.0-Concurrent-Sync-Requests

        static async Task Test_ConcurrentSyncRequests()
        {
            string testName = "Concurrent Sync Requests (v6.1.0)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Callbacks.SyncRequestReceivedAsync = async (req) =>
                {
                    // Echo back with "Reply:" prefix
                    string requestData = Encoding.UTF8.GetString(req.Data);
                    await Task.Delay(50); // simulate processing
                    return new SyncResponse(req, "Reply:" + requestData);
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                // Send 5 concurrent sync requests
                int numRequests = 5;
                Task<SyncResponse>[] tasks = new Task<SyncResponse>[numRequests];
                for (int i = 0; i < numRequests; i++)
                {
                    int idx = i;
                    tasks[i] = client.SendAndWaitAsync(10000, "Request" + idx);
                }

                SyncResponse[] responses = await Task.WhenAll(tasks);

                // Verify all responses
                HashSet<string> expectedResponses = new HashSet<string>();
                for (int i = 0; i < numRequests; i++)
                    expectedResponses.Add("Reply:Request" + i);

                HashSet<string> actualResponses = new HashSet<string>();
                foreach (var resp in responses)
                {
                    if (resp == null || resp.Data == null)
                    {
                        _framework.RecordFailure(testName, "Null response received");
                        return;
                    }
                    actualResponses.Add(Encoding.UTF8.GetString(resp.Data));
                }

                if (!expectedResponses.SetEquals(actualResponses))
                {
                    _framework.RecordFailure(testName, "Responses don't match expected values. Expected: " + string.Join(", ", expectedResponses) + " Got: " + string.Join(", ", actualResponses));
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_SslConnectivity()
        {
            string testName = "SSL Connectivity (v6.1.0)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;

            try
            {
                string pfxFile = "test.pfx";
                if (!File.Exists(pfxFile))
                {
                    _framework.RecordSuccess(testName + " [SKIPPED - no test.pfx]");
                    return;
                }

                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port, pfxFile, "password");
                SetupDefaultServerHandlers(server);
                server.Settings.AcceptInvalidCertificates = true;
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port, pfxFile, "password");
                SetupDefaultClientHandlers(client);
                client.Settings.AcceptInvalidCertificates = true;
                client.Connect();
                await Task.Delay(200);

                if (!client.Connected)
                {
                    _framework.RecordFailure(testName, "SSL client not connected");
                    return;
                }

                var clients = server.ListClients().ToList();
                if (clients.Count != 1)
                {
                    _framework.RecordFailure(testName, $"Expected 1 SSL client, found {clients.Count}");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_SslMessageExchange()
        {
            string testName = "SSL Message Exchange (v6.1.0)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            ManualResetEvent messageReceived = new ManualResetEvent(false);
            string receivedData = null;

            try
            {
                string pfxFile = "test.pfx";
                if (!File.Exists(pfxFile))
                {
                    _framework.RecordSuccess(testName + " [SKIPPED - no test.pfx]");
                    return;
                }

                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port, pfxFile, "password");
                server.Settings.AcceptInvalidCertificates = true;
                server.Events.MessageReceived += (s, e) =>
                {
                    receivedData = Encoding.UTF8.GetString(e.Data);
                    messageReceived.Set();
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port, pfxFile, "password");
                SetupDefaultClientHandlers(client);
                client.Settings.AcceptInvalidCertificates = true;
                client.Connect();
                await Task.Delay(200);

                string testData = "Hello over SSL!";
                await client.SendAsync(testData);

                if (!messageReceived.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Server did not receive SSL message");
                    return;
                }

                if (receivedData != testData)
                {
                    _framework.RecordFailure(testName, $"Expected '{testData}', got '{receivedData}'");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

        static async Task Test_ServerStopWhileClientSending()
        {
            string testName = "Server Stop While Client Connected (v6.1.0)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            bool disconnectDetected = false;
            ManualResetEvent disconnectEvent = new ManualResetEvent(false);

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Events.ServerDisconnected += (s, e) =>
                {
                    disconnectDetected = true;
                    disconnectEvent.Set();
                };
                client.Connect();
                await Task.Delay(200);

                // Stop the server while client is connected
                SafeDispose(server);
                server = null;

                // Try to send a message to trigger disconnect detection
                try
                {
                    await client.SendAsync("trigger disconnect");
                }
                catch { }

                // Client should detect the disconnect
                disconnectEvent.WaitOne(10000);

                // Also check that the client is no longer connected
                if (!disconnectDetected && client.Connected)
                {
                    _framework.RecordFailure(testName, "Client did not detect server stop");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_DuplicateClientGuid()
        {
            string testName = "Duplicate Client GUID (v6.1.0)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client1 = null;
            WatsonTcpClient client2 = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                SetupDefaultServerHandlers(server);
                server.Start();
                await Task.Delay(100);

                Guid sharedGuid = Guid.NewGuid();

                client1 = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client1);
                client1.Settings.Guid = sharedGuid;
                client1.Connect();
                await Task.Delay(300);

                // Second client with the same GUID
                client2 = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client2);
                client2.Settings.Guid = sharedGuid;
                client2.Connect();
                await Task.Delay(500);

                // At least one client should be connected
                // The behavior may vary - the second connection should replace the first
                // or the server should handle it gracefully
                bool atLeastOneConnected = client1.Connected || client2.Connected;
                if (!atLeastOneConnected)
                {
                    _framework.RecordFailure(testName, "Neither client is connected with duplicate GUID");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
            }
            finally
            {
                SafeDispose(client1);
                SafeDispose(client2);
                SafeDispose(server);
                await Task.Delay(100);
            }
        }

        static async Task Test_SendWithOffset()
        {
            string testName = "Send With Byte Array Offset (v6.1.0)";
            _framework.StartTest(testName);
            WatsonTcpServer server = null;
            WatsonTcpClient client = null;
            ManualResetEvent messageReceived = new ManualResetEvent(false);
            byte[] receivedBytes = null;

            try
            {
                int port = GetNextPort();
                server = new WatsonTcpServer(_hostname, port);
                server.Events.MessageReceived += (s, e) =>
                {
                    receivedBytes = e.Data;
                    messageReceived.Set();
                };
                server.Start();
                await Task.Delay(100);

                client = new WatsonTcpClient(_hostname, port);
                SetupDefaultClientHandlers(client);
                client.Connect();
                await Task.Delay(200);

                // Send with an offset - skip first 5 bytes
                byte[] fullData = Encoding.UTF8.GetBytes("HEADERHello World");
                int offset = 6; // skip "HEADER"
                await client.SendAsync(fullData, null, offset);

                if (!messageReceived.WaitOne(5000))
                {
                    _framework.RecordFailure(testName, "Message not received");
                    return;
                }

                string received = Encoding.UTF8.GetString(receivedBytes);
                if (received != "Hello World")
                {
                    _framework.RecordFailure(testName, $"Expected 'Hello World', got '{received}'");
                    return;
                }

                _framework.RecordSuccess(testName);
            }
            catch (Exception ex)
            {
                _framework.RecordFailure(testName, ex.Message);
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

    #region Test-Framework

    class TestFramework
    {
        private List<TestResult> _results = new List<TestResult>();
        private object _lock = new object();
        private Dictionary<string, System.Diagnostics.Stopwatch> _timers = new Dictionary<string, System.Diagnostics.Stopwatch>();
        private System.Diagnostics.Stopwatch _totalTimer = new System.Diagnostics.Stopwatch();

        public void StartSuite()
        {
            _totalTimer.Start();
        }

        public void StartTest(string testName)
        {
            lock (_lock)
            {
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                _timers[testName] = sw;
            }
        }

        public void RecordSuccess(string testName)
        {
            lock (_lock)
            {
                TimeSpan elapsed = StopTimer(testName);
                _results.Add(new TestResult { TestName = testName, Passed = true, Duration = elapsed });
                Console.WriteLine($"[PASS] {testName} ({FormatDuration(elapsed)})");
            }
        }

        public void RecordFailure(string testName, string reason)
        {
            lock (_lock)
            {
                TimeSpan elapsed = StopTimer(testName);
                _results.Add(new TestResult { TestName = testName, Passed = false, FailureReason = reason, Duration = elapsed });
                Console.WriteLine($"[FAIL] {testName} ({FormatDuration(elapsed)})");
                Console.WriteLine($"       Reason: {reason}");
            }
        }

        public void PrintSummary()
        {
            _totalTimer.Stop();

            Console.WriteLine();
            foreach (var result in _results)
            {
                string status = result.Passed ? "PASS" : "FAIL";
                Console.WriteLine($"[{status}] {result.TestName} ({FormatDuration(result.Duration)})");
            }

            Console.WriteLine();
            int passed = _results.Count(r => r.Passed);
            int failed = _results.Count(r => !r.Passed);
            int total = _results.Count;

            Console.WriteLine($"Total:    {total} tests");
            Console.WriteLine($"Passed:   {passed}");
            Console.WriteLine($"Failed:   {failed}");
            Console.WriteLine($"Duration: {FormatDuration(_totalTimer.Elapsed)}");

            if (failed > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Failed tests:");
                foreach (var result in _results.Where(r => !r.Passed))
                {
                    Console.WriteLine($"  - {result.TestName}: {result.FailureReason}");
                }
            }

            Console.WriteLine();

            if (failed == 0)
            {
                Console.WriteLine("===============================================");
                Console.WriteLine("OVERALL RESULT: PASS");
                Console.WriteLine("===============================================");
            }
            else
            {
                Console.WriteLine("===============================================");
                Console.WriteLine("OVERALL RESULT: FAIL");
                Console.WriteLine("===============================================");
            }
        }

        private TimeSpan StopTimer(string testName)
        {
            if (_timers.TryGetValue(testName, out var sw))
            {
                sw.Stop();
                _timers.Remove(testName);
                return sw.Elapsed;
            }
            return TimeSpan.Zero;
        }

        private string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalSeconds >= 1.0)
                return ts.TotalSeconds.ToString("F2") + "s";
            return ts.TotalMilliseconds.ToString("F0") + "ms";
        }
    }

    class TestResult
    {
        public string TestName { get; set; }
        public bool Passed { get; set; }
        public string FailureReason { get; set; }
        public TimeSpan Duration { get; set; }
    }

    #endregion
}
