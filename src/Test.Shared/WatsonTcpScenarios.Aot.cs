namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.IO;
    using System.Linq;
    using System.Net.Sockets;
    using System.Reflection;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.Json.Serialization;
    using System.Text.Json.Serialization.Metadata;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonTcp;

    /// <summary>
    /// Native AOT and trimming compatibility scenarios.
    /// Scenarios prefixed "AotStrict" run the serializer with reflection fallback disabled, which reproduces the exact
    /// serialization behavior of a Native AOT or trimmed application inside a JIT test host.
    /// </summary>
    public static partial class WatsonTcpScenarios
    {
        #region Helpers

        private static readonly DateTime _AotFixedTimestamp = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);
        private static readonly Guid _AotFixedConversation = Guid.Parse("11111111-2222-3333-4444-555555555555");
        private static readonly Guid _AotFixedSender = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        private static DefaultSerializationHelper CreateStrictHelper(IJsonTypeInfoResolver resolver = null)
        {
            DefaultSerializationHelper helper = new DefaultSerializationHelper(resolver, false);
            TestAssert.False(helper.ReflectionFallbackEnabled, "Strict helper must not fall back to reflection.");
            return helper;
        }

        private static WatsonMessage CreateFixedMessage(Dictionary<string, object> metadata = null)
        {
            return new WatsonMessage
            {
                ContentLength = 42,
                Status = MessageStatus.Normal,
                Metadata = metadata,
                SyncRequest = true,
                SyncResponse = false,
                TimestampUtc = _AotFixedTimestamp,
                ExpirationUtc = _AotFixedTimestamp.AddSeconds(30),
                ConversationGuid = _AotFixedConversation,
                SenderGuid = _AotFixedSender,
                PresharedKey = Encoding.UTF8.GetBytes("0123456789abcdef")
            };
        }

        private static Dictionary<string, object> CreateBuiltInMetadata()
        {
            return new Dictionary<string, object>
            {
                { "string", "hello" },
                { "bool", true },
                { "byte", (byte)7 },
                { "sbyte", (sbyte)-7 },
                { "short", (short)-1234 },
                { "ushort", (ushort)1234 },
                { "int", 123456 },
                { "uint", 123456u },
                { "long", 1234567890123L },
                { "ulong", 1234567890123UL },
                { "float", 1.5f },
                { "double", 2.25d },
                { "decimal", 3.125m },
                { "char", 'x' },
                { "guid", _AotFixedConversation },
                { "datetime", _AotFixedTimestamp },
                { "datetimeoffset", new DateTimeOffset(_AotFixedTimestamp) },
                { "timespan", TimeSpan.FromSeconds(90) },
                { "uri", new Uri("https://example.com/path") },
                { "bytes", new byte[] { 1, 2, 3 } },
                { "strings", new[] { "a", "b" } },
                { "ints", new[] { 1, 2, 3 } },
                { "longs", new[] { 1L, 2L } },
                { "doubles", new[] { 1.5d, 2.5d } },
                { "bools", new[] { true, false } },
                { "guids", new[] { _AotFixedSender } },
                { "objects", new object[] { "a", 1, true } },
                { "stringList", new List<string> { "x", "y" } },
                { "intList", new List<int> { 4, 5 } },
                { "longList", new List<long> { 6L } },
                { "objectList", new List<object> { "z", 9 } },
                { "nested", new Dictionary<string, object> { { "inner", "value" }, { "deeper", new Dictionary<string, object> { { "n", 1 } } } } },
                { "stringMap", new Dictionary<string, string> { { "k", "v" } } },
                { "element", JsonDocument.Parse("{\"e\":1}").RootElement.Clone() },
                { "jsonObject", new JsonObject { ["o"] = 1 } },
                { "jsonArray", new JsonArray(1, 2) },
                { "status", MessageStatus.Success },
                { "null", null }
            };
        }

        private static void AssertBuiltInMetadata(Dictionary<string, object> md)
        {
            TestAssert.NotNull(md, "Metadata should be present.");

            JsonElement Get(string key)
            {
                TestAssert.True(md.ContainsKey(key), "Metadata key '" + key + "' missing.");
                object value = md[key];
                TestAssert.True(value is JsonElement, "Metadata value '" + key + "' should deserialize as JsonElement but was " + (value?.GetType().Name ?? "null") + ".");
                return (JsonElement)value;
            }

            TestAssert.Equal("hello", Get("string").GetString());
            TestAssert.True(Get("bool").GetBoolean());
            TestAssert.Equal(7, Get("byte").GetInt32());
            TestAssert.Equal(-7, Get("sbyte").GetInt32());
            TestAssert.Equal(-1234, Get("short").GetInt32());
            TestAssert.Equal(1234, Get("ushort").GetInt32());
            TestAssert.Equal(123456, Get("int").GetInt32());
            TestAssert.Equal(123456u, Get("uint").GetUInt32());
            TestAssert.Equal(1234567890123L, Get("long").GetInt64());
            TestAssert.Equal(1234567890123UL, Get("ulong").GetUInt64());
            TestAssert.Equal(1.5f, Get("float").GetSingle());
            TestAssert.Equal(2.25d, Get("double").GetDouble());
            TestAssert.Equal(3.125m, Get("decimal").GetDecimal());
            TestAssert.Equal("x", Get("char").GetString());
            TestAssert.Equal(_AotFixedConversation, Get("guid").GetGuid());
            TestAssert.Equal(_AotFixedTimestamp, Get("datetime").GetDateTime().ToUniversalTime());
            TestAssert.Equal(new DateTimeOffset(_AotFixedTimestamp), Get("datetimeoffset").GetDateTimeOffset());
            TestAssert.Equal("00:01:30", Get("timespan").GetString());
            TestAssert.Equal("https://example.com/path", Get("uri").GetString());
            TestAssert.True(Get("bytes").GetBytesFromBase64().SequenceEqual(new byte[] { 1, 2, 3 }), "byte[] should round-trip as base64.");
            TestAssert.Equal(2, Get("strings").GetArrayLength());
            TestAssert.Equal(3, Get("ints").GetArrayLength());
            TestAssert.Equal(2, Get("longs").GetArrayLength());
            TestAssert.Equal(2, Get("doubles").GetArrayLength());
            TestAssert.Equal(2, Get("bools").GetArrayLength());
            TestAssert.Equal(_AotFixedSender, Get("guids")[0].GetGuid());
            TestAssert.Equal(3, Get("objects").GetArrayLength());
            TestAssert.Equal("y", Get("stringList")[1].GetString());
            TestAssert.Equal(5, Get("intList")[1].GetInt32());
            TestAssert.Equal(6L, Get("longList")[0].GetInt64());
            TestAssert.Equal(9, Get("objectList")[1].GetInt32());
            TestAssert.Equal("value", Get("nested").GetProperty("inner").GetString());
            TestAssert.Equal(1, Get("nested").GetProperty("deeper").GetProperty("n").GetInt32());
            TestAssert.Equal("v", Get("stringMap").GetProperty("k").GetString());
            TestAssert.Equal(1, Get("element").GetProperty("e").GetInt32());
            TestAssert.Equal(1, Get("jsonObject").GetProperty("o").GetInt32());
            TestAssert.Equal(2, Get("jsonArray").GetArrayLength());
            TestAssert.Equal("Success", Get("status").GetString());
            TestAssert.True(md.ContainsKey("null"), "Null-valued metadata key should be preserved.");
            TestAssert.True(md["null"] == null, "Null metadata value should deserialize as null.");
        }

        private static JsonSerializerOptions CreateReflectionReferenceOptions()
        {
            return new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            };
        }

        private static async Task<(WatsonTcpServer Server, WatsonTcpClient Client)> StartStrictPairAsync(
            Action<WatsonTcpServer> configureServer = null,
            Action<WatsonTcpClient> configureClient = null,
            IJsonTypeInfoResolver resolver = null)
        {
            int port = GetNextPort();

            WatsonTcpServer server = new WatsonTcpServer(_hostname, port);
            server.SerializationHelper = CreateStrictHelper(resolver);
            if (configureServer != null) configureServer(server);
            else SetupDefaultServerHandlers(server);
            server.Start();
            await WaitForServerListeningAsync(server);

            WatsonTcpClient client = new WatsonTcpClient(_hostname, port);
            client.SerializationHelper = CreateStrictHelper(resolver);
            if (configureClient != null) configureClient(client);
            else SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            return (server, client);
        }

        private static async Task<byte[]> ReadRawFrameAsync(NetworkStream stream, Func<JsonElement, bool> accept, int timeoutMs = 5000)
        {
            using CancellationTokenSource cts = new CancellationTokenSource(timeoutMs);

            while (true)
            {
                List<byte> header = new List<byte>();
                byte[] one = new byte[1];
                while (true)
                {
                    int read = await stream.ReadAsync(one, 0, 1, cts.Token).ConfigureAwait(false);
                    if (read == 0) throw new IOException("Raw peer stream closed while reading header.");
                    header.Add(one[0]);
                    int n = header.Count;
                    if (n >= 4 && header[n - 4] == 13 && header[n - 3] == 10 && header[n - 2] == 13 && header[n - 1] == 10) break;
                }

                byte[] headerBytes = header.Take(header.Count - 4).ToArray();
                using JsonDocument doc = JsonDocument.Parse(headerBytes);
                long len = doc.RootElement.GetProperty("len").GetInt64();

                byte[] payload = new byte[len];
                int offset = 0;
                while (offset < len)
                {
                    int read = await stream.ReadAsync(payload, offset, (int)len - offset, cts.Token).ConfigureAwait(false);
                    if (read == 0) throw new IOException("Raw peer stream closed while reading payload.");
                    offset += read;
                }

                if (accept(doc.RootElement)) return headerBytes;
            }
        }

        private static async Task WriteRawFrameAsync(NetworkStream stream, string headerJson, byte[] payload = null)
        {
            byte[] header = Encoding.UTF8.GetBytes(headerJson + "\r\n\r\n");
            await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
            if (payload != null && payload.Length > 0) await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        #endregion

        #region Assembly-And-Configuration

        public static void AotLibraryAssemblyIsMarkedTrimmable()
        {
            Assembly assembly = typeof(WatsonTcpServer).Assembly;
            AssemblyMetadataAttribute trimmable = assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "IsTrimmable");

            TestAssert.NotNull(trimmable, "WatsonTcp should carry the IsTrimmable assembly metadata emitted by IsAotCompatible.");
            TestAssert.Equal("True", trimmable.Value, "IsTrimmable metadata should be 'True'.");
        }

        public static void AotDefaultHelperUsesReflectionFallbackUnderJit()
        {
            DefaultSerializationHelper helper = new DefaultSerializationHelper();
            TestAssert.True(JsonSerializer.IsReflectionEnabledByDefault, "Test host is expected to be a JIT host with reflection enabled.");
            TestAssert.True(helper.ReflectionFallbackEnabled, "Default helper should fall back to reflection in a JIT host.");
            TestAssert.True(helper.TypeInfoResolver == null, "Default helper should have no caller-supplied resolver.");
        }

        public static void AotHelperResolverConstructorPreservesFallback()
        {
            DefaultSerializationHelper helper = new DefaultSerializationHelper(AotTestJsonContext.Default);
            TestAssert.True(helper.ReflectionFallbackEnabled, "Resolver-only constructor should keep reflection fallback enabled.");
            TestAssert.True(ReferenceEquals(AotTestJsonContext.Default, helper.TypeInfoResolver), "Supplied resolver should be exposed.");
        }

        public static void AotHelperNullResolverAccepted()
        {
            DefaultSerializationHelper helper = new DefaultSerializationHelper(null, false);
            TestAssert.True(helper.TypeInfoResolver == null);
            TestAssert.False(helper.ReflectionFallbackEnabled);
            TestAssert.NotNull(helper.SerializeJson(CreateFixedMessage(), false));
        }

        public static void AotStrictHelperReportsReflectionDisabled()
        {
            DefaultSerializationHelper helper = new DefaultSerializationHelper(AotTestJsonContext.Default, false);
            TestAssert.False(helper.ReflectionFallbackEnabled, "Explicitly disabling reflection must be honored.");
        }

        public static void AotInstantiateConverterDoesNotThrow()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            helper.InstantiateConverter();
            helper.InstantiateConverter();
        }

        public static void AotWireEnumsUseGenericStringConverter()
        {
            Type internalMode = typeof(WatsonTcpServer).Assembly.GetType("WatsonTcp.Mode", throwOnError: true);
            foreach (Type enumType in new[] { typeof(MessageStatus), typeof(DisconnectReason), internalMode, typeof(TlsVersion) })
            {
                JsonConverterAttribute attr = enumType.GetCustomAttribute<JsonConverterAttribute>();
                TestAssert.NotNull(attr, enumType.Name + " should declare a JsonConverter.");
                TestAssert.True(attr.ConverterType.IsGenericType, enumType.Name + " should use the AOT-safe generic JsonStringEnumConverter<T>.");
                TestAssert.Equal(typeof(JsonStringEnumConverter<>), attr.ConverterType.GetGenericTypeDefinition());
                TestAssert.Equal(enumType, attr.ConverterType.GetGenericArguments()[0]);
            }
        }

        #endregion

        #region Wire-Format

        public static void AotStrictHeaderUsesExpectedWirePropertyNames()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            string json = helper.SerializeJson(CreateFixedMessage(new Dictionary<string, object> { { "k", "v" } }), false);

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            string[] names = root.EnumerateObject().Select(p => p.Name).ToArray();
            string[] expected = { "len", "psk", "status", "md", "syncreq", "syncresp", "ts", "exp", "convguid", "SenderGuid" };

            TestAssert.True(expected.SequenceEqual(names), "Header property names/order changed: " + String.Join(",", names));
            TestAssert.Equal(42L, root.GetProperty("len").GetInt64());
            TestAssert.Equal("Normal", root.GetProperty("status").GetString());
            TestAssert.Equal("v", root.GetProperty("md").GetProperty("k").GetString());
            TestAssert.True(root.GetProperty("syncreq").GetBoolean());
            TestAssert.False(root.GetProperty("syncresp").GetBoolean());
            TestAssert.Equal(_AotFixedConversation, root.GetProperty("convguid").GetGuid());
            TestAssert.Equal(_AotFixedSender, root.GetProperty("SenderGuid").GetGuid());
            TestAssert.False(json.Contains("DataStream"), "DataStream must never be serialized.");
            TestAssert.False(json.Contains("\n"), "Compact header must not contain newlines.");
        }

        public static void AotStrictHeaderMatchesReflectionSerializerByteForByte()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            WatsonMessage msg = CreateFixedMessage(new Dictionary<string, object> { { "a", 1 }, { "b", "two" }, { "c", true } });

            string sourceGenerated = helper.SerializeJson(msg, false);
            string reflection = JsonSerializer.Serialize(msg, CreateReflectionReferenceOptions());

            TestAssert.Equal(reflection, sourceGenerated, "Source-generated header must be identical to the reflection-based header.");
        }

        public static void AotStrictHeaderOmitsNullMembers()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            WatsonMessage msg = new WatsonMessage { ContentLength = 0, TimestampUtc = _AotFixedTimestamp, ConversationGuid = _AotFixedConversation };
            string json = helper.SerializeJson(msg, false);

            TestAssert.False(json.Contains("\"psk\""), "Null preshared key should be omitted.");
            TestAssert.False(json.Contains("\"md\""), "Null metadata should be omitted.");
            TestAssert.False(json.Contains("\"exp\""), "Null expiration should be omitted.");
        }

        public static void AotStrictHeaderRoundTrips()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            WatsonMessage original = CreateFixedMessage(new Dictionary<string, object> { { "k", "v" } });
            WatsonMessage copy = helper.DeserializeJson<WatsonMessage>(helper.SerializeJson(original, false));

            TestAssert.Equal(original.ContentLength, copy.ContentLength);
            TestAssert.Equal(original.Status, copy.Status);
            TestAssert.Equal(original.SyncRequest, copy.SyncRequest);
            TestAssert.Equal(original.SyncResponse, copy.SyncResponse);
            TestAssert.Equal(original.TimestampUtc, copy.TimestampUtc);
            TestAssert.Equal(original.ExpirationUtc, copy.ExpirationUtc);
            TestAssert.Equal(original.ConversationGuid, copy.ConversationGuid);
            TestAssert.Equal(original.SenderGuid, copy.SenderGuid);
            TestAssert.True(original.PresharedKey.SequenceEqual(copy.PresharedKey), "Preshared key should round-trip.");
            TestAssert.Equal("v", ((JsonElement)copy.Metadata["k"]).GetString());
        }

        public static void AotStrictEveryMessageStatusRoundTripsAsString()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            foreach (MessageStatus status in Enum.GetValues(typeof(MessageStatus)).Cast<MessageStatus>())
            {
                WatsonMessage msg = CreateFixedMessage();
                msg.Status = status;
                string json = helper.SerializeJson(msg, false);
                TestAssert.True(json.Contains("\"status\":\"" + status + "\""), "Status " + status + " should serialize as its name.");
                TestAssert.Equal(status, helper.DeserializeJson<WatsonMessage>(json).Status);
            }
        }

        public static void AotStrictAllWireEnumsSerializeAsStrings()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();

            foreach (DisconnectReason v in Enum.GetValues(typeof(DisconnectReason)).Cast<DisconnectReason>())
            {
                TestAssert.Equal("\"" + v + "\"", helper.SerializeJson(v, false));
                TestAssert.Equal(v, helper.DeserializeJson<DisconnectReason>("\"" + v + "\""));
            }

            foreach (TlsVersion v in Enum.GetValues(typeof(TlsVersion)).Cast<TlsVersion>())
            {
                TestAssert.Equal("\"" + v + "\"", helper.SerializeJson(v, false));
                TestAssert.Equal(v, helper.DeserializeJson<TlsVersion>("\"" + v + "\""));
            }
        }

        public static void AotStrictNumericStatusStillAccepted()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            WatsonMessage msg = helper.DeserializeJson<WatsonMessage>("{\"len\":0,\"status\":1}");
            TestAssert.Equal(MessageStatus.Success, msg.Status, "Integer enum values from non-Watson peers must remain accepted.");
        }

        public static void AotStrictUnknownStatusNameRejected()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            TestAssert.ThrowsAny<JsonException>(() => helper.DeserializeJson<WatsonMessage>("{\"len\":0,\"status\":\"NotARealStatus\"}"));
        }

        public static void AotStrictUnknownHeaderPropertiesIgnored()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            WatsonMessage msg = helper.DeserializeJson<WatsonMessage>("{\"len\":5,\"status\":\"Normal\",\"future\":{\"x\":[1,2]},\"other\":\"y\"}");
            TestAssert.Equal(5L, msg.ContentLength);
            TestAssert.Equal(MessageStatus.Normal, msg.Status);
        }

        public static void AotStrictMalformedHeaderRejected()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            TestAssert.ThrowsAny<JsonException>(() => helper.DeserializeJson<WatsonMessage>("{\"len\":"));
            TestAssert.ThrowsAny<JsonException>(() => helper.DeserializeJson<WatsonMessage>("{\"len\":\"not-a-number\"}"));
        }

        public static void AotStrictInvalidPresharedKeyLengthRejected()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            string json = "{\"len\":0,\"psk\":\"" + Convert.ToBase64String(new byte[5]) + "\"}";
            TestAssert.ThrowsAny<Exception>(() => helper.DeserializeJson<WatsonMessage>(json));
        }

        public static void AotStrictHandshakeMessageRoundTrips()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            HandshakeMessage original = new HandshakeMessage
            {
                Type = "challenge",
                Data = new byte[] { 9, 8, 7 },
                Metadata = new Dictionary<string, object> { { "nonce", "abc" }, { "attempt", 2 } }
            };

            string json = helper.SerializeJson(original, false);
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                string[] names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
                TestAssert.True(new[] { "Type", "Metadata", "Data" }.SequenceEqual(names), "Handshake property names changed: " + String.Join(",", names));
            }

            HandshakeMessage copy = helper.DeserializeJson<HandshakeMessage>(json);
            TestAssert.Equal("challenge", copy.Type);
            TestAssert.True(copy.Data.SequenceEqual(original.Data), "Handshake data should round-trip.");
            TestAssert.Equal("abc", ((JsonElement)copy.Metadata["nonce"]).GetString());
            TestAssert.Equal(2, ((JsonElement)copy.Metadata["attempt"]).GetInt32());
            TestAssert.Equal(JsonSerializer.Serialize(original, CreateReflectionReferenceOptions()), json, "Handshake JSON must match reflection output.");
        }

        public static void AotStrictPrettyAndCompactOutputDiffer()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            WatsonMessage msg = CreateFixedMessage();
            string compact = helper.SerializeJson(msg, false);
            string pretty = helper.SerializeJson(msg, true);

            TestAssert.False(compact.Contains("\n"), "Compact output must be a single line.");
            TestAssert.True(pretty.Contains("\n"), "Pretty output must be indented.");
            TestAssert.Equal(compact, JsonSerializer.Serialize(JsonDocument.Parse(pretty).RootElement), "Pretty and compact must carry the same content.");
        }

        public static void AotNullInputsHandled()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            TestAssert.True(helper.SerializeJson(null) == null, "Serializing null should return null.");
            TestAssert.True(helper.DeserializeJson<WatsonMessage>("null") == null, "Deserializing JSON null should return null.");
        }

        #endregion

        #region Metadata-Values

        public static void AotStrictBuiltInMetadataTypesRoundTrip()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            string json = helper.SerializeJson(CreateFixedMessage(CreateBuiltInMetadata()), false);
            WatsonMessage copy = helper.DeserializeJson<WatsonMessage>(json);
            AssertBuiltInMetadata(copy.Metadata);
        }

        public static void AotStrictBuiltInMetadataMatchesReflectionOutput()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            WatsonMessage msg = CreateFixedMessage(CreateBuiltInMetadata());
            TestAssert.Equal(JsonSerializer.Serialize(msg, CreateReflectionReferenceOptions()), helper.SerializeJson(msg, false),
                "Metadata serialized without reflection must be identical to reflection output.");
        }

        public static void AotStrictCustomResolverSerializesRegisteredType()
        {
            DefaultSerializationHelper helper = CreateStrictHelper(AotTestJsonContext.Default);
            Dictionary<string, object> md = new Dictionary<string, object> { { "payload", new AotCustomPayload { Name = "widget", Count = 3 } } };

            WatsonMessage copy = helper.DeserializeJson<WatsonMessage>(helper.SerializeJson(CreateFixedMessage(md), false));
            JsonElement payload = (JsonElement)copy.Metadata["payload"];
            TestAssert.Equal("widget", payload.GetProperty("Name").GetString());
            TestAssert.Equal(3, payload.GetProperty("Count").GetInt32());

            AotCustomPayload direct = helper.DeserializeJson<AotCustomPayload>(helper.SerializeJson(new AotCustomPayload { Name = "n", Count = 1 }, false));
            TestAssert.Equal("n", direct.Name);
            TestAssert.Equal(1, direct.Count);
        }

        public static void AotStrictCustomResolverTakesPrecedenceOverBuiltIn()
        {
            DefaultJsonTypeInfoResolver overriding = new DefaultJsonTypeInfoResolver();
            overriding.Modifiers.Add(info =>
            {
                if (info.Type != typeof(HandshakeMessage)) return;
                foreach (JsonPropertyInfo prop in info.Properties)
                {
                    if (prop.Name == "Type") prop.Name = "overridden";
                }
            });

            DefaultSerializationHelper helper = CreateStrictHelper(overriding);
            string json = helper.SerializeJson(new HandshakeMessage { Type = "t" }, false);
            TestAssert.True(json.Contains("\"overridden\":\"t\""), "Caller-supplied resolver should be consulted before built-in metadata: " + json);
        }

        public static void AotJitDefaultHelperSerializesArbitraryTypesViaReflection()
        {
            DefaultSerializationHelper helper = new DefaultSerializationHelper();
            Dictionary<string, object> md = new Dictionary<string, object> { { "u", new AotUnregisteredPayload { Value = "reflected" } } };

            WatsonMessage copy = helper.DeserializeJson<WatsonMessage>(helper.SerializeJson(CreateFixedMessage(md), false));
            TestAssert.Equal("reflected", ((JsonElement)copy.Metadata["u"]).GetProperty("Value").GetString());
            TestAssert.Equal("x", helper.DeserializeJson<AotUnregisteredPayload>("{\"Value\":\"x\"}").Value);
        }

        public static void AotStrictUnregisteredTopLevelSerializeThrowsNotSupported()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            NotSupportedException e = TestAssert.Throws<NotSupportedException>(() => helper.SerializeJson(new AotUnregisteredPayload { Value = "x" }));
            TestAssert.True(e.Message.Contains(typeof(AotUnregisteredPayload).FullName), "Error should name the unregistered type: " + e.Message);
            TestAssert.True(e.Message.Contains("JsonSerializerContext"), "Error should explain how to register the type: " + e.Message);
        }

        public static void AotStrictUnregisteredTopLevelDeserializeThrowsNotSupported()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            NotSupportedException e = TestAssert.Throws<NotSupportedException>(() => helper.DeserializeJson<AotUnregisteredPayload>("{\"Value\":\"x\"}"));
            TestAssert.True(e.Message.Contains(typeof(AotUnregisteredPayload).FullName), "Error should name the unregistered type: " + e.Message);
        }

        public static void AotStrictUnregisteredMetadataValueThrowsNotSupported()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            Dictionary<string, object> md = new Dictionary<string, object> { { "bad", new AotUnregisteredPayload { Value = "x" } } };
            NotSupportedException e = TestAssert.Throws<NotSupportedException>(() => helper.SerializeJson(CreateFixedMessage(md), false));
            TestAssert.True(e.Message.Contains(nameof(AotUnregisteredPayload)), "Error should name the unregistered metadata type: " + e.Message);
        }

        public static void AotStrictCustomResolverDoesNotCoverOtherTypes()
        {
            DefaultSerializationHelper helper = CreateStrictHelper(AotTestJsonContext.Default);
            TestAssert.Throws<NotSupportedException>(() => helper.SerializeJson(new AotUnregisteredPayload { Value = "x" }));
            Dictionary<string, object> md = new Dictionary<string, object> { { "bad", new AotUnregisteredPayload() } };
            TestAssert.Throws<NotSupportedException>(() => helper.SerializeJson(CreateFixedMessage(md), false));
        }

        public static void AotStrictNonFiniteDoubleMetadataRejected()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            Dictionary<string, object> md = new Dictionary<string, object> { { "nan", double.NaN } };
            TestAssert.ThrowsAny<ArgumentException>(() => helper.SerializeJson(CreateFixedMessage(md), false));
        }

        #endregion

        #region Exceptions-And-Collections

        public static void AotStrictExceptionSerializesWithoutReflection()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            ArgumentException e = new ArgumentException("outer message", "theParam", new InvalidOperationException("inner message"));
            e.Data["key"] = "value";
            e.Data["number"] = 5;

            string json = helper.SerializeJson(e, false);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            TestAssert.Equal(typeof(ArgumentException).FullName, root.GetProperty("Type").GetString());
            TestAssert.True(root.GetProperty("Message").GetString().StartsWith("outer message"), "Message should be written.");
            TestAssert.Equal("theParam", root.GetProperty("ParamName").GetString());
            TestAssert.Equal("value", root.GetProperty("Data").GetProperty("key").GetString());
            TestAssert.Equal(5, root.GetProperty("Data").GetProperty("number").GetInt32());
            TestAssert.Equal(e.HResult, root.GetProperty("HResult").GetInt32());
            TestAssert.Equal("inner message", root.GetProperty("InnerException").GetProperty("Message").GetString());
            TestAssert.Equal(typeof(InvalidOperationException).FullName, root.GetProperty("InnerException").GetProperty("Type").GetString());
            TestAssert.False(json.Contains("TargetSite"), "TargetSite must not be serialized.");
        }

        public static void AotStrictThrownExceptionIncludesStackTrace()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            Exception captured;
            try { throw new InvalidOperationException("thrown"); }
            catch (Exception e) { captured = e; }

            using JsonDocument doc = JsonDocument.Parse(helper.SerializeJson(captured, false));
            TestAssert.True(doc.RootElement.TryGetProperty("StackTrace", out JsonElement st) && st.GetString().Length > 0, "Thrown exception should include a stack trace.");
            TestAssert.True(doc.RootElement.TryGetProperty("Source", out _), "Thrown exception should include its source.");
        }

        public static void AotStrictExceptionNullMembersOmitted()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            using JsonDocument doc = JsonDocument.Parse(helper.SerializeJson(new Exception("bare"), false));
            TestAssert.False(doc.RootElement.TryGetProperty("InnerException", out _), "Null inner exception should be omitted.");
            TestAssert.False(doc.RootElement.TryGetProperty("StackTrace", out _), "Null stack trace should be omitted.");
            TestAssert.False(doc.RootElement.TryGetProperty("HelpLink", out _), "Null help link should be omitted.");
        }

        public static void AotStrictExceptionDeserializationRejected()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            TestAssert.ThrowsAny<NotSupportedException>(() => helper.DeserializeJson<Exception>("{\"Message\":\"x\"}"));

            DefaultSerializationHelper jit = new DefaultSerializationHelper();
            TestAssert.ThrowsAny<NotSupportedException>(() => jit.DeserializeJson<Exception>("{\"Message\":\"x\"}"));
        }

        public static void AotStrictNameValueCollectionSerializes()
        {
            DefaultSerializationHelper helper = CreateStrictHelper();
            NameValueCollection nvc = new NameValueCollection();
            nvc.Add("single", "one");
            nvc.Add("multi", "a");
            nvc.Add("multi", "b");

            using JsonDocument doc = JsonDocument.Parse(helper.SerializeJson(nvc, false));
            TestAssert.Equal("one", doc.RootElement.GetProperty("single").GetString());
            TestAssert.Equal("a, b", doc.RootElement.GetProperty("multi").GetString());
        }

        public static void AotJitAndStrictSpecialTypeOutputIdentical()
        {
            DefaultSerializationHelper strict = CreateStrictHelper();
            DefaultSerializationHelper jit = new DefaultSerializationHelper();
            ArgumentNullException e = new ArgumentNullException("p", "msg");
            NameValueCollection nvc = new NameValueCollection { { "k", "v" } };

            TestAssert.Equal(jit.SerializeJson(e, false), strict.SerializeJson(e, false), "Exception output must not depend on reflection availability.");
            TestAssert.Equal(jit.SerializeJson(nvc, false), strict.SerializeJson(nvc, false), "NameValueCollection output must not depend on reflection availability.");
        }

        #endregion

        #region End-To-End

        public static async Task AotStrictEndToEndMessageWithBuiltInMetadata()
        {
            Dictionary<string, object> received = null;
            string receivedData = null;
            ManualResetEvent signal = new ManualResetEvent(false);

            var (server, client) = await StartStrictPairAsync(configureServer: s =>
            {
                s.Events.MessageReceived += (sender, e) => { received = e.Metadata; receivedData = Encoding.UTF8.GetString(e.Data); signal.Set(); };
            });

            try
            {
                TestAssert.True(await client.SendAsync("aot payload", CreateBuiltInMetadata()), "Send should succeed.");
                WaitForSignal(signal, 5000);
                TestAssert.Equal("aot payload", receivedData);
                AssertBuiltInMetadata(received);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotStrictEndToEndServerToClientWithMetadata()
        {
            Dictionary<string, object> received = null;
            ManualResetEvent signal = new ManualResetEvent(false);

            var (server, client) = await StartStrictPairAsync(configureClient: c =>
            {
                c.Events.MessageReceived += (sender, e) => { received = e.Metadata; signal.Set(); };
            });

            try
            {
                Guid guid = server.ListClients().Single().Guid;
                TestAssert.True(await server.SendAsync(guid, "to client", CreateBuiltInMetadata()), "Server send should succeed.");
                WaitForSignal(signal, 5000);
                AssertBuiltInMetadata(received);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotStrictEndToEndSyncRequestResponseWithMetadata()
        {
            Dictionary<string, object> requestMetadata = null;

            var (server, client) = await StartStrictPairAsync(configureServer: s =>
            {
                SetupDefaultServerHandlers(s);
                s.Callbacks.SyncRequestReceivedAsync = req =>
                {
                    requestMetadata = req.Metadata;
                    return Task.FromResult(new SyncResponse(req, new Dictionary<string, object> { { "reply", 99 }, { "ok", true } }, "pong"));
                };
            });

            try
            {
                SyncResponse resp = await client.SendAndWaitAsync(5000, "ping", new Dictionary<string, object> { { "request", "r1" } });
                TestAssert.NotNull(resp);
                TestAssert.Equal("pong", Encoding.UTF8.GetString(resp.Data));
                TestAssert.Equal(99, ((JsonElement)resp.Metadata["reply"]).GetInt32());
                TestAssert.True(((JsonElement)resp.Metadata["ok"]).GetBoolean());
                TestAssert.Equal("r1", ((JsonElement)requestMetadata["request"]).GetString());
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotStrictEndToEndStreamWithMetadata()
        {
            byte[] receivedBytes = null;
            Dictionary<string, object> received = null;
            ManualResetEvent signal = new ManualResetEvent(false);
            byte[] payload = CreatePatternedPayload(256 * 1024);

            var (server, client) = await StartStrictPairAsync(configureServer: s =>
            {
                s.Callbacks.StreamReceivedAsync = async (e, token) =>
                {
                    received = e.Metadata;
                    receivedBytes = await ReadAllBytesAsync(e.DataStream, token);
                    signal.Set();
                };
            });

            try
            {
                using MemoryStream ms = new MemoryStream(payload);
                TestAssert.True(await client.SendAsync(payload.Length, ms, new Dictionary<string, object> { { "file", "data.bin" }, { "size", payload.Length } }));
                WaitForSignal(signal, 10000);
                TestAssert.True(payload.SequenceEqual(receivedBytes), "Streamed payload should arrive intact.");
                TestAssert.Equal("data.bin", ((JsonElement)received["file"]).GetString());
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotStrictEndToEndSslWithMetadata()
        {
            string pfxFile = GetSslTestCertificatePath();
            int port = GetNextPort();
            Dictionary<string, object> received = null;
            ManualResetEvent signal = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port, pfxFile, "password");
            server.SerializationHelper = CreateStrictHelper();
            server.Settings.AcceptInvalidCertificates = true;
            server.Events.MessageReceived += (s, e) => { received = e.Metadata; signal.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server, timeoutMs: 5000);

            var client = new WatsonTcpClient(_hostname, port, pfxFile, "password");
            client.SerializationHelper = CreateStrictHelper();
            SetupDefaultClientHandlers(client);
            client.Settings.AcceptInvalidCertificates = true;
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1, timeoutMs: 5000);

            try
            {
                TestAssert.True(await client.SendAsync("tls", new Dictionary<string, object> { { "secure", true } }));
                WaitForSignal(signal, 5000);
                TestAssert.True(((JsonElement)received["secure"]).GetBoolean());
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotStrictEndToEndHandshakeWithMetadata()
        {
            string serverSawRole = null;
            int serverSawLevel = 0;
            bool clientSucceeded = false;

            var (server, client) = await StartStrictPairAsync(
                configureServer: s =>
                {
                    SetupDefaultServerHandlers(s);
                    s.Callbacks.HandshakeAsync = async (session, token) =>
                    {
                        HandshakeMessage m = await session.ReceiveAsync(token);
                        serverSawRole = ((JsonElement)m.Metadata["role"]).GetString();
                        serverSawLevel = ((JsonElement)m.Metadata["level"]).GetInt32();
                        await session.SendAsync(new HandshakeMessage { Type = "welcome", Metadata = new Dictionary<string, object> { { "granted", true } } }, token);
                        return HandshakeResult.Succeed();
                    };
                },
                configureClient: c =>
                {
                    SetupDefaultClientHandlers(c);
                    c.Callbacks.HandshakeAsync = async (session, token) =>
                    {
                        await session.SendAsync(new HandshakeMessage { Type = "hello", Metadata = new Dictionary<string, object> { { "role", "admin" }, { "level", 3 } } }, token);
                        HandshakeMessage reply = await session.ReceiveAsync(token);
                        clientSucceeded = reply.Type == "welcome" && ((JsonElement)reply.Metadata["granted"]).GetBoolean();
                        return clientSucceeded ? HandshakeResult.Succeed() : HandshakeResult.Fail("not granted");
                    };
                });

            try
            {
                // The client is marked connected when the server reports handshake success, which can precede the
                // client's own callback returning, so wait for the callback to observe the reply.
                TestAssert.True(client.Connected, "Client should be connected after handshake.");
                TestAssert.Equal("admin", serverSawRole);
                TestAssert.Equal(3, serverSawLevel);
                await WaitForConditionAsync(() => Volatile.Read(ref clientSucceeded), 5000, "Client should observe the server's handshake reply metadata.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotStrictEndToEndCustomResolverMetadata()
        {
            Dictionary<string, object> received = null;
            ManualResetEvent signal = new ManualResetEvent(false);

            var (server, client) = await StartStrictPairAsync(
                configureServer: s => s.Events.MessageReceived += (sender, e) => { received = e.Metadata; signal.Set(); },
                resolver: AotTestJsonContext.Default);

            try
            {
                Dictionary<string, object> md = new Dictionary<string, object> { { "payload", new AotCustomPayload { Name = "custom", Count = 7 } } };
                TestAssert.True(await client.SendAsync("x", md));
                WaitForSignal(signal, 5000);
                JsonElement payload = (JsonElement)received["payload"];
                TestAssert.Equal("custom", payload.GetProperty("Name").GetString());
                TestAssert.Equal(7, payload.GetProperty("Count").GetInt32());
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotMixedStrictAndJitPeersInteroperate()
        {
            int port = GetNextPort();
            Dictionary<string, object> serverReceived = null;
            Dictionary<string, object> clientReceived = null;
            ManualResetEvent serverSignal = new ManualResetEvent(false);
            ManualResetEvent clientSignal = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.SerializationHelper = new DefaultSerializationHelper();
            server.Events.MessageReceived += (s, e) => { serverReceived = e.Metadata; serverSignal.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.SerializationHelper = CreateStrictHelper();
            client.Events.MessageReceived += (s, e) => { clientReceived = e.Metadata; clientSignal.Set(); };
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                TestAssert.True(await client.SendAsync("from strict", CreateBuiltInMetadata()));
                WaitForSignal(serverSignal, 5000);
                AssertBuiltInMetadata(serverReceived);

                TestAssert.True(await server.SendAsync(server.ListClients().Single().Guid, "from jit", CreateBuiltInMetadata()));
                WaitForSignal(clientSignal, 5000);
                AssertBuiltInMetadata(clientReceived);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotCustomSerializationHelperStillUsed()
        {
            int port = GetNextPort();
            CountingSerializationHelper serverHelper = new CountingSerializationHelper();
            CountingSerializationHelper clientHelper = new CountingSerializationHelper();
            string receivedData = null;
            ManualResetEvent signal = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.SerializationHelper = serverHelper;
            server.Events.MessageReceived += (s, e) => { receivedData = Encoding.UTF8.GetString(e.Data); signal.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.SerializationHelper = clientHelper;
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                TestAssert.True(await client.SendAsync("custom helper", new Dictionary<string, object> { { "k", "v" } }));
                WaitForSignal(signal, 5000);
                TestAssert.Equal("custom helper", receivedData);
                TestAssert.True(clientHelper.Serializations > 0, "Custom client helper should be used for serialization.");
                TestAssert.True(serverHelper.Deserializations > 0, "Custom server helper should be used for deserialization.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        private sealed class CountingSerializationHelper : ISerializationHelper
        {
            private readonly DefaultSerializationHelper _Inner = new DefaultSerializationHelper();
            private int _Serializations;
            private int _Deserializations;

            public int Serializations => Volatile.Read(ref _Serializations);

            public int Deserializations => Volatile.Read(ref _Deserializations);

            public T DeserializeJson<T>(string json)
            {
                Interlocked.Increment(ref _Deserializations);
                return _Inner.DeserializeJson<T>(json);
            }

            public string SerializeJson(object obj, bool pretty = true)
            {
                Interlocked.Increment(ref _Serializations);
                return _Inner.SerializeJson(obj, pretty);
            }

            public void InstantiateConverter()
            {
                _Inner.InstantiateConverter();
            }
        }

        #endregion

        #region End-To-End-Negative

        public static async Task AotStrictClientUnregisteredMetadataThrowsAndConnectionSurvives()
        {
            string receivedData = null;
            ManualResetEvent signal = new ManualResetEvent(false);

            var (server, client) = await StartStrictPairAsync(configureServer: s =>
            {
                s.Events.MessageReceived += (sender, e) => { receivedData = Encoding.UTF8.GetString(e.Data); signal.Set(); };
            });

            try
            {
                Dictionary<string, object> bad = new Dictionary<string, object> { { "bad", new AotUnregisteredPayload { Value = "x" } } };
                NotSupportedException e = await TestAssert.ThrowsAsync<NotSupportedException>(() => client.SendAsync("never sent", bad));
                TestAssert.True(e.Message.Contains(nameof(AotUnregisteredPayload)), "Error should name the unregistered type: " + e.Message);

                await Task.Delay(200);
                TestAssert.True(client.Connected, "A serialization failure must not disconnect the client.");
                TestAssert.Single(server.ListClients(), "Server should still see the client.");
                TestAssert.True(receivedData == null, "No partial message should have reached the server.");

                TestAssert.True(await client.SendAsync("after failure"), "Subsequent valid send should succeed.");
                WaitForSignal(signal, 5000);
                TestAssert.Equal("after failure", receivedData);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotStrictServerUnregisteredMetadataThrowsAndConnectionSurvives()
        {
            string receivedData = null;
            ManualResetEvent signal = new ManualResetEvent(false);

            var (server, client) = await StartStrictPairAsync(configureClient: c =>
            {
                c.Events.MessageReceived += (sender, e) => { receivedData = Encoding.UTF8.GetString(e.Data); signal.Set(); };
            });

            try
            {
                Guid guid = server.ListClients().Single().Guid;
                Dictionary<string, object> bad = new Dictionary<string, object> { { "bad", new AotUnregisteredPayload() } };
                await TestAssert.ThrowsAsync<NotSupportedException>(() => server.SendAsync(guid, "never sent", bad));

                await Task.Delay(200);
                TestAssert.True(client.Connected, "A server-side serialization failure must not disconnect the client.");
                TestAssert.True(await server.SendAsync(guid, "after failure"), "Subsequent valid send should succeed.");
                WaitForSignal(signal, 5000);
                TestAssert.Equal("after failure", receivedData);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotStrictClientSendAndWaitUnregisteredMetadataThrows()
        {
            var (server, client) = await StartStrictPairAsync(configureServer: s =>
            {
                SetupDefaultServerHandlers(s);
                s.Callbacks.SyncRequestReceivedAsync = req => Task.FromResult(new SyncResponse(req, "ok"));
            });

            try
            {
                Dictionary<string, object> bad = new Dictionary<string, object> { { "bad", new AotUnregisteredPayload() } };
                await TestAssert.ThrowsAsync<NotSupportedException>(() => client.SendAndWaitAsync(2000, "req", bad));
                TestAssert.True(client.Connected, "Client should remain connected.");

                SyncResponse resp = await client.SendAndWaitAsync(5000, "req");
                TestAssert.Equal("ok", Encoding.UTF8.GetString(resp.Data));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotStrictServerSendAndWaitUnregisteredMetadataThrows()
        {
            var (server, client) = await StartStrictPairAsync(configureClient: c =>
            {
                SetupDefaultClientHandlers(c);
                c.Callbacks.SyncRequestReceivedAsync = req => Task.FromResult(new SyncResponse(req, "ok"));
            });

            try
            {
                Guid guid = server.ListClients().Single().Guid;
                Dictionary<string, object> bad = new Dictionary<string, object> { { "bad", new AotUnregisteredPayload() } };
                await TestAssert.ThrowsAsync<NotSupportedException>(() => server.SendAndWaitAsync(2000, guid, "req", bad));

                SyncResponse resp = await server.SendAndWaitAsync(5000, guid, "req");
                TestAssert.Equal("ok", Encoding.UTF8.GetString(resp.Data));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotStrictSyncResponseUnregisteredMetadataReportedNotDisconnected()
        {
            Exception serverException = null;
            ManualResetEvent exceptionSignal = new ManualResetEvent(false);
            int calls = 0;

            var (server, client) = await StartStrictPairAsync(configureServer: s =>
            {
                SetupDefaultServerHandlers(s);
                s.Events.ExceptionEncountered += (sender, e) =>
                {
                    if (e.Exception is NotSupportedException) { serverException = e.Exception; exceptionSignal.Set(); }
                };
                s.Callbacks.SyncRequestReceivedAsync = req =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                        return Task.FromResult(new SyncResponse(req, new Dictionary<string, object> { { "bad", new AotUnregisteredPayload() } }, "bad"));
                    return Task.FromResult(new SyncResponse(req, "good"));
                };
            });

            try
            {
                await TestAssert.ThrowsAsync<TimeoutException>(() => client.SendAndWaitAsync(1500, "first"));
                WaitForSignal(exceptionSignal, 5000, "Server should raise ExceptionEncountered for the unserializable sync response.");
                TestAssert.True(serverException.Message.Contains(nameof(AotUnregisteredPayload)));
                TestAssert.True(client.Connected, "Client should remain connected.");

                SyncResponse resp = await client.SendAndWaitAsync(5000, "second");
                TestAssert.Equal("good", Encoding.UTF8.GetString(resp.Data));
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotJitHeaderSerializationFailureDoesNotDisconnect()
        {
            // Regression: previously any header serialization failure was treated as a transport failure and
            // disconnected the client, even with reflection available.  NaN cannot be represented in JSON.
            string receivedData = null;
            ManualResetEvent signal = new ManualResetEvent(false);
            int port = GetNextPort();

            var server = new WatsonTcpServer(_hostname, port);
            server.Events.MessageReceived += (s, e) => { receivedData = Encoding.UTF8.GetString(e.Data); signal.Set(); };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            SetupDefaultClientHandlers(client);
            client.Connect();
            await WaitForClientConnectedAsync(client, server, 1);

            try
            {
                await TestAssert.ThrowsAsync<ArgumentException>(() => client.SendAsync("x", new Dictionary<string, object> { { "nan", double.NaN } }));
                TestAssert.True(client.Connected, "Client should remain connected after a header serialization failure.");
                TestAssert.True(await client.SendAsync("still alive"));
                WaitForSignal(signal, 5000);
                TestAssert.Equal("still alive", receivedData);
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        public static async Task AotStrictHandshakeUnregisteredMetadataFailsConnection()
        {
            int port = GetNextPort();
            Exception handshakeError = null;

            var server = new WatsonTcpServer(_hostname, port);
            server.SerializationHelper = CreateStrictHelper();
            SetupDefaultServerHandlers(server);
            server.Settings.HandshakeTimeoutMs = 2000;
            server.Callbacks.HandshakeAsync = async (session, token) =>
            {
                await session.ReceiveAsync(token);
                return HandshakeResult.Succeed();
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            var client = new WatsonTcpClient(_hostname, port);
            client.SerializationHelper = CreateStrictHelper();
            SetupDefaultClientHandlers(client);
            client.Callbacks.HandshakeAsync = async (session, token) =>
            {
                try
                {
                    await session.SendAsync(new HandshakeMessage { Type = "hello", Metadata = new Dictionary<string, object> { { "bad", new AotUnregisteredPayload() } } }, token);
                }
                catch (Exception e)
                {
                    handshakeError = e;
                    throw;
                }

                return HandshakeResult.Succeed();
            };

            try
            {
                try { client.Connect(); } catch (Exception) { }
                await Task.Delay(250);
                TestAssert.True(handshakeError is NotSupportedException, "Handshake send should fail with NotSupportedException, got " + (handshakeError?.GetType().Name ?? "none") + ".");
                TestAssert.False(client.Connected, "Client must not report connected after a failed handshake.");
            }
            finally
            {
                SafeDispose(client);
                SafeDispose(server);
            }
        }

        #endregion

        #region Raw-Peer-Wire-Compatibility

        public static async Task AotRawPeerReceivesUnchangedHeaderFormat()
        {
            int port = GetNextPort();
            Guid rawGuid = Guid.NewGuid();
            ManualResetEvent connected = new ManualResetEvent(false);

            var server = new WatsonTcpServer(_hostname, port);
            server.SerializationHelper = CreateStrictHelper();
            SetupDefaultServerHandlers(server);
            server.Events.ClientConnected += (s, e) => connected.Set();
            server.Start();
            await WaitForServerListeningAsync(server);

            using TcpClient raw = new TcpClient();
            await raw.ConnectAsync(_hostname, port);
            NetworkStream ns = raw.GetStream();

            try
            {
                await WriteRawFrameAsync(ns, "{\"len\":0,\"status\":\"RegisterClient\",\"SenderGuid\":\"" + rawGuid + "\"}");
                WaitForSignal(connected, 5000, "Server should accept a raw peer registration.");
                await WaitForConditionAsync(() => server.ListClients().Any(c => c.Guid == rawGuid), 5000, "Server should track the raw peer by its GUID.");

                TestAssert.True(await server.SendAsync(rawGuid, "raw hello", new Dictionary<string, object> { { "k", "v" }, { "n", 1 } }));

                byte[] header = await ReadRawFrameAsync(ns, h => h.TryGetProperty("status", out JsonElement st) && st.GetString() == "Normal");
                using JsonDocument doc = JsonDocument.Parse(header);
                JsonElement root = doc.RootElement;

                TestAssert.Equal(9L, root.GetProperty("len").GetInt64());
                TestAssert.Equal("Normal", root.GetProperty("status").GetString());
                TestAssert.Equal("v", root.GetProperty("md").GetProperty("k").GetString());
                TestAssert.Equal(1, root.GetProperty("md").GetProperty("n").GetInt32());
                TestAssert.True(root.TryGetProperty("ts", out _), "Timestamp should be present.");
                TestAssert.True(root.TryGetProperty("convguid", out _), "Conversation GUID should be present.");
                TestAssert.False(Encoding.UTF8.GetString(header).Contains("\n"), "Wire header must be compact.");
            }
            finally
            {
                SafeDispose(server);
            }
        }

        public static async Task AotRawPeerHandcraftedHeadersAccepted()
        {
            int port = GetNextPort();
            Guid rawGuid = Guid.NewGuid();
            ManualResetEvent connected = new ManualResetEvent(false);
            List<(string Data, Dictionary<string, object> Metadata)> received = new List<(string, Dictionary<string, object>)>();
            object receivedLock = new object();

            var server = new WatsonTcpServer(_hostname, port);
            server.SerializationHelper = CreateStrictHelper();
            server.Events.ClientConnected += (s, e) => connected.Set();
            server.Events.MessageReceived += (s, e) =>
            {
                lock (receivedLock) received.Add((Encoding.UTF8.GetString(e.Data), e.Metadata));
            };
            server.Start();
            await WaitForServerListeningAsync(server);

            using TcpClient raw = new TcpClient();
            await raw.ConnectAsync(_hostname, port);
            NetworkStream ns = raw.GetStream();

            try
            {
                await WriteRawFrameAsync(ns, "{\"len\":0,\"status\":\"RegisterClient\",\"SenderGuid\":\"" + rawGuid + "\"}");
                WaitForSignal(connected, 5000);

                // String status with metadata, numeric status, minimal header, and unknown extra properties.
                await WriteRawFrameAsync(ns, "{\"len\":5,\"status\":\"Normal\",\"md\":{\"from\":\"raw\",\"n\":[1,2]}}", Encoding.UTF8.GetBytes("first"));
                await WriteRawFrameAsync(ns, "{\"len\":6,\"status\":0}", Encoding.UTF8.GetBytes("second"));
                await WriteRawFrameAsync(ns, "{\"len\":5}", Encoding.UTF8.GetBytes("third"));
                await WriteRawFrameAsync(ns, "{\"len\":6,\"status\":\"Normal\",\"x-extension\":{\"a\":1}}", Encoding.UTF8.GetBytes("fourth"));

                await WaitForConditionAsync(() => { lock (receivedLock) return received.Count == 4; }, 5000, "All handcrafted frames should be received.");

                lock (receivedLock)
                {
                    TestAssert.Equal("first", received[0].Data);
                    TestAssert.Equal("raw", ((JsonElement)received[0].Metadata["from"]).GetString());
                    TestAssert.Equal(2, ((JsonElement)received[0].Metadata["n"]).GetArrayLength());
                    TestAssert.Equal("second", received[1].Data);
                    TestAssert.Equal("third", received[2].Data);
                    TestAssert.Equal("fourth", received[3].Data);
                }
            }
            finally
            {
                SafeDispose(server);
            }
        }

        #endregion
    }
}
