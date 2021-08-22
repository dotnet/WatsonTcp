using System;
using System.Security.Authentication;

namespace WatsonTcp
{
    static class TlsExtensions
    {
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
                    throw new ArgumentOutOfRangeException($"Unsupported TLS version {tlsVersion}");
            }
        }
    }
}
