using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Event arguments for when an exception is encountered. 
    /// </summary>
    public class ExceptionEventArgs
    {
        internal ExceptionEventArgs(Exception e, string json)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            Exception = e;
            Json = json;
        }

        /// <summary>
        /// Exception.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// JSON representation of the exception.
        /// </summary>
        public string Json { get; }
    }
}
