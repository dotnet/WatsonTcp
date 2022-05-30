using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WatsonTcp
{
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

        private readonly object _Lock = new object();
        private Stream _Stream = null;
        private long _Length = 0;
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
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0) throw new ArgumentException("Offset must be zero or greater.");
            if (offset >= buffer.Length) throw new IndexOutOfRangeException("Offset must be less than the buffer length of " + buffer.Length + ".");
            if (count < 0) throw new ArgumentException("Count must be zero or greater.");
            if (count == 0) return 0;
            if (count + offset > buffer.Length) throw new ArgumentException("Offset and count must sum to a value less than the buffer length of " + buffer.Length + ".");

            lock (_Lock)
            {
                byte[] temp = null;

                if (_BytesRemaining == 0) return 0;

                if (count > _BytesRemaining) temp = new byte[_BytesRemaining];
                else temp = new byte[count]; 

                int bytesRead = _Stream.Read(temp, 0, temp.Length);
                Buffer.BlockCopy(temp, 0, buffer, offset, bytesRead); 
                _Position += bytesRead; 

                return bytesRead;

            }
        }

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
    }
}