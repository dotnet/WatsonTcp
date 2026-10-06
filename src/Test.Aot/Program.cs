namespace Test.Aot
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonTcp;

    /// <summary>
    /// Native AOT verification host for WatsonTcp.
    /// When published with PublishAot and run as a native binary, every scenario executes without runtime
    /// reflection or code generation.  Pass --jit to run the same scenarios under the JIT with reflection
    /// fallback explicitly disabled.  Exit code is zero only when every scenario passes.
    /// </summary>
    public static class Program
    {
        private const string Hostname = "127.0.0.1";

        private static bool _JitMode = false;

        public static async Task<int> Main(string[] args)
        {
            _JitMode = args.Any(a => String.Equals(a, "--jit", StringComparison.OrdinalIgnoreCase));

            List<(string Name, Func<Task> Run)> scenarios = new List<(string, Func<Task>)>
            {
                ("Runtime is Native AOT with reflection disabled", RuntimeIsNativeAot),
                ("Default helper has no reflection fallback", DefaultHelperHasNoReflectionFallback),
                ("Header wire format is unchanged", HeaderWireFormatUnchanged),
                ("Header round-trips", HeaderRoundTrips),
                ("Enums serialize as strings", EnumsSerializeAsStrings),
                ("Built-in metadata types serialize", BuiltInMetadataTypesSerialize),
                ("Exception and NameValueCollection serialize", SpecialTypesSerialize),
                ("Client to server message with metadata", ClientToServerWithMetadata),
                ("Server to client message with metadata", ServerToClientWithMetadata),
                ("Sync request/response with metadata", SyncRequestResponseWithMetadata),
                ("Sync request timeout", SyncRequestTimeout),
                ("Stream receive with metadata", StreamReceiveWithMetadata),
                ("SSL message exchange", SslMessageExchange),
                ("Custom handshake with metadata", HandshakeWithMetadata),
                ("Caller-supplied context enables custom metadata type", CustomContextMetadata),
                ("Server disconnect reports reason", ServerDisconnectReportsReason),
                ("Telemetry metrics recorded", TelemetryRecorded),
                ("NEGATIVE: unregistered top-level type serialize throws", UnregisteredTopLevelSerializeThrows),
                ("NEGATIVE: unregistered top-level type deserialize throws", UnregisteredTopLevelDeserializeThrows),
                ("NEGATIVE: unregistered metadata send throws, connection survives", UnregisteredMetadataSendThrowsConnectionSurvives),
                ("NEGATIVE: unregistered metadata sync send throws", UnregisteredMetadataSyncThrows),
                ("NEGATIVE: unknown status name rejected", UnknownStatusRejected),
                ("NEGATIVE: exception deserialization rejected", ExceptionDeserializationRejected)
            };

            Console.WriteLine("WatsonTcp Native AOT verification (" + (_JitMode ? "JIT, reflection fallback disabled" : "native") + ")");
            Console.WriteLine(new string('-', 90));

            int failed = 0;
            Stopwatch total = Stopwatch.StartNew();

            foreach ((string name, Func<Task> run) in scenarios)
            {
                Stopwatch sw = Stopwatch.StartNew();
                try
                {
                    Task task = run();
                    if (await Task.WhenAny(task, Task.Delay(30000)).ConfigureAwait(false) != task)
                        throw new TimeoutException("Scenario did not complete within 30 seconds.");
                    await task.ConfigureAwait(false);
                    Console.WriteLine("PASS  " + name.PadRight(72) + sw.ElapsedMilliseconds.ToString().PadLeft(7) + "ms");
                }
                catch (Exception e)
                {
                    failed++;
                    Console.WriteLine("FAIL  " + name.PadRight(72) + sw.ElapsedMilliseconds.ToString().PadLeft(7) + "ms");
                    Console.WriteLine("      " + e.GetType().Name + ": " + e.Message);
                }
            }

            Console.WriteLine(new string('-', 90));
            Console.WriteLine("Total: " + scenarios.Count + "  Passed: " + (scenarios.Count - failed) + "  Failed: " + failed + "  (" + total.ElapsedMilliseconds + "ms)");
            return failed == 0 ? 0 : 1;
        }

        #region Helpers

        private static DefaultSerializationHelper CreateHelper(System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver resolver = null)
        {
            // In native mode the default constructor must already be reflection-free; in JIT mode force it off.
            return _JitMode ? new DefaultSerializationHelper(resolver, false) : new DefaultSerializationHelper(resolver);
        }

        private static int GetFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(what + ": expected '" + expected + "', got '" + actual + "'.");
        }

        private static async Task<TException> Throws<TException>(Func<Task> action) where TException : Exception
        {
            try { await action().ConfigureAwait(false); }
            catch (TException e) { return e; }
            catch (Exception e) { throw new InvalidOperationException("Expected " + typeof(TException).Name + " but got " + e.GetType().Name + ": " + e.Message, e); }
            throw new InvalidOperationException("Expected " + typeof(TException).Name + " but nothing was thrown.");
        }

        private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 5000)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Timed out waiting for: " + what);
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        private static string FindCertificate()
        {
            foreach (string candidate in new[] { Path.Combine(AppContext.BaseDirectory, "test.pfx"), Path.Combine(Environment.CurrentDirectory, "test.pfx") })
            {
                if (File.Exists(candidate)) return candidate;
            }

            throw new FileNotFoundException("test.pfx not found next to the executable.");
        }

        private static async Task<(WatsonTcpServer Server, WatsonTcpClient Client)> StartPairAsync(
            Action<WatsonTcpServer> configureServer = null,
            Action<WatsonTcpClient> configureClient = null,
            System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver resolver = null,
            string pfx = null,
            bool defaultMessageHandlers = true)
        {
            int port = GetFreePort();

            WatsonTcpServer server = pfx == null ? new WatsonTcpServer(Hostname, port) : new WatsonTcpServer(Hostname, port, pfx, "password");
            server.SerializationHelper = CreateHelper(resolver);
            server.Settings.AcceptInvalidCertificates = true;
            if (defaultMessageHandlers) server.Events.MessageReceived += (s, e) => { };
            configureServer?.Invoke(server);
            server.Start();
            await WaitFor(() => server.IsListening, "server listening").ConfigureAwait(false);

            WatsonTcpClient client = pfx == null ? new WatsonTcpClient(Hostname, port) : new WatsonTcpClient(Hostname, port, pfx, "password");
            client.SerializationHelper = CreateHelper(resolver);
            client.Settings.AcceptInvalidCertificates = true;
            client.Events.MessageReceived += (s, e) => { };
            configureClient?.Invoke(client);
            client.Connect();
            await WaitFor(() => client.Connected && server.Connections == 1, "client connected").ConfigureAwait(false);

            return (server, client);
        }

        private static void Dispose(WatsonTcpServer server, WatsonTcpClient client)
        {
            try { client?.Dispose(); } catch (Exception) { }
            try { server?.Dispose(); } catch (Exception) { }
        }

        private static Dictionary<string, object> BuiltInMetadata()
        {
            return new Dictionary<string, object>
            {
                { "string", "s" }, { "int", 1 }, { "long", 2L }, { "double", 1.5 }, { "decimal", 2.5m }, { "bool", true },
                { "guid", Guid.Empty }, { "datetime", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                { "bytes", new byte[] { 1, 2 } }, { "strings", new[] { "a", "b" } }, { "list", new List<object> { 1, "two" } },
                { "nested", new Dictionary<string, object> { { "inner", 3 } } }, { "status", MessageStatus.Success }, { "null", null }
            };
        }

        private static void AssertBuiltInMetadata(Dictionary<string, object> md)
        {
            Check(md != null, "metadata missing");
            Equal("s", ((JsonElement)md["string"]).GetString(), "string");
            Equal(1, ((JsonElement)md["int"]).GetInt32(), "int");
            Equal(2L, ((JsonElement)md["long"]).GetInt64(), "long");
            Equal(1.5, ((JsonElement)md["double"]).GetDouble(), "double");
            Equal(2.5m, ((JsonElement)md["decimal"]).GetDecimal(), "decimal");
            Check(((JsonElement)md["bool"]).GetBoolean(), "bool");
            Equal(Guid.Empty, ((JsonElement)md["guid"]).GetGuid(), "guid");
            Equal(2, ((JsonElement)md["bytes"]).GetBytesFromBase64().Length, "bytes");
            Equal(2, ((JsonElement)md["strings"]).GetArrayLength(), "strings");
            Equal("two", ((JsonElement)md["list"])[1].GetString(), "list");
            Equal(3, ((JsonElement)md["nested"]).GetProperty("inner").GetInt32(), "nested");
            Equal("Success", ((JsonElement)md["status"]).GetString(), "status");
            Check(md.ContainsKey("null") && md["null"] == null, "null value");
        }

        #endregion

        #region Scenarios

        private static Task RuntimeIsNativeAot()
        {
            if (_JitMode) return Task.CompletedTask;

            Check(!RuntimeFeature.IsDynamicCodeSupported, "Expected a Native AOT runtime (IsDynamicCodeSupported == false). Publish with PublishAot or pass --jit.");
            Check(!JsonSerializer.IsReflectionEnabledByDefault, "Expected reflection-based JSON serialization to be disabled under Native AOT.");
            return Task.CompletedTask;
        }

        private static Task DefaultHelperHasNoReflectionFallback()
        {
            Check(!CreateHelper().ReflectionFallbackEnabled, "Helper must not use reflection fallback.");
            return Task.CompletedTask;
        }

        private static Task HeaderWireFormatUnchanged()
        {
            DefaultSerializationHelper helper = CreateHelper();
            WatsonMessage msg = new WatsonMessage
            {
                ContentLength = 3,
                Metadata = new Dictionary<string, object> { { "k", "v" } },
                TimestampUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                ConversationGuid = Guid.Empty
            };

            string json = helper.SerializeJson(msg, false);
            Equal("{\"len\":3,\"status\":\"Normal\",\"md\":{\"k\":\"v\"},\"syncreq\":false,\"syncresp\":false,\"ts\":\"2026-01-02T03:04:05Z\",\"convguid\":\"00000000-0000-0000-0000-000000000000\",\"SenderGuid\":\"00000000-0000-0000-0000-000000000000\"}",
                json, "header JSON");
            return Task.CompletedTask;
        }

        private static Task HeaderRoundTrips()
        {
            DefaultSerializationHelper helper = CreateHelper();
            WatsonMessage msg = new WatsonMessage { ContentLength = 7, Status = MessageStatus.AuthRequired, SyncRequest = true, ExpirationUtc = DateTime.UtcNow, PresharedKey = new byte[16] };
            WatsonMessage copy = helper.DeserializeJson<WatsonMessage>(helper.SerializeJson(msg, false));
            Equal(7L, copy.ContentLength, "len");
            Equal(MessageStatus.AuthRequired, copy.Status, "status");
            Check(copy.SyncRequest, "syncreq");
            Equal(16, copy.PresharedKey.Length, "psk");
            Equal(msg.ConversationGuid, copy.ConversationGuid, "convguid");
            return Task.CompletedTask;
        }

        private static Task EnumsSerializeAsStrings()
        {
            DefaultSerializationHelper helper = CreateHelper();
            foreach (MessageStatus v in Enum.GetValues<MessageStatus>())
            {
                Equal("\"" + v + "\"", helper.SerializeJson(v, false), "MessageStatus." + v);
                Equal(v, helper.DeserializeJson<MessageStatus>("\"" + v + "\""), "MessageStatus." + v + " round-trip");
            }

            foreach (DisconnectReason v in Enum.GetValues<DisconnectReason>())
                Equal("\"" + v + "\"", helper.SerializeJson(v, false), "DisconnectReason." + v);

            foreach (TlsVersion v in Enum.GetValues<TlsVersion>())
                Equal("\"" + v + "\"", helper.SerializeJson(v, false), "TlsVersion." + v);

            Equal(MessageStatus.Success, helper.DeserializeJson<WatsonMessage>("{\"len\":0,\"status\":1}").Status, "numeric status");
            return Task.CompletedTask;
        }

        private static Task BuiltInMetadataTypesSerialize()
        {
            DefaultSerializationHelper helper = CreateHelper();
            WatsonMessage copy = helper.DeserializeJson<WatsonMessage>(helper.SerializeJson(new WatsonMessage { Metadata = BuiltInMetadata() }, false));
            AssertBuiltInMetadata(copy.Metadata);
            return Task.CompletedTask;
        }

        private static Task SpecialTypesSerialize()
        {
            DefaultSerializationHelper helper = CreateHelper();

            using (JsonDocument doc = JsonDocument.Parse(helper.SerializeJson(new ArgumentException("bad", "p", new IOException("inner")), false)))
            {
                Equal("System.ArgumentException", doc.RootElement.GetProperty("Type").GetString(), "exception type");
                Equal("p", doc.RootElement.GetProperty("ParamName").GetString(), "param name");
                Equal("inner", doc.RootElement.GetProperty("InnerException").GetProperty("Message").GetString(), "inner exception");
            }

            NameValueCollection nvc = new NameValueCollection { { "a", "1" }, { "a", "2" } };
            Equal("{\"a\":\"1, 2\"}", helper.SerializeJson(nvc, false), "NameValueCollection");
            return Task.CompletedTask;
        }

        private static async Task ClientToServerWithMetadata()
        {
            Dictionary<string, object> received = null;
            string data = null;
            var (server, client) = await StartPairAsync(configureServer: s => s.Events.MessageReceived += (o, e) => { data = Encoding.UTF8.GetString(e.Data); received = e.Metadata; });
            try
            {
                Check(await client.SendAsync("hello", BuiltInMetadata()), "send failed");
                await WaitFor(() => received != null, "message");
                Equal("hello", data, "data");
                AssertBuiltInMetadata(received);
            }
            finally { Dispose(server, client); }
        }

        private static async Task ServerToClientWithMetadata()
        {
            Dictionary<string, object> received = null;
            var (server, client) = await StartPairAsync(configureClient: c => c.Events.MessageReceived += (o, e) => received = e.Metadata);
            try
            {
                Check(await server.SendAsync(server.ListClients().Single().Guid, "hi", BuiltInMetadata()), "send failed");
                await WaitFor(() => received != null, "message");
                AssertBuiltInMetadata(received);
            }
            finally { Dispose(server, client); }
        }

        private static async Task SyncRequestResponseWithMetadata()
        {
            var (server, client) = await StartPairAsync(configureServer: s =>
                s.Callbacks.SyncRequestReceivedAsync = req => Task.FromResult(new SyncResponse(req, new Dictionary<string, object> { { "echo", ((JsonElement)req.Metadata["q"]).GetString() } }, "pong")));
            try
            {
                SyncResponse resp = await client.SendAndWaitAsync(5000, "ping", new Dictionary<string, object> { { "q", "question" } });
                Equal("pong", Encoding.UTF8.GetString(resp.Data), "response data");
                Equal("question", ((JsonElement)resp.Metadata["echo"]).GetString(), "response metadata");
            }
            finally { Dispose(server, client); }
        }

        private static async Task SyncRequestTimeout()
        {
            var (server, client) = await StartPairAsync(configureServer: s =>
                s.Callbacks.SyncRequestReceivedAsync = async req => { await Task.Delay(3000); return new SyncResponse(req, "late"); });
            try
            {
                await Throws<TimeoutException>(() => client.SendAndWaitAsync(1000, "ping"));
            }
            finally { Dispose(server, client); }
        }

        private static async Task StreamReceiveWithMetadata()
        {
            byte[] payload = new byte[512 * 1024];
            new Random(1).NextBytes(payload);
            byte[] received = null;
            Dictionary<string, object> md = null;

            var (server, client) = await StartPairAsync(configureServer: s =>
            {
                s.Callbacks.StreamReceivedAsync = async (e, token) =>
                {
                    using MemoryStream ms = new MemoryStream();
                    await e.DataStream.CopyToAsync(ms, token);
                    md = e.Metadata;
                    received = ms.ToArray();
                };
            }, defaultMessageHandlers: false);

            try
            {
                using MemoryStream source = new MemoryStream(payload);
                Check(await client.SendAsync(payload.Length, source, new Dictionary<string, object> { { "name", "blob" } }), "send failed");
                await WaitFor(() => received != null, "stream", 10000);
                Check(payload.SequenceEqual(received), "stream payload mismatch");
                Equal("blob", ((JsonElement)md["name"]).GetString(), "stream metadata");
            }
            finally { Dispose(server, client); }
        }

        private static async Task SslMessageExchange()
        {
            string data = null;
            var (server, client) = await StartPairAsync(configureServer: s => s.Events.MessageReceived += (o, e) => data = Encoding.UTF8.GetString(e.Data), pfx: FindCertificate());
            try
            {
                Check(await client.SendAsync("secure", new Dictionary<string, object> { { "tls", true } }), "send failed");
                await WaitFor(() => data != null, "ssl message");
                Equal("secure", data, "ssl data");
            }
            finally { Dispose(server, client); }
        }

        private static async Task HandshakeWithMetadata()
        {
            string serverSaw = null;
            var (server, client) = await StartPairAsync(
                configureServer: s => s.Callbacks.HandshakeAsync = async (session, token) =>
                {
                    HandshakeMessage m = await session.ReceiveAsync(token);
                    serverSaw = ((JsonElement)m.Metadata["user"]).GetString();
                    await session.SendAsync(new HandshakeMessage { Type = "ok", Metadata = new Dictionary<string, object> { { "granted", true } } }, token);
                    return HandshakeResult.Succeed();
                },
                configureClient: c => c.Callbacks.HandshakeAsync = async (session, token) =>
                {
                    await session.SendAsync(new HandshakeMessage { Type = "hello", Metadata = new Dictionary<string, object> { { "user", "alice" } } }, token);
                    HandshakeMessage reply = await session.ReceiveAsync(token);
                    return ((JsonElement)reply.Metadata["granted"]).GetBoolean() ? HandshakeResult.Succeed() : HandshakeResult.Fail("denied");
                });
            try
            {
                Equal("alice", serverSaw, "handshake metadata");
                Check(client.Connected, "client should be connected");
            }
            finally { Dispose(server, client); }
        }

        private static async Task CustomContextMetadata()
        {
            Dictionary<string, object> received = null;
            var (server, client) = await StartPairAsync(
                configureServer: s => s.Events.MessageReceived += (o, e) => received = e.Metadata,
                resolver: AotHostJsonContext.Default);
            try
            {
                Check(await client.SendAsync("x", new Dictionary<string, object> { { "order", new Order { Id = 42, Sku = "ABC" } } }), "send failed");
                await WaitFor(() => received != null, "message");
                Equal(42, ((JsonElement)received["order"]).GetProperty("Id").GetInt32(), "custom type");
            }
            finally { Dispose(server, client); }
        }

        private static async Task ServerDisconnectReportsReason()
        {
            DisconnectReason? reason = null;
            var (server, client) = await StartPairAsync(configureClient: c => c.Events.ServerDisconnected += (o, e) => reason = e.Reason);
            try
            {
                await server.DisconnectClientAsync(server.ListClients().Single().Guid, MessageStatus.Removed);
                await WaitFor(() => reason != null, "disconnect event");
                Equal(DisconnectReason.Removed, reason.Value, "disconnect reason");
            }
            finally { Dispose(server, client); }
        }

        private static async Task TelemetryRecorded()
        {
            long measurements = 0;
            using MeterListener listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == WatsonTcpMetrics.MeterName) l.EnableMeasurementEvents(instrument); };
            listener.SetMeasurementEventCallback<long>((i, m, t, s) => Interlocked.Increment(ref measurements));
            listener.SetMeasurementEventCallback<double>((i, m, t, s) => Interlocked.Increment(ref measurements));
            listener.SetMeasurementEventCallback<int>((i, m, t, s) => Interlocked.Increment(ref measurements));
            listener.Start();

            var (server, client) = await StartPairAsync();
            try
            {
                Check(await client.SendAsync("metric"), "send failed");
                await WaitFor(() => Interlocked.Read(ref measurements) > 0, "telemetry measurement");
            }
            finally { Dispose(server, client); }
        }

        private static async Task UnregisteredTopLevelSerializeThrows()
        {
            NotSupportedException e = await Throws<NotSupportedException>(() => Task.FromResult(CreateHelper().SerializeJson(new Unregistered())));
            Check(e.Message.Contains("JsonSerializerContext"), "error should explain how to register types: " + e.Message);
        }

        private static Task UnregisteredTopLevelDeserializeThrows()
        {
            return Throws<NotSupportedException>(() => Task.FromResult(CreateHelper().DeserializeJson<Unregistered>("{}")));
        }

        private static async Task UnregisteredMetadataSendThrowsConnectionSurvives()
        {
            string data = null;
            var (server, client) = await StartPairAsync(configureServer: s => s.Events.MessageReceived += (o, e) => data = Encoding.UTF8.GetString(e.Data));
            try
            {
                await Throws<NotSupportedException>(() => client.SendAsync("no", new Dictionary<string, object> { { "bad", new Unregistered() } }));
                await Throws<NotSupportedException>(() => server.SendAsync(server.ListClients().Single().Guid, "no", new Dictionary<string, object> { { "bad", new Unregistered() } }));
                await Task.Delay(200);
                Check(client.Connected, "client should still be connected");
                Check(await client.SendAsync("yes"), "follow-up send failed");
                await WaitFor(() => data != null, "follow-up message");
                Equal("yes", data, "follow-up data");
            }
            finally { Dispose(server, client); }
        }

        private static async Task UnregisteredMetadataSyncThrows()
        {
            var (server, client) = await StartPairAsync(configureServer: s => s.Callbacks.SyncRequestReceivedAsync = req => Task.FromResult(new SyncResponse(req, "ok")));
            try
            {
                await Throws<NotSupportedException>(() => client.SendAndWaitAsync(2000, "no", new Dictionary<string, object> { { "bad", new Unregistered() } }));
                SyncResponse resp = await client.SendAndWaitAsync(5000, "yes");
                Equal("ok", Encoding.UTF8.GetString(resp.Data), "follow-up sync");
            }
            finally { Dispose(server, client); }
        }

        private static Task UnknownStatusRejected()
        {
            return Throws<JsonException>(() => Task.FromResult(CreateHelper().DeserializeJson<WatsonMessage>("{\"len\":0,\"status\":\"Bogus\"}")));
        }

        private static Task ExceptionDeserializationRejected()
        {
            return Throws<NotSupportedException>(() => Task.FromResult(CreateHelper().DeserializeJson<Exception>("{}")));
        }

        #endregion
    }

    public sealed class Order
    {
        public int Id { get; set; }

        public string Sku { get; set; }
    }

    public sealed class Unregistered
    {
        public string Value { get; set; }
    }

    [JsonSerializable(typeof(Order))]
    internal partial class AotHostJsonContext : JsonSerializerContext
    {
    }
}
