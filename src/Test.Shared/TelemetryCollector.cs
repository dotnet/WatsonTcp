namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Linq;

    /// <summary>
    /// Collects WatsonTcp metrics and spans using only the base class library
    /// (<see cref="MeterListener"/> and <see cref="ActivityListener"/>), so the shared test project
    /// takes no dependency on the OpenTelemetry SDK.  Construct one before creating the WatsonTcp
    /// client/server under test so that instrument publication is captured, and dispose it when done.
    /// </summary>
    internal sealed class TelemetryCollector : IDisposable
    {
        #region Private-Members

        private readonly object _Lock = new object();
        private readonly List<MetricMeasurement> _Measurements = new List<MetricMeasurement>();
        private readonly List<Activity> _Activities = new List<Activity>();
        private readonly MeterListener _MeterListener;
        private readonly ActivityListener _ActivityListener;

        #endregion

        #region Constructors-and-Factories

        internal TelemetryCollector(string meterName = "WatsonTcp", string activitySourceName = "WatsonTcp")
        {
            if (String.IsNullOrEmpty(meterName)) throw new ArgumentNullException(nameof(meterName));
            if (String.IsNullOrEmpty(activitySourceName)) throw new ArgumentNullException(nameof(activitySourceName));

            _MeterListener = new MeterListener();
            _MeterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == meterName) listener.EnableMeasurementEvents(instrument);
            };
            _MeterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
                Record(instrument.Name, measurement, tags));
            _MeterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
                Record(instrument.Name, measurement, tags));
            _MeterListener.Start();

            _ActivityListener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == activitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
                SampleUsingParentId = (ref ActivityCreationOptions<string> options) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    lock (_Lock)
                    {
                        _Activities.Add(activity);
                    }
                }
            };
            ActivitySource.AddActivityListener(_ActivityListener);
        }

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Force collection of all observable (gauge) instruments so their latest values are recorded.
        /// </summary>
        internal void CollectObservable()
        {
            _MeterListener.RecordObservableInstruments();
        }

        /// <summary>
        /// Sum the values of all recorded measurements for the named instrument that match the supplied tags.
        /// </summary>
        internal double Sum(string name, params string[] tagKeyValuePairs)
        {
            Dictionary<string, string> filter = BuildFilter(tagKeyValuePairs);
            lock (_Lock)
            {
                return _Measurements
                    .Where(m => m.Name == name && Matches(m, filter))
                    .Sum(m => m.Value);
            }
        }

        /// <summary>
        /// Count the recorded measurements for the named instrument that match the supplied tags.
        /// </summary>
        internal int Count(string name, params string[] tagKeyValuePairs)
        {
            Dictionary<string, string> filter = BuildFilter(tagKeyValuePairs);
            lock (_Lock)
            {
                return _Measurements.Count(m => m.Name == name && Matches(m, filter));
            }
        }

        /// <summary>
        /// Return the latest recorded value for the named instrument matching the supplied tags, or zero.
        /// </summary>
        internal double Latest(string name, params string[] tagKeyValuePairs)
        {
            Dictionary<string, string> filter = BuildFilter(tagKeyValuePairs);
            lock (_Lock)
            {
                MetricMeasurement match = _Measurements.LastOrDefault(m => m.Name == name && Matches(m, filter));
                return match == null ? 0.0 : match.Value;
            }
        }

        /// <summary>
        /// Total number of metric measurements recorded across all instruments.
        /// </summary>
        internal int TotalMeasurements()
        {
            lock (_Lock)
            {
                return _Measurements.Count;
            }
        }

        /// <summary>
        /// Distinct tag keys observed across all recorded measurements.
        /// </summary>
        internal IReadOnlyCollection<string> AllTagKeys()
        {
            lock (_Lock)
            {
                HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (MetricMeasurement measurement in _Measurements)
                {
                    foreach (string key in measurement.Tags.Keys) keys.Add(key);
                }

                return keys;
            }
        }

        /// <summary>
        /// Number of stopped spans recorded with the given name.
        /// </summary>
        internal int SpanCount(string spanName)
        {
            lock (_Lock)
            {
                return _Activities.Count(a => a.OperationName == spanName);
            }
        }

        /// <summary>
        /// Total number of stopped spans recorded.
        /// </summary>
        internal int TotalSpans()
        {
            lock (_Lock)
            {
                return _Activities.Count;
            }
        }

        /// <summary>
        /// Return true if any recorded span with the given name carries a tag with the supplied key.
        /// </summary>
        internal bool AnySpanHasTag(string spanName, string tagKey)
        {
            lock (_Lock)
            {
                // Activity.Tags only surfaces string-valued tags; TagObjects includes non-string values too.
                return _Activities.Any(a => a.OperationName == spanName
                    && a.TagObjects.Any(t => t.Key == tagKey));
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _MeterListener.Dispose();
            _ActivityListener.Dispose();
        }

        #endregion

        #region Private-Methods

        private void Record(string name, double value, ReadOnlySpan<KeyValuePair<string, object>> tags)
        {
            Dictionary<string, string> tagMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> tag in tags)
            {
                tagMap[tag.Key] = tag.Value == null ? String.Empty : tag.Value.ToString();
            }

            lock (_Lock)
            {
                _Measurements.Add(new MetricMeasurement(name, value, tagMap));
            }
        }

        private static Dictionary<string, string> BuildFilter(string[] tagKeyValuePairs)
        {
            Dictionary<string, string> filter = new Dictionary<string, string>(StringComparer.Ordinal);
            if (tagKeyValuePairs == null) return filter;
            if (tagKeyValuePairs.Length % 2 != 0)
            {
                throw new ArgumentException("Tag key/value pairs must be supplied in pairs.", nameof(tagKeyValuePairs));
            }

            for (int i = 0; i < tagKeyValuePairs.Length; i += 2)
            {
                filter[tagKeyValuePairs[i]] = tagKeyValuePairs[i + 1];
            }

            return filter;
        }

        private static bool Matches(MetricMeasurement measurement, Dictionary<string, string> filter)
        {
            foreach (KeyValuePair<string, string> expected in filter)
            {
                if (!measurement.Tags.TryGetValue(expected.Key, out string actual)) return false;
                if (!String.Equals(actual, expected.Value, StringComparison.Ordinal)) return false;
            }

            return true;
        }

        #endregion
    }
}
