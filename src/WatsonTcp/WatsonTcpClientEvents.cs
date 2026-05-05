namespace WatsonTcp
{
    using System;

    /// <summary>
    /// Watson TCP client events.
    /// </summary>
    public class WatsonTcpClientEvents
    {
        #region Public-Members

        /// <summary>
        /// Event fired when authentication has succeeded.
        /// </summary>
        public event EventHandler AuthenticationSucceeded;

        /// <summary>
        /// Event fired when authentication has failed.
        /// </summary>
        public event EventHandler AuthenticationFailure;

        /// <summary>
        /// Event fired when a connection is rejected during initialization.
        /// </summary>
        public event EventHandler<ConnectionRejectedEventArgs> ConnectionRejected;

        /// <summary>
        /// Event fired when a handshake succeeds.
        /// </summary>
        public event EventHandler<HandshakeSucceededEventArgs> HandshakeSucceeded;

        /// <summary>
        /// Event fired when a handshake fails.
        /// </summary>
        public event EventHandler<HandshakeFailedEventArgs> HandshakeFailed;

        /// <summary>  
        /// This event is fired when a message is received from the server and it is desired that WatsonTcp pass the byte array containing the message payload.
        /// Events.MessageReceived takes precedence over Callbacks.StreamReceivedAsync and Events.StreamReceived.
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        /// <summary>
        /// This event is fired when a stream is received from the server and it is desired that WatsonTcp pass the stream containing the message payload to your application.
        /// This is the legacy synchronous stream API. Callbacks.StreamReceivedAsync takes precedence over Events.StreamReceived, and
        /// large proxied streams should be fully consumed before the handler returns.
        /// </summary>
        public event EventHandler<StreamReceivedEventArgs> StreamReceived;

        /// <summary>
        /// Event fired when the client successfully connects to the server.
        /// The IP:port of the server is passed in the arguments.
        /// </summary>
        public event EventHandler<ConnectionEventArgs> ServerConnected;

        /// <summary>
        /// Event fired when the client disconnects from the server.
        /// The IP:port of the server is passed in the arguments.
        /// </summary>
        public event EventHandler<DisconnectionEventArgs> ServerDisconnected;

        /// <summary>
        /// This event is fired when an exception is encountered.
        /// </summary>
        public event EventHandler<ExceptionEventArgs> ExceptionEncountered;

        #endregion

        #region Internal-Members

        internal bool IsUsingMessages
        {
            get
            {
                if (MessageReceived != null && MessageReceived.GetInvocationList().Length > 0) return true;
                return false;
            }
        }

        internal bool IsUsingStreams
        {
            get
            {
                if (StreamReceived != null && StreamReceived.GetInvocationList().Length > 0) return true;
                return false;
            }
        }

        #endregion

        #region Private-Members

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public WatsonTcpClientEvents()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Internal-Methods

        internal void HandleAuthenticationSucceeded(object sender, EventArgs args)
        {
            WrappedEventHandler(() => AuthenticationSucceeded?.Invoke(sender, args), "ServerConnected", sender); 
        }

        internal void HandleAuthenticationFailure(object sender, EventArgs args)
        {
            WrappedEventHandler(() => AuthenticationFailure?.Invoke(sender, args), "AuthenticationFailure", sender);
        }

        internal void HandleConnectionRejected(object sender, ConnectionRejectedEventArgs args)
        {
            WrappedEventHandler(() => ConnectionRejected?.Invoke(sender, args), "ConnectionRejected", sender);
        }

        internal void HandleHandshakeSucceeded(object sender, HandshakeSucceededEventArgs args)
        {
            WrappedEventHandler(() => HandshakeSucceeded?.Invoke(sender, args), "HandshakeSucceeded", sender);
        }

        internal void HandleHandshakeFailed(object sender, HandshakeFailedEventArgs args)
        {
            WrappedEventHandler(() => HandshakeFailed?.Invoke(sender, args), "HandshakeFailed", sender);
        }

        internal void HandleMessageReceived(object sender, MessageReceivedEventArgs args)
        {
            WrappedEventHandler(() => MessageReceived?.Invoke(sender, args), "MessageReceived", sender);
        }

        internal void HandleStreamReceived(object sender, StreamReceivedEventArgs args)
        {
            WrappedEventHandler(() => StreamReceived?.Invoke(sender, args), "StreamReceived", sender);
        }

        internal void HandleServerConnected(object sender, ConnectionEventArgs args)
        {
            WrappedEventHandler(() => ServerConnected?.Invoke(sender, args), "ServerConnected", sender);
        }

        internal void HandleServerDisconnected(object sender, DisconnectionEventArgs args)
        {
            WrappedEventHandler(() => ServerDisconnected?.Invoke(sender, args), "ServerDisconnected", sender);
        }

        internal void HandleExceptionEncountered(object sender, ExceptionEventArgs args)
        {
            WrappedEventHandler(() => ExceptionEncountered?.Invoke(sender, args), "ExceptionEncountered", sender);
        }

        #endregion

        #region Private-Methods

        internal static void WrappedEventHandler(Action action, string handler, object sender)
        {
            if (action == null) return;

            try
            {
                action.Invoke();
            }
            catch (Exception e)
            {
                Action<Severity, string> logger = ((WatsonTcpClient)sender).Settings?.Logger;
                logger?.Invoke(Severity.Error, "Event handler exception in " + handler + ": " + Environment.NewLine + e.ToString());
            }
        }

        #endregion
    }
}
