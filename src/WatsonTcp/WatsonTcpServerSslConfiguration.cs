using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace WatsonTcp
{
    /// <summary>
    /// Stores the parameters for the <see cref="SslStream"/> used by servers.
    /// </summary>
    public class WatsonTcpServerSslConfiguration
    {
        #region Public-Members

        /// <summary>
        /// Gets or sets a value indicating whether the client is asked for
        /// a certificate for authentication.
        /// </summary>
        public bool ClientCertificateRequired
        {
            get
            {
                return _ClientCertRequired;
            }

            set
            {
                _ClientCertRequired = value;
            }
        }

        /// <summary>
        /// Gets or sets a <see cref="RemoteCertificateValidationCallback"/> delegate responsible
        /// for validating the certificate supplied by the remote party.
        /// </summary>
        /// <remarks>
        /// The default delegate returns true for all certificates
        /// </remarks>
        public RemoteCertificateValidationCallback ClientCertificateValidationCallback
        {
            get
            {
                if (_ClientCertValidationCallback == null)
                    _ClientCertValidationCallback = DefaultValidateClientCertificate;

                return _ClientCertValidationCallback;
            }

            set
            {
                _ClientCertValidationCallback = value;
            }
        }

        #endregion

        #region Private-Members

        private bool _ClientCertRequired = true;
        private RemoteCertificateValidationCallback _ClientCertValidationCallback;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of <see cref="WatsonTcpServerSslConfiguration"/>.
        /// </summary>
        public WatsonTcpServerSslConfiguration()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WatsonTcpServerSslConfiguration"/>
        /// class that stores the parameters copied from another configuration.
        /// </summary>
        /// <param name="configuration">
        /// A <see cref="WatsonTcpServerSslConfiguration"/> from which to copy.
        /// </param>
        /// <exception cref="ArgumentNullException"/>
        public WatsonTcpServerSslConfiguration(WatsonTcpServerSslConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException("Can not copy from null server SSL configuration");

            _ClientCertRequired = configuration._ClientCertRequired;
            _ClientCertValidationCallback = configuration._ClientCertValidationCallback;
        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        private static bool DefaultValidateClientCertificate(
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