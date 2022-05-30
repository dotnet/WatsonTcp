using System;
using System.Security.Authentication;

namespace WatsonTcp
{
    /// <summary>
    /// TLS extensions.
    /// </summary>
    public static class TlsExtensions
    {
        /// <summary>
        /// TLS version to SSL protocol version.
        /// </summary>
        /// <param name="tlsVersion"></param>
        /// <returns></returns>
        public static SslProtocols ToSslProtocols(this TlsVersion tlsVersion)
        {
            switch (tlsVersion)
            {
                case TlsVersion.Tls12:
                    return SslProtocols.Tls12;
#if NET5_0_OR_GREATER
                case TlsVersion.Tls13:
                    return SslProtocols.Tls13;
#endif
                default: 
                    throw new ArgumentOutOfRangeException($"Unsupported TLS version {tlsVersion}.");
            }
        }
    }
}
