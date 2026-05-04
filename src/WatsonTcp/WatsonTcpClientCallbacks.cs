namespace WatsonTcp
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Watson TCP client callbacks.
    /// </summary>
    public class WatsonTcpClientCallbacks
    {
        #region Public-Members

        /// <summary>
        /// Function called when authentication is requested from the server.  Expects the 16-byte preshared key.
        /// </summary>
        public Func<string> AuthenticationRequested = null;

        /// <summary>
        /// Callback to invoke when receiving a synchronous request that demands a response.
        /// </summary>
        [Obsolete("Please migrate to async methods.")]
        public Func<SyncRequest, SyncResponse> SyncRequestReceived
        {
            get
            {
                return _SyncRequestReceived;
            }
            set
            {
                _SyncRequestReceived = value;
            }
        }

        /// <summary>
        /// Callback to invoke when receiving a synchronous request that demands a response.
        /// </summary>
        public Func<SyncRequest, Task<SyncResponse>> SyncRequestReceivedAsync
        {
            get
            {
                return _SyncRequestReceivedAsync;
            }
            set
            {
                _SyncRequestReceivedAsync = value;
            }
        }

        /// <summary>
        /// Callback to invoke when the server requests a framed handshake.
        /// </summary>
        public Func<ClientHandshakeSession, CancellationToken, Task<HandshakeResult>> HandshakeAsync
        {
            get
            {
                return _HandshakeAsync;
            }
            set
            {
                _HandshakeAsync = value;
            }
        }

        #endregion

        #region Private-Members

        private Func<SyncRequest, SyncResponse> _SyncRequestReceived = null;
        private Func<SyncRequest, Task<SyncResponse>> _SyncRequestReceivedAsync = null;
        private Func<ClientHandshakeSession, CancellationToken, Task<HandshakeResult>> _HandshakeAsync = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public WatsonTcpClientCallbacks()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Internal-Methods

        internal string HandleAuthenticationRequested()
        {
            string ret = null;

            if (AuthenticationRequested != null)
            {
                try
                {
                    ret = AuthenticationRequested();
                }
                catch (Exception)
                {

                }
            }

            return ret;
        }

        internal SyncResponse HandleSyncRequestReceived(SyncRequest req)
        {
            SyncResponse ret = null;

#pragma warning disable CS0618 // Type or member is obsolete
            if (SyncRequestReceived != null)
            {
                try
                {
                    ret = SyncRequestReceived(req);
                }
                catch (Exception)
                {

                }
            }
#pragma warning restore CS0618 // Type or member is obsolete

            return ret;
        }

        internal async Task<SyncResponse> HandleSyncRequestReceivedAsync(SyncRequest req)
        {
            SyncResponse ret = null;

            if (SyncRequestReceivedAsync != null)
            {
                try
                {
                    ret = await SyncRequestReceivedAsync(req);
                }
                catch (Exception)
                {

                }
            }

            return ret;
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
