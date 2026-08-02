namespace Test.Shared
{
    using System.Collections.Generic;

    /// <summary>
    /// A single recorded metric measurement: instrument name, numeric value, and tag map.
    /// </summary>
    internal sealed class MetricMeasurement
    {
        internal string Name { get; }
        internal double Value { get; }
        internal Dictionary<string, string> Tags { get; }

        internal MetricMeasurement(string name, double value, Dictionary<string, string> tags)
        {
            Name = name;
            Value = value;
            Tags = tags;
        }
    }
}
