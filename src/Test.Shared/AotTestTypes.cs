namespace Test.Shared
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Custom metadata type registered in <see cref="AotTestJsonContext"/>.
    /// </summary>
    public sealed class AotCustomPayload
    {
        public string Name { get; set; }

        public int Count { get; set; }
    }

    /// <summary>
    /// Custom metadata type intentionally NOT registered in any source-generated context.
    /// </summary>
    public sealed class AotUnregisteredPayload
    {
        public string Value { get; set; }
    }

    /// <summary>
    /// Caller-supplied source-generated context, as an AOT application would provide to WatsonTcp.
    /// </summary>
    [JsonSerializable(typeof(AotCustomPayload))]
    internal partial class AotTestJsonContext : JsonSerializerContext
    {
    }
}
