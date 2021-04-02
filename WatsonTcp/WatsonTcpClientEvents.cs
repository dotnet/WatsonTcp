using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
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
        /// This event is fired when a message is received from the server and it is desired that WatsonTcp pass the byte array containing the message payload. 
        /// If MessageReceived is set, StreamReceived will not be used.
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        /// <summary> 
        /// This callback is called when a stream is received from the server and it is desired that WatsonTcp pass the stream containing the message payload to your application. 
        /// If MessageReceived is set, StreamReceived will not be used.
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
        /// Instantiate the object.
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

        internal void WrappedEventHandler(Action action, string handler, object sender)
        {
            if (action == null) return;

            try
            {
                action.Invoke();
            }
            catch (Exception e)
            {
                Action<Severity, string> logger = ((WatsonTcpClient)sender).Settings?.Logger;
                logger?.Invoke(Severity.Error, "Event handler exception in " + handler + ": " + Environment.NewLine + SerializationHelper.SerializeJson(e, true));
            }
        }

        #endregion
    }
}
