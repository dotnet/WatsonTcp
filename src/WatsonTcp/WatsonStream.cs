namespace WatsonTcp
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Stream containing message data.
    /// </summary>
    public class WatsonStream : Stream
    {
        #region Public-Members

        /// <summary>
        /// Indicates if the stream is readable.
        /// This value will always be true.
        /// </summary>
        public override bool CanRead => true;

        /// <summary>
        /// Indicates if seek operations are supported.
        /// This value will always be false.
        /// </summary>
        public override bool CanSeek => false;

        /// <summary>
        /// Indicates if the stream is writeable.
        /// This value will always be false.
        /// </summary>
        public override bool CanWrite => false;

        /// <summary>
        /// The number of bytes remaining in the stream.
        /// </summary>
        public override long Length
        {
            get
            {
                return _Length;
            }
        }

        /// <summary>
        /// The current position within the stream.
        /// </summary>
        public override long Position
        {
            get
            {
                return _Position;
            }
            set
            {
                throw new InvalidOperationException("Position may not be modified.");
            }
        }

        #endregion

        #region Private-Members

        private readonly SemaphoreSlim _ReadLock = new SemaphoreSlim(1, 1);
        private readonly Stream _Stream = null;
        private readonly long _Length = 0;
        private long _Position = 0;
        private long _BytesRemaining
        {
            get
            {
                return _Length - _Position;
            }
        }

        #endregion

        #region Constructors-and-Factories

        internal WatsonStream(long contentLength, Stream stream)
        {
            if (contentLength < 0) throw new ArgumentException("Content length must be zero or greater.");
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Cannot read from supplied stream.");

            _Length = contentLength;
            _Stream = stream;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Flushes data awaiting in the stream.
        /// </summary>
        public override void Flush()
        {
        }

        /// <summary>
        /// Read data from the stream.
        /// </summary>
        /// <param name="buffer">The buffer to which the data should be read.</param>
        /// <param name="offset">The offset within the buffer where data should begin.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>Number of bytes read.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateReadArguments(buffer, offset, count);
            if (count == 0) return 0;

            _ReadLock.Wait();

            try
            {
                if (_BytesRemaining == 0) return 0;

                int toRead = GetReadLength(count);

                // Stream.Read() may return fewer bytes than requested - this is expected behavior
                // for Stream implementations. Callers must loop to read all desired bytes.
#pragma warning disable CA2022 // Avoid inexact read
                int bytesRead = _Stream.Read(buffer, offset, toRead);
#pragma warning restore CA2022
                _Position += bytesRead;

                return bytesRead;
            }
            finally
            {
                _ReadLock.Release();
            }
        }

        /// <summary>
        /// Read data asynchronously from the stream.
        /// </summary>
        /// <param name="buffer">The buffer to which the data should be read.</param>
        /// <param name="offset">The offset within the buffer where data should begin.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Number of bytes read.</returns>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateReadArguments(buffer, offset, count);
            if (count == 0) return 0;

            await _ReadLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (_BytesRemaining == 0) return 0;

                int toRead = GetReadLength(count);
                int bytesRead = await _Stream.ReadAsync(buffer, offset, toRead, cancellationToken).ConfigureAwait(false);
                _Position += bytesRead;
                return bytesRead;
            }
            finally
            {
                _ReadLock.Release();
            }
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        /// <summary>
        /// Read data asynchronously from the stream.
        /// </summary>
        /// <param name="buffer">The buffer to which the data should be read.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Number of bytes read.</returns>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0) return 0;

            await _ReadLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (_BytesRemaining == 0) return 0;

                int toRead = GetReadLength(buffer.Length);
                int bytesRead = await _Stream.ReadAsync(buffer.Slice(0, toRead), cancellationToken).ConfigureAwait(false);
                _Position += bytesRead;
                return bytesRead;
            }
            finally
            {
                _ReadLock.Release();
            }
        }
#endif

        /// <summary>
        /// Not supported.
        /// Seek to a specific position within a stream.
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="origin"></param>
        /// <returns></returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new InvalidOperationException("Seek operations are not supported.");
        }

        /// <summary>
        /// Not supported.
        /// Set the length of the stream.
        /// </summary>
        /// <param name="value">Length.</param>
        public override void SetLength(long value)
        {
            throw new InvalidOperationException("Length may not be modified.");
        }

        /// <summary>
        /// Not supported.
        /// Write to the stream.
        /// </summary>
        /// <param name="buffer">The buffer containing the data that should be written to the stream.</param>
        /// <param name="offset">The offset within the buffer from which data should be read.</param>
        /// <param name="count">The number of bytes to read.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException("Stream is not writeable.");
        }

        #endregion

        #region Internal-Methods

        internal long RemainingBytes
        {
            get
            {
                return _BytesRemaining;
            }
        }

        internal async Task DrainAsync(int bufferSize, CancellationToken token)
        {
            if (bufferSize < 1) throw new ArgumentException("Buffer size must be greater than zero.", nameof(bufferSize));
            if (_BytesRemaining <= 0) return;

            byte[] buffer = new byte[bufferSize];

            while (_BytesRemaining > 0)
            {
                int bytesRead = await ReadAsync(buffer, 0, GetReadLength(buffer.Length), token).ConfigureAwait(false);
                if (bytesRead <= 0) throw new IOException("Could not drain the remaining bytes from the stream.");
            }
        }

        #endregion

        #region Private-Methods

        private int GetReadLength(int requestedCount)
        {
            return (int)Math.Min((long)requestedCount, _BytesRemaining);
        }

        private static void ValidateReadArguments(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0) throw new ArgumentException("Offset must be zero or greater.");
            if (offset >= buffer.Length) throw new IndexOutOfRangeException("Offset must be less than the buffer length of " + buffer.Length + ".");
            if (count < 0) throw new ArgumentException("Count must be zero or greater.");
            if (count + offset > buffer.Length) throw new ArgumentException("Offset and count must sum to a value less than the buffer length of " + buffer.Length + ".");
        }

        #endregion
    }
}
