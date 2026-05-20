namespace WatsonTcp
{
    using System;
    using System.Buffers;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Default serialization helper.
    /// </summary>
    public class DefaultSerializationHelper : ISerializationHelper
    {
        #region Private-Members

        private readonly ExceptionConverter<Exception> _ExceptionConverter = new ExceptionConverter<Exception>();
        private readonly NameValueCollectionConverter _NameValueCollectionConverter = new NameValueCollectionConverter();
        private readonly JsonSerializerOptions _CompactJsonOptions = null;
        private readonly JsonSerializerOptions _PrettyJsonOptions = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DefaultSerializationHelper()
        {
            InstantiateConverter();
            _CompactJsonOptions = CreateJsonSerializerOptions(false);
            _PrettyJsonOptions = CreateJsonSerializerOptions(true);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Deserialize JSON to an instance.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="json">JSON string.</param>
        /// <returns>Instance.</returns>
        public T DeserializeJson<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, _CompactJsonOptions);
        }

        /// <summary>
        /// Serialize object to JSON.
        /// </summary>
        /// <param name="obj">Object.</param>
        /// <param name="pretty">Pretty print.</param>
        /// <returns>JSON.</returns>
        public string SerializeJson(object obj, bool pretty = true)
        {
            if (obj == null) return null;
            return JsonSerializer.Serialize(obj, obj.GetType(), pretty ? _PrettyJsonOptions : _CompactJsonOptions);
        }

        /// <summary>
        /// Instantiation method to support fixups for various environments, e.g. Unity.
        /// </summary>
        public void InstantiateConverter()
        {
            try
            {
                Activator.CreateInstance<JsonStringEnumConverter>();
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Internal-Methods

        internal T DeserializeJson<T>(ReadOnlySpan<byte> json)
        {
            return JsonSerializer.Deserialize<T>(json, _CompactJsonOptions);
        }

        internal byte[] SerializeJsonBytes(object obj, bool pretty = true)
        {
            if (obj == null) return Array.Empty<byte>();
            return JsonSerializer.SerializeToUtf8Bytes(obj, obj.GetType(), pretty ? _PrettyJsonOptions : _CompactJsonOptions);
        }

        internal void SerializeJson(object obj, IBufferWriter<byte> bufferWriter, bool pretty = true)
        {
            if (obj == null) return;
            if (bufferWriter == null) throw new ArgumentNullException(nameof(bufferWriter));

            using (Utf8JsonWriter writer = new Utf8JsonWriter(bufferWriter))
            {
                JsonSerializer.Serialize(writer, obj, obj.GetType(), pretty ? _PrettyJsonOptions : _CompactJsonOptions);
                writer.Flush();
            }
        }

        #endregion

        #region Private-Methods

        private JsonSerializerOptions CreateJsonSerializerOptions(bool pretty)
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = pretty
            };

            // see https://github.com/dotnet/runtime/issues/43026
            options.Converters.Add(_ExceptionConverter);
            options.Converters.Add(_NameValueCollectionConverter);

            return options;
        }

        #endregion

        #region Private-Classes

        private sealed class ExceptionConverter<TExceptionType> : JsonConverter<TExceptionType>
        {
            public override bool CanConvert(Type typeToConvert)
            {
                return typeof(Exception).IsAssignableFrom(typeToConvert);
            }

            public override TExceptionType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotSupportedException("Deserializing exceptions is not allowed");
            }

            public override void Write(Utf8JsonWriter writer, TExceptionType value, JsonSerializerOptions options)
            {
                var serializableProperties = value.GetType()
                    .GetProperties()
                    .Select(uu => new { uu.Name, Value = uu.GetValue(value) })
                    .Where(uu => uu.Name != nameof(Exception.TargetSite));

                if (options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull)
                {
                    serializableProperties = serializableProperties.Where(uu => uu.Value != null);
                }

                var propList = serializableProperties.ToList();

                if (propList.Count == 0)
                {
                    return;
                }

                writer.WriteStartObject();

                foreach (var prop in propList)
                {
                    writer.WritePropertyName(prop.Name);
                    JsonSerializer.Serialize(writer, prop.Value, options);
                }

                writer.WriteEndObject();
            }
        }

        private sealed class NameValueCollectionConverter : JsonConverter<NameValueCollection>
        {
            public override NameValueCollection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotImplementedException();

            public override void Write(Utf8JsonWriter writer, NameValueCollection value, JsonSerializerOptions options)
            {
                var val = value.Keys.Cast<string>()
                    .ToDictionary(k => k, k => string.Join(", ", value.GetValues(k)));
                JsonSerializer.Serialize(writer, val);
            }
        }

        #endregion
    }
}
