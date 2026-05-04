namespace WatsonTcp
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Server-side handshake session.
    /// </summary>
    public class ServerHandshakeSession
    {
        private readonly HandshakeSessionTransport _Transport;

        /// <summary>
        /// Provisional client metadata.
        /// </summary>
        public ClientMetadata Client { get; }

        internal ServerHandshakeSession(ClientMetadata client, HandshakeSessionTransport transport)
        {
            Client = client;
            _Transport = transport;
        }

        /// <summary>
        /// Send a handshake message to the remote client.
        /// </summary>
        public async Task SendAsync(HandshakeMessage msg, CancellationToken token = default)
        {
            await _Transport.SendAsync(msg, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Receive a handshake message from the remote client.
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
