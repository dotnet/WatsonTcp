using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace WatsonTcp
{
    /// <summary>
    /// Stores the parameters for the <see cref="SslStream"/> used by clients.
    /// </summary>
    public class WatsonTcpClientSslConfiguration
    {
        #region Public-Members

        /// <summary>
        /// Gets or sets a <see cref="LocalCertificateSelectionCallback"/> delegate responsible for
        /// selecting the certificate used for authentication.
        /// </summary>
        /// <remarks>The default delegate returns the first certificate in the collection</remarks>
        public LocalCertificateSelectionCallback ClientCertificateSelectionCallback
        {
            get
            {
                if (_ClientCertSelectionCallback == null)
                    _ClientCertSelectionCallback = DefaultSelectClientCertificate;

                return _ClientCertSelectionCallback;
            }

            set
            {
                _ClientCertSelectionCallback = value;
            }
        }

        /// <summary>
        /// Gets or sets a <see cref="RemoteCertificateValidationCallback"/> delegate responsible
        /// for validating the certificate supplied by the remote party.
        /// </summary>
        /// <remarks>
        /// The default delegate returns true for all certificates
        /// </remarks>
        public RemoteCertificateValidationCallback ServerCertificateValidationCallback
        {
            get
            {
                if (_ServerCertValidationCallback == null)
                    _ServerCertValidationCallback = DefaultValidateServerCertificate;

                return _ServerCertValidationCallback;
            }

            set
            {
                _ServerCertValidationCallback = value;
            }
        }
        #endregion

        #region Private-Members

        private LocalCertificateSelectionCallback _ClientCertSelectionCallback;
        private RemoteCertificateValidationCallback _ServerCertValidationCallback;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of <see cref="WatsonTcpClientSslConfiguration"/>.
        /// </summary>
        public WatsonTcpClientSslConfiguration()
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="WatsonTcpClientSslConfiguration"/>
        /// that stores the parameters copied from another configuration.
        /// </summary>
        /// <param name="configuration">
        /// A <see cref="WatsonTcpClientSslConfiguration"/> from which to copy.
        /// </param>
        /// <exception cref="ArgumentNullException" />
        public WatsonTcpClientSslConfiguration(WatsonTcpClientSslConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException("Can not copy from null client SSL configuration");

            _ClientCertSelectionCallback = configuration._ClientCertSelectionCallback;
            _ServerCertValidationCallback = configuration._ServerCertValidationCallback;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        private static X509Certificate DefaultSelectClientCertificate(
          object sender,
          string targetHost,
          X509CertificateCollection clientCertificates,
          X509Certificate serverCertificate,
          string[] acceptableIssuers
        )
        {
            if (clientCertificates == null || clientCertificates.Count == 0)
            {
                return null;
            }

            return clientCertificates[0];
        }

        private static bool DefaultValidateServerCertificate(
          object sender,
          X509Certificate certificate,
          X509Chain chain,
          SslPolicyErrors sslPolicyErrors
        )
        {
            return true;
        }

        #endregion
    }
}