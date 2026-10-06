namespace WatsonTcp
{
    using System;
    using System.Buffers;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.Specialized;
#if NET
    using System.Diagnostics.CodeAnalysis;
#endif
    using System.Globalization;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.Json.Serialization.Metadata;

    /// <summary>
    /// Default serialization helper.
    /// Serialization of WatsonTcp's own wire types (message headers and handshake messages) and of common
    /// metadata value types uses source-generated metadata and is safe under Native AOT and trimming.
    /// Other types are resolved, in order, by the optional caller-supplied <see cref="IJsonTypeInfoResolver"/>
    /// and then by reflection, when reflection-based serialization is available and enabled.
    /// </summary>
    public class DefaultSerializationHelper : ISerializationHelper
    {
        #region Public-Members

        /// <summary>
        /// The caller-supplied type info resolver consulted before the built-in metadata, or null if none was supplied.
        /// Supply a source-generated <see cref="JsonSerializerContext"/> here to serialize custom metadata types under Native AOT.
        /// </summary>
        public IJsonTypeInfoResolver TypeInfoResolver { get; } = null;

        /// <summary>
        /// Indicates whether types unknown to the built-in and caller-supplied metadata fall back to reflection-based serialization.
        /// This is false when reflection was disabled through the constructor, or when the application has disabled
        /// reflection-based serialization (the default for Native AOT and trimmed applications).
        /// </summary>
        public bool ReflectionFallbackEnabled { get; } = false;

        #endregion

        #region Private-Members

        private readonly ExceptionConverter _ExceptionConverter = new ExceptionConverter();
        private readonly NameValueCollectionConverter _NameValueCollectionConverter = new NameValueCollectionConverter();
        private readonly JsonSerializerOptions _CompactJsonOptions = null;
        private readonly JsonSerializerOptions _PrettyJsonOptions = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DefaultSerializationHelper() : this(null, true)
        {
        }

        /// <summary>
        /// Instantiate with an additional type info resolver, such as a source-generated <see cref="JsonSerializerContext"/>,
        /// used to serialize custom metadata types.
        /// </summary>
        /// <param name="typeInfoResolver">Resolver consulted before the built-in metadata; may be null.</param>
        public DefaultSerializationHelper(IJsonTypeInfoResolver typeInfoResolver) : this(typeInfoResolver, true)
        {
        }

        /// <summary>
        /// Instantiate with an additional type info resolver and explicit control over reflection fallback.
        /// Passing false for <paramref name="enableReflectionFallback"/> reproduces Native AOT serialization behavior
        /// in a JIT application, which is useful for verifying AOT readiness before publishing.
        /// </summary>
        /// <param name="typeInfoResolver">Resolver consulted before the built-in metadata; may be null.</param>
        /// <param name="enableReflectionFallback">True to allow reflection-based serialization for unknown types when the runtime supports it.</param>
        public DefaultSerializationHelper(IJsonTypeInfoResolver typeInfoResolver, bool enableReflectionFallback)
        {
            TypeInfoResolver = typeInfoResolver;
            ReflectionFallbackEnabled = enableReflectionFallback && JsonSerializer.IsReflectionEnabledByDefault;

            InstantiateConverter();

            IJsonTypeInfoResolver resolver = BuildResolver(typeInfoResolver, ReflectionFallbackEnabled);
            _CompactJsonOptions = CreateJsonSerializerOptions(resolver, false);
            _PrettyJsonOptions = CreateJsonSerializerOptions(resolver, true);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Deserialize JSON to an instance.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="json">JSON string.</param>
        /// <returns>Instance.</returns>
        /// <exception cref="NotSupportedException">No JSON metadata is available for <typeparamref name="T"/>.</exception>
        public T DeserializeJson<T>(string json)
        {
            return JsonSerializer.Deserialize(json, GetTypeInfo<T>(_CompactJsonOptions));
        }

        /// <summary>
        /// Serialize object to JSON.
        /// </summary>
        /// <param name="obj">Object.</param>
        /// <param name="pretty">Pretty print.</param>
        /// <returns>JSON.</returns>
        /// <exception cref="NotSupportedException">No JSON metadata is available for the runtime type of <paramref name="obj"/>.</exception>
        public string SerializeJson(object obj, bool pretty = true)
        {
            if (obj == null) return null;

            JsonSerializerOptions options = pretty ? _PrettyJsonOptions : _CompactJsonOptions;

            if (IsSpecialType(obj))
            {
                return System.Text.Encoding.UTF8.GetString(WriteSpecial(obj, options));
            }

            return JsonSerializer.Serialize(obj, GetTypeInfo(obj.GetType(), options));
        }

        /// <summary>
        /// Instantiation method to support fixups for various environments, e.g. Unity.
        /// Directly constructs the converters WatsonTcp relies on so that ahead-of-time compilers and linkers retain them.
        /// </summary>
        public void InstantiateConverter()
        {
            try
            {
                _ = new JsonStringEnumConverter<MessageStatus>();
                _ = new JsonStringEnumConverter<DisconnectReason>();
                _ = new JsonStringEnumConverter<Mode>();
                _ = new JsonStringEnumConverter<TlsVersion>();
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Internal-Methods

        internal T DeserializeJson<T>(ReadOnlySpan<byte> json)
        {
            return JsonSerializer.Deserialize(json, GetTypeInfo<T>(_CompactJsonOptions));
        }

        internal WatsonMessage DeserializeWatsonMessage(ReadOnlySpan<byte> json)
        {
            return JsonSerializer.Deserialize(json, GetTypeInfo<WatsonMessage>(_CompactJsonOptions));
        }

        internal HandshakeMessage DeserializeHandshakeMessage(ReadOnlySpan<byte> json)
        {
            return JsonSerializer.Deserialize(json, GetTypeInfo<HandshakeMessage>(_CompactJsonOptions));
        }

        internal byte[] SerializeJsonBytes(object obj, bool pretty = true)
        {
            if (obj == null) return Array.Empty<byte>();

            JsonSerializerOptions options = pretty ? _PrettyJsonOptions : _CompactJsonOptions;

            if (IsSpecialType(obj))
            {
                return WriteSpecial(obj, options);
            }

            return JsonSerializer.SerializeToUtf8Bytes(obj, GetTypeInfo(obj.GetType(), options));
        }

        internal void SerializeJson(object obj, IBufferWriter<byte> bufferWriter, bool pretty = true)
        {
            if (obj == null) return;
            if (bufferWriter == null) throw new ArgumentNullException(nameof(bufferWriter));

            JsonSerializerOptions options = pretty ? _PrettyJsonOptions : _CompactJsonOptions;

            using (Utf8JsonWriter writer = new Utf8JsonWriter(bufferWriter))
            {
                if (IsSpecialType(obj)) WriteSpecial(writer, obj, options);
                else JsonSerializer.Serialize(writer, obj, GetTypeInfo(obj.GetType(), options));
                writer.Flush();
            }
        }

        #endregion

        #region Private-Methods

        private static IJsonTypeInfoResolver BuildResolver(IJsonTypeInfoResolver typeInfoResolver, bool reflectionFallback)
        {
            List<IJsonTypeInfoResolver> resolvers = new List<IJsonTypeInfoResolver>(3);
            if (typeInfoResolver != null) resolvers.Add(typeInfoResolver);
            resolvers.Add(WatsonJsonContext.Default);
            if (reflectionFallback) resolvers.Add(CreateReflectionResolver());
            return JsonTypeInfoResolver.Combine(resolvers.ToArray());
        }

#if NET
        [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
            Justification = "Only reached when JsonSerializer.IsReflectionEnabledByDefault is true, which the trimmer treats as false for Native AOT and trimmed applications.")]
        [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
            Justification = "Only reached when JsonSerializer.IsReflectionEnabledByDefault is true, which the trimmer treats as false for Native AOT and trimmed applications.")]
#endif
        private static IJsonTypeInfoResolver CreateReflectionResolver()
        {
            return new DefaultJsonTypeInfoResolver();
        }

        private JsonSerializerOptions CreateJsonSerializerOptions(IJsonTypeInfoResolver resolver, bool pretty)
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = pretty,
                TypeInfoResolver = resolver
            };

            // see https://github.com/dotnet/runtime/issues/43026
            options.Converters.Add(_ExceptionConverter);
            options.Converters.Add(_NameValueCollectionConverter);

            options.MakeReadOnly();
            return options;
        }

        private JsonTypeInfo<T> GetTypeInfo<T>(JsonSerializerOptions options)
        {
            return (JsonTypeInfo<T>)GetTypeInfo(typeof(T), options);
        }

        private JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            try
            {
                return options.GetTypeInfo(type);
            }
            catch (NotSupportedException e)
            {
                throw new NotSupportedException(
                    "No JSON serialization metadata is available for type '" + type.FullName + "'. " +
                    (ReflectionFallbackEnabled
                        ? "The type is not supported by System.Text.Json."
                        : "Reflection-based serialization is disabled (Native AOT, trimming, or enableReflectionFallback = false). " +
                          "Register the type in a source-generated JsonSerializerContext and supply it via new DefaultSerializationHelper(MyContext.Default)."),
                    e);
            }
        }

        private static bool IsSpecialType(object obj)
        {
            return obj is Exception || obj is NameValueCollection;
        }

        private byte[] WriteSpecial(object obj, JsonSerializerOptions options)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = options.WriteIndented }))
                {
                    WriteSpecial(writer, obj, options);
                    writer.Flush();
                }

                return ms.ToArray();
            }
        }

        private void WriteSpecial(Utf8JsonWriter writer, object obj, JsonSerializerOptions options)
        {
            if (obj is Exception e) _ExceptionConverter.Write(writer, e, options);
            else _NameValueCollectionConverter.Write(writer, (NameValueCollection)obj, options);
        }

        private static void WriteLooseValue(Utf8JsonWriter writer, object value)
        {
            switch (value)
            {
                case null: writer.WriteNullValue(); break;
                case string s: writer.WriteStringValue(s); break;
                case bool b: writer.WriteBooleanValue(b); break;
                case int i: writer.WriteNumberValue(i); break;
                case long l: writer.WriteNumberValue(l); break;
                case uint ui: writer.WriteNumberValue(ui); break;
                case ulong ul: writer.WriteNumberValue(ul); break;
                case short sh: writer.WriteNumberValue(sh); break;
                case ushort us: writer.WriteNumberValue(us); break;
                case byte by: writer.WriteNumberValue(by); break;
                case sbyte sb: writer.WriteNumberValue(sb); break;
                case float f when !float.IsNaN(f) && !float.IsInfinity(f): writer.WriteNumberValue(f); break;
                case double d when !double.IsNaN(d) && !double.IsInfinity(d): writer.WriteNumberValue(d); break;
                case decimal m: writer.WriteNumberValue(m); break;
                case Guid g: writer.WriteStringValue(g); break;
                case DateTime dt: writer.WriteStringValue(dt); break;
                case DateTimeOffset dto: writer.WriteStringValue(dto); break;
                case IFormattable fmt: writer.WriteStringValue(fmt.ToString(null, CultureInfo.InvariantCulture)); break;
                default: writer.WriteStringValue(value.ToString()); break;
            }
        }

        #endregion

        #region Private-Classes

        /// <summary>
        /// Writes exceptions using a fixed set of members rather than reflecting over the runtime type,
        /// so that exception serialization is safe under Native AOT and trimming.
        /// </summary>
        private sealed class ExceptionConverter : JsonConverter<Exception>
        {
            public override bool CanConvert(Type typeToConvert)
            {
                return typeof(Exception).IsAssignableFrom(typeToConvert);
            }

            public override Exception Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotSupportedException("Deserializing exceptions is not allowed");
            }

            public override void Write(Utf8JsonWriter writer, Exception value, JsonSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNullValue();
                    return;
                }

                bool skipNulls = options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull;

                writer.WriteStartObject();

                writer.WriteString("Type", value.GetType().FullName);
                WriteString(writer, "Message", value.Message, skipNulls);

                if (value is ArgumentException argException)
                {
                    WriteString(writer, "ParamName", argException.ParamName, skipNulls);
                }

                writer.WritePropertyName("Data");
                writer.WriteStartObject();
                if (value.Data != null)
                {
                    foreach (DictionaryEntry entry in value.Data)
                    {
                        writer.WritePropertyName(entry.Key?.ToString() ?? "");
                        WriteLooseValue(writer, entry.Value);
                    }
                }
                writer.WriteEndObject();

                if (value.InnerException != null)
                {
                    writer.WritePropertyName("InnerException");
                    Write(writer, value.InnerException, options);
                }
                else if (!skipNulls)
                {
                    writer.WriteNull("InnerException");
                }

                WriteString(writer, "HelpLink", value.HelpLink, skipNulls);
                WriteString(writer, "Source", value.Source, skipNulls);
                writer.WriteNumber("HResult", value.HResult);
                WriteString(writer, "StackTrace", value.StackTrace, skipNulls);

                writer.WriteEndObject();
            }

            private static void WriteString(Utf8JsonWriter writer, string name, string value, bool skipNulls)
            {
                if (value != null) writer.WriteString(name, value);
                else if (!skipNulls) writer.WriteNull(name);
            }
        }

        private sealed class NameValueCollectionConverter : JsonConverter<NameValueCollection>
        {
            public override NameValueCollection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotImplementedException();

            public override void Write(Utf8JsonWriter writer, NameValueCollection value, JsonSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNullValue();
                    return;
                }

                writer.WriteStartObject();
                foreach (string key in value.AllKeys)
                {
                    string[] values = value.GetValues(key);
                    writer.WriteString(key ?? "", values != null ? String.Join(", ", values) : null);
                }
                writer.WriteEndObject();
            }
        }

        #endregion
    }
}
