using Newtonsoft.Json.Converters;
using System;

namespace WatsonTcp
{
    /// <summary>
    /// Watson Utility Class
    /// </summary>
    public static class WatsonUtils
    {
        /// <summary>
        /// This function is for prevent from error by Newtonsoft.Json
        /// Use this for Unity Builds
        /// </summary>
        public static void FixJsonUnity()
        {
            try
            {
                Activator.CreateInstance<StringEnumConverter>();
            }
            catch (Exception exc)
            {
                throw new InvalidOperationException("FixJsonUnity function error.", exc);
            }
        }
    }
}
