using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Response to a synchronous request.
    /// </summary>
    public class SyncResponse
    { 
        /// <summary>
        /// Metadata to attach to the response.
        /// </summary>
        public Dictionary<object, object> Metadata { get; }

        /// <summary>
        /// Data to attach to the response.
        /// </summary>
        public byte[] Data { get; }
         
        internal DateTime? ExpirationUtc { get; set; }

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        /// <param name="data">Data to send as a response.</param>
        public SyncResponse(string data)
        {
            Metadata = new Dictionary<object, object>();
            if (String.IsNullOrEmpty(data))
            {
                Data = new byte[0];
            }
            else
            {
                Data = Encoding.UTF8.GetBytes(data);
            }
        }

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        /// <param name="data">Data to send as a response.</param>
        public SyncResponse(byte[] data)
        {
            Metadata = new Dictionary<object, object>();
            Data = data;
        }

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        /// <param name="metadata">Metadata to attach to the response.</param>
        /// <param name="data">Data to send as a response.</param>
        public SyncResponse(Dictionary<object, object> metadata, string data)
        { 
            Metadata = metadata;

            if (String.IsNullOrEmpty(data))
            {
                Data = new byte[0];
            }
            else
            {
                Data = Encoding.UTF8.GetBytes(data);
            }
        }

        /// <summary>
        /// Instantiate the object.
        /// </summary> 
        /// <param name="metadata">Metadata to attach to the response.</param>
        /// <param name="data">Data to send as a response.</param>
        public SyncResponse(Dictionary<object, object> metadata, byte[] data)
        {
            Metadata = metadata;
            Data = data;
        }
    }
}
