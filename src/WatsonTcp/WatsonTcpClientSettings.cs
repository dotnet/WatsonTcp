using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Settings for Watson TCP client.
    /// </summary>
    public class WatsonTcpClientSettings
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
                if (value < 1) throw new ArgumentException("Stream buffer size must be greater than zero.");
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
        /// For Watson TCP client, the number of seconds to wait before timing out a connection attempt.  Default is 5 seconds.  Value must be greater than zero.
        /// </summary>
        public int ConnectTimeoutSeconds
        {
            get
            {
                return _ConnectTimeoutSeconds;
            }
            set
            {
                if (value < 1) throw new ArgumentException("ConnectTimeoutSeconds must be greater than zero.");
                _ConnectTimeoutSeconds = value;
            }
        }

        /// <summary>
        /// Maximum amount of time to wait before considering the server to be idle and disconnecting from it. 
        /// By default, this value is set to 0, which will never disconnect due to inactivity.
        /// The timeout is reset any time a message is received from the server.
        /// For instance, if you set this value to 30000, the client will disconnect if the server has not sent a message to the client within 30 seconds.
        /// </summary>
        public int IdleServerTimeoutMs
        {
            get
            {
                return _IdleServerTimeoutMs;
            }
            set
            {
                if (value < 0) throw new ArgumentException("IdleClientTimeoutMs must be zero or greater.");
                _IdleServerTimeoutMs = value;
            }
        }

        /// <summary>
        /// Number of milliseconds to wait between each iteration of evaluating the server connection to see if the configured timeout interval has been exceeded.
        /// </summary>
        public int IdleServerEvaluationIntervalMs
        {
            get
            {
                return _IdleServerEvaluationIntervalMs;
            }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException("IdleServerEvaluationIntervalMs must be one or greater.");
                _IdleServerEvaluationIntervalMs = value;
            }
        }

        /// <summary>
        /// Local TCP port.  
        /// Set to '0' to have the underlying TcpClient implementation automatically assign.
        /// Value must be 0, or, 1024 or greater.
        /// </summary>
        public int LocalPort
        {
            get
            {
                return _LocalPort;
            }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentException("Valid values for LocalPort are 0, 1024-65535.");
                }
                else if (value > 0 && value < 1024)
                {
                    throw new ArgumentException("Valid values for LocalPort are 0, 1024-65535."); 
                }
                else if (value > 65535)
                {
                    throw new ArgumentException("Valid values for LocalPort are 0, 1024-65535.");
                }

                _LocalPort = value;
            }
        }

        /// <summary>
        /// Disable the delay when send or receive buffers are not full.  If true, disable the delay.  Default is false.
        /// </summary>
        public bool NoDelay { get; set; } = false;

        #endregion

        #region Private-Members

        private int _StreamBufferSize = 65536;
        private int _MaxProxiedStreamSize = 67108864;
        private int _ConnectTimeoutSeconds = 5;
        private int _IdleServerTimeoutMs = 0;
        private int _IdleServerEvaluationIntervalMs = 1000;
        private int _LocalPort = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        public WatsonTcpClientSettings()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Private-Methods

        #endregion
    }
}
