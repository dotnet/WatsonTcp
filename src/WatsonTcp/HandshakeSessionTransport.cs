namespace WatsonTcp
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class HandshakeSessionTransport : IDisposable
    {
        private readonly object _QueueLock = new object();
        private readonly Queue<HandshakeMessage> _Queue = new Queue<HandshakeMessage>();
        private readonly SemaphoreSlim _Signal = new SemaphoreSlim(0, Int32.MaxValue);
        private readonly Func<HandshakeMessage, CancellationToken, Task> _SendAsync;
        private readonly Func<string, MessageStatus, CancellationToken, Task> _RejectAsync;
        private readonly CancellationToken _SessionToken;

        internal HandshakeSessionTransport(
            Func<HandshakeMessage, CancellationToken, Task> sendAsync,
            Func<string, MessageStatus, CancellationToken, Task> rejectAsync,
            CancellationToken sessionToken)
        {
            _SendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
            _RejectAsync = rejectAsync ?? throw new ArgumentNullException(nameof(rejectAsync));
            _SessionToken = sessionToken;
        }

        public void Dispose()
        {
            _Signal.Dispose();
        }

        internal async Task SendAsync(HandshakeMessage msg, CancellationToken token = default)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));
            if (token == default(CancellationToken))
            {
                await _SendAsync(msg, _SessionToken).ConfigureAwait(false);
            }
            else
            {
                using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _SessionToken))
                {
                    await _SendAsync(msg, linkedCts.Token).ConfigureAwait(false);
                }
            }
        }

        internal async Task<HandshakeMessage> ReceiveAsync(CancellationToken token = default)
        {
            if (token == default(CancellationToken))
            {
                await _Signal.WaitAsync(_SessionToken).ConfigureAwait(false);
            }
            else
            {
                using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _SessionToken))
                {
                    await _Signal.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                }
            }

            lock (_QueueLock)
            {
                if (_Queue.Count < 1) return null;
                return _Queue.Dequeue();
            }
        }

        internal async Task RejectAsync(string reason, MessageStatus status = MessageStatus.HandshakeFailure, CancellationToken token = default)
        {
            if (token == default(CancellationToken))
            {
                await _RejectAsync(reason, status, _SessionToken).ConfigureAwait(false);
            }
            else
            {
                using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _SessionToken))
                {
                    await _RejectAsync(reason, status, linkedCts.Token).ConfigureAwait(false);
                }
            }
        }

        internal void Enqueue(HandshakeMessage msg)
        {
            if (msg == null) return;

            lock (_QueueLock)
            {
                _Queue.Enqueue(msg);
            }

            _Signal.Release();
        }
    }
}
