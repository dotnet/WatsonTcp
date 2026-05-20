namespace WatsonTcp
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class BufferedReadStream : Stream
    {
        #region Public-Members

        public override bool CanRead => _Count > 0 || (_InnerStream?.CanRead ?? false);
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        #endregion

        #region Private-Members

        private readonly Stream _InnerStream = null;
        private byte[] _Buffer = null;
        private int _Offset = 0;
        private int _Count = 0;

        #endregion

        #region Constructors-and-Factories

        internal BufferedReadStream(Stream stream, int bufferSize)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (bufferSize < 1) throw new ArgumentOutOfRangeException(nameof(bufferSize));

            _InnerStream = stream;
            _Buffer = new byte[bufferSize];
        }

        #endregion

        #region Public-Methods

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateReadArguments(buffer, offset, count);
            if (count == 0) return 0;

            if (_Count > 0)
            {
                int buffered = Math.Min(count, _Count);
                Buffer.BlockCopy(_Buffer, _Offset, buffer, offset, buffered);
                _Offset += buffered;
                _Count -= buffered;
                if (_Count == 0) _Offset = 0;
                return buffered;
            }

            return _InnerStream.Read(buffer, offset, count);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateReadArguments(buffer, offset, count);
            if (count == 0) return 0;

            if (_Count > 0)
            {
                int buffered = Math.Min(count, _Count);
                Buffer.BlockCopy(_Buffer, _Offset, buffer, offset, buffered);
                _Offset += buffered;
                _Count -= buffered;
                if (_Count == 0) _Offset = 0;
                return buffered;
            }

            return await _InnerStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0) return 0;

            if (_Count > 0)
            {
                int buffered = Math.Min(buffer.Length, _Count);
                _Buffer.AsSpan(_Offset, buffered).CopyTo(buffer.Span);
                _Offset += buffered;
                _Count -= buffered;
                if (_Count == 0) _Offset = 0;
                return buffered;
            }

            return await _InnerStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
#endif

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        #endregion

        #region Internal-Methods

        internal void PushBack(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (offset + count > buffer.Length) throw new ArgumentException("Offset and count exceed the supplied buffer length.");
            if (count == 0) return;

            if (_Offset >= count)
            {
                _Offset -= count;
                Buffer.BlockCopy(buffer, offset, _Buffer, _Offset, count);
                _Count += count;
                return;
            }

            if ((_Count + count) <= _Buffer.Length)
            {
                if (_Count > 0)
                {
                    Buffer.BlockCopy(_Buffer, _Offset, _Buffer, count, _Count);
                }

                Buffer.BlockCopy(buffer, offset, _Buffer, 0, count);
                _Offset = 0;
                _Count += count;
                return;
            }

            int newLength = _Buffer.Length;
            while (newLength < (_Count + count))
            {
                newLength *= 2;
            }

            byte[] resized = new byte[newLength];
            Buffer.BlockCopy(buffer, offset, resized, 0, count);
            if (_Count > 0)
            {
                Buffer.BlockCopy(_Buffer, _Offset, resized, count, _Count);
            }

            _Buffer = resized;
            _Offset = 0;
            _Count += count;
        }

        #endregion

        #region Private-Methods

        private static void ValidateReadArguments(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if ((offset + count) > buffer.Length) throw new ArgumentException("Offset and count exceed the supplied buffer length.");
        }

        #endregion
    }
}
