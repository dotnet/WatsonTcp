using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Serialization helper.
    /// </summary>
    public interface ISerializationHelper
    {
        /// <summary>
        /// Deserialize from JSON to an object of the specified type.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="json">JSON string.</param>
        /// <returns>Instance.</returns>
        T DeserializeJson<T>(string json);

        /// <summary>
        /// Serialize from object to JSON.
        /// </summary>
        /// <param name="obj">Object.</param>
        /// <param name="pretty">Pretty print.</param>
        /// <returns>JSON.</returns>
        string SerializeJson(object obj, bool pretty = true);

        /// <summary>
        /// Instantiation method to support fixups for various environments, e.g. Unity.
        /// </summary>
        void InstantiateConverter();
    }
}