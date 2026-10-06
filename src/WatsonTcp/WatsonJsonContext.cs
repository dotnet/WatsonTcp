namespace WatsonTcp
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Source-generated JSON metadata for every type WatsonTcp places on the wire, plus the common
    /// metadata value types that callers are expected to use.  This is what allows WatsonTcp to
    /// serialize without runtime reflection or code generation under Native AOT and trimming.
    /// Serializer behavior (null handling, indentation, converters) is governed by the options
    /// constructed in <see cref="DefaultSerializationHelper"/>, not by this context.
    /// </summary>
    [JsonSerializable(typeof(WatsonMessage))]
    [JsonSerializable(typeof(HandshakeMessage))]
    [JsonSerializable(typeof(MessageStatus))]
    [JsonSerializable(typeof(DisconnectReason))]
    [JsonSerializable(typeof(Mode))]
    [JsonSerializable(typeof(TlsVersion))]
    [JsonSerializable(typeof(object))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(byte))]
    [JsonSerializable(typeof(sbyte))]
    [JsonSerializable(typeof(short))]
    [JsonSerializable(typeof(ushort))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(uint))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(ulong))]
    [JsonSerializable(typeof(float))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(decimal))]
    [JsonSerializable(typeof(char))]
    [JsonSerializable(typeof(Guid))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(DateTimeOffset))]
    [JsonSerializable(typeof(TimeSpan))]
    [JsonSerializable(typeof(Uri))]
    [JsonSerializable(typeof(byte[]))]
    [JsonSerializable(typeof(string[]))]
    [JsonSerializable(typeof(int[]))]
    [JsonSerializable(typeof(long[]))]
    [JsonSerializable(typeof(double[]))]
    [JsonSerializable(typeof(bool[]))]
    [JsonSerializable(typeof(Guid[]))]
    [JsonSerializable(typeof(object[]))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(List<int>))]
    [JsonSerializable(typeof(List<long>))]
    [JsonSerializable(typeof(List<object>))]
    [JsonSerializable(typeof(Dictionary<string, object>))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSerializable(typeof(JsonElement))]
    [JsonSerializable(typeof(JsonDocument))]
    [JsonSerializable(typeof(JsonNode))]
    [JsonSerializable(typeof(JsonObject))]
    [JsonSerializable(typeof(JsonArray))]
    internal partial class WatsonJsonContext : JsonSerializerContext
    {
    }
}
