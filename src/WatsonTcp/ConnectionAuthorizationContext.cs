namespace WatsonTcp
{
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// Connection authorization context.
    /// </summary>
    public class ConnectionAuthorizationContext
    {
        /// <summary>
        /// Provisional client metadata.
        /// </summary>
        public ClientMetadata Client { get; }

        /// <summary>
        /// Indicates whether SSL/TLS is in use for this connection.
        /// </summary>
        public bool IsSsl { get; }

        /// <summary>
        /// Client certificate, if mutual TLS is in use.
        /// </summary>
        public X509Certificate ClientCertificate { get; }

        /// <summary>
        /// Remote endpoint expressed as IP:port.
        /// </summary>
        public string IpPort { get; }

        internal ConnectionAuthorizationContext(ClientMetadata client, bool isSsl, X509Certificate clientCertificate)
        {
            Client = client;
            IsSsl = isSsl;
            ClientCertificate = clientCertificate;
            IpPort = client?.IpPort;
        }
    }
}
