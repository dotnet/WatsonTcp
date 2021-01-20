using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Settings for Watson TCP server.
    /// </summary>
    public class WatsonTcpServerSettings
    {
        #region Public-Members

        /// <summary>
        /// Buffer size to use when reading input and output streams.  Default is 65536.  Value must be greater than zero.
        /// </summary>
        public int StreamBufferSize
        {
            get
            {
                return _StreamBufferSize;
            }
            set
            {
                if (value < 1) throw new ArgumentException("Read stream buffer size must be greater than zero.");
                _StreamBufferSize = value;
            }
        }

        /// <summary>
        /// Maximum content length for streams that are proxied through a MemoryStream.
        /// If the content length exceeds this value, the underlying DataStream will be passed in the StreamReceived event.
        /// Value must be greater than zero.
        /// </summary>
        public int MaxProxiedStreamSize
        {
            get
            {
                return _MaxProxiedStreamSize;
            }
            set
            {
                if (value < 1) throw new ArgumentException("MaxProxiedStreamSize must be greater than zero.");
                _MaxProxiedStreamSize = value;
            }
        }

        /// <summary>
        /// Enable or disable message debugging.  Requires `Logger` to be set.
        /// WARNING: Setting this value to true will emit a large number of log messages with a large amount of data.
        /// </summary>
        public bool DebugMessages = false;

        /// <summary>
        /// Method to invoke when sending a log message.
        /// </summary>
        public Action<Severity, string> Logger = null;

        /// <summary>
        /// Enable acceptance of SSL certificates from clients that cannot be validated.
        /// </summary>
        public bool AcceptInvalidCertificates = true;

        /// <summary>
        /// Require mutual authentication between SSL clients and this server.
        /// </summary>
        public bool MutuallyAuthenticate = false;

        /// <summary>
        /// Preshared key that must be consistent between clients and this server.
        /// </summary>
        public string PresharedKey = null;

        /// <summary>
        /// For Watson TCP server, the maximum amount of time to wait before considering a client idle and disconnecting them. 
        /// By default, this value is set to 0, which will never disconnect a client due to inactivity.
        /// The timeout is reset any time a message is received from a client or a message is sent to a client.
        /// For instance, if you set this value to 30, the client will be disconnected if the server has not received a message from the client within 30 seconds or if a message has not been sent to the client in 30 seconds.
        /// Value must be zero or greater.
        /// </summary>
        public int IdleClientTimeoutSeconds
        {
            get
            {
                return _IdleClientTimeoutSeconds;
            }
            set
            {
                if (value < 0) throw new ArgumentException("IdleClientTimeoutSeconds must be zero or greater.");
                _IdleClientTimeoutSeconds = value;
            }
        }

        /// <summary>
        /// For Watson TCP server, specify the maximum number of connections the server will accept.
        /// Default is 4096.  Value must be greater than zero.
        /// </summary>
        public int MaxConnections
        {
            get
            {
                return _MaxConnections;
            }
            set
            {
                if (value < 1) throw new ArgumentException("Max connections must be greater than zero.");
                _MaxConnections = value;
            }
        }

        /// <summary>
        /// For Watson TCP server, the list of permitted IP addresses from which connections can be received.
        /// </summary>
        public List<string> PermittedIPs
        {
            get
            {
                return _PermittedIPs;
            }
            set
            {
                if (value == null) _PermittedIPs = new List<string>();
                else _PermittedIPs = value;
            }
        }

        #endregion

        #region Private-Members

        private int _StreamBufferSize = 65536;
        private int _MaxProxiedStreamSize = 67108864;

        private int _MaxConnections = 4096;
        private int _IdleClientTimeoutSeconds = 0;
        private List<string> _PermittedIPs = new List<string>();

        #endregion 

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        public WatsonTcpServerSettings()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
