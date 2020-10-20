using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Watson TCP server events.
    /// </summary>
    public class WatsonTcpServerEvents
    {
        #region Public-Members

        /// <summary>
        /// Event to fire when authentication is requested from a client.
        /// </summary>
        public event EventHandler<AuthenticationRequestedEventArgs> AuthenticationRequested = delegate { };

        /// <summary>
        /// Event to fire when a client successfully authenticates.
        /// </summary>
        public event EventHandler<AuthenticationSucceededEventArgs> AuthenticationSucceeded = delegate { };

        /// <summary>
        /// Event to fire when a client fails authentication.
        /// </summary>
        public event EventHandler<AuthenticationFailedEventArgs> AuthenticationFailed = delegate { };

        /// <summary>
        /// Event to fire when a client connects to the server.
        /// The IP:port of the client is passed in the arguments.
        /// </summary>
        public event EventHandler<ClientConnectedEventArgs> ClientConnected = delegate { };

        /// <summary>
        /// Event to fire when a client disconnects from the server.
        /// The IP:port is passed in the arguments along with the reason for the disconnection.
        /// </summary>
        public event EventHandler<ClientDisconnectedEventArgs> ClientDisconnected = delegate { };

        /// <summary>
        /// This event is fired when a message is received from a client and it is desired that WatsonTcp pass the byte array containing the message payload.
        /// If MessageReceived is set, StreamReceived will not be used.
        /// </summary>
        public event EventHandler<MessageReceivedFromClientEventArgs> MessageReceived = delegate { }; 

        /// <summary> 
        /// This event is fired when a stream is received from a client and it is desired that WatsonTcp pass the stream containing the message payload to your application. 
        /// If MessageReceived is set, StreamReceived will not be used.
        /// </summary>
        public event EventHandler<StreamReceivedFromClientEventArgs> StreamReceived = delegate { };

        /// <summary>
        /// This event is fired when the server is started.
        /// </summary>
        public event EventHandler ServerStarted = delegate { };

        /// <summary>
        /// This event is fired when the server is stopped.
        /// </summary>
        public event EventHandler ServerStopped = delegate { };

        /// <summary>
        /// This event is fired when an exception is encountered.
        /// </summary>
        public event EventHandler<ExceptionEventArgs> ExceptionEncountered = delegate { };

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
        /// Instantiate the object.
        /// </summary>
        public WatsonTcpServerEvents()
        {

        }

        #endregion

        #region Public-Methods

        #endregion

        #region Internal-Methods

        internal void HandleAuthenticationRequested(object sender, AuthenticationRequestedEventArgs args)
        {
            AuthenticationRequested?.Invoke(sender, args);
        }

        internal void HandleAuthenticationSucceeded(object sender, AuthenticationSucceededEventArgs args)
        {
            AuthenticationSucceeded?.Invoke(sender, args);
        }

        internal void HandleAuthenticationFailed(object sender, AuthenticationFailedEventArgs args)
        {
            AuthenticationFailed?.Invoke(sender, args);
        }

        internal void HandleClientConnected(object sender, ClientConnectedEventArgs args)
        {
            ClientConnected?.Invoke(sender, args);
        }

        internal void HandleClientDisconnected(object sender, ClientDisconnectedEventArgs args)
        {
            ClientDisconnected?.Invoke(sender, args);
        }

        internal void HandleMessageReceived(object sender, MessageReceivedFromClientEventArgs args)
        {
            MessageReceived?.Invoke(sender, args);
        }

        internal void HandleStreamReceived(object sender, StreamReceivedFromClientEventArgs args)
        {
            StreamReceived?.Invoke(sender, args);
        }

        internal void HandleServerStarted(object sender, EventArgs args)
        {
            ServerStarted?.Invoke(sender, args);
        }

        internal void HandleServerStopped(object sender, EventArgs args)
        {
            ServerStopped?.Invoke(sender, args);
        }

        internal void HandleExceptionEncountered(object sender, ExceptionEventArgs args)
        {
            ExceptionEncountered?.Invoke(sender, args);
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
