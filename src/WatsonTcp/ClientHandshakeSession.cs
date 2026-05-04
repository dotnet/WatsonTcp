namespace WatsonTcp
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Client-side handshake session.
    /// </summary>
    public class ClientHandshakeSession
    {
        private readonly HandshakeSessionTransport _Transport;

        internal ClientHandshakeSession(HandshakeSessionTransport transport)
        {
            _Transport = transport;
        }

        /// <summary>
        /// Send a handshake message to the remote server.
        /// </summary>
        public async Task SendAsync(HandshakeMessage msg, CancellationToken token = default)
        {
            await _Transport.SendAsync(msg, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Receive a handshake message from the remote server.
        /// </summary>
        public async Task<HandshakeMessage> ReceiveAsync(CancellationToken token = default)
        {
            return await _Transport.ReceiveAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Reject the handshake.
        /// </summary>
        public async Task RejectAsync(string reason, MessageStatus status = MessageStatus.HandshakeFailure, CancellationToken token = default)
        {
            await _Transport.RejectAsync(reason, status, token).ConfigureAwait(false);
        }
    }
}
