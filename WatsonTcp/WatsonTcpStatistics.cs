using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace WatsonTcp
{
    /// <summary>
    /// Watson TCP statistics.
    /// </summary>
    public class WatsonTcpStatistics
    {
        #region Public-Members

        /// <summary>
        /// The time at which the client or server was started.
        /// </summary>
        public DateTime StartTime
        {
            get
            {
                return _StartTime;
            }
        }

        /// <summary>
        /// The amount of time which the client or server has been up.
        /// </summary>
        public TimeSpan UpTime
        {
            get
            {
                return DateTime.Now.ToUniversalTime() - _StartTime;
            }
        }

        /// <summary>
        /// The number of bytes received.
        /// </summary>
        public long ReceivedBytes
        {
            get
            {
                return _ReceivedBytes;
            }
            internal set
            {
                _ReceivedBytes = value;
            }
        }

        /// <summary>
        /// The number of messages received.
        /// </summary>
        public long ReceivedMessages
        {
            get
            {
                return _ReceivedMessages;
            }
            internal set
            {
                _ReceivedMessages = value;
            }
        }

        /// <summary>
        /// Average received message size in bytes.
        /// </summary>
        public int ReceivedMessageSizeAverage
        {
            get
            {
                if (_ReceivedBytes > 0 && _ReceivedMessages > 0)
                {
                    return (int)(_ReceivedBytes / _ReceivedMessages);
                }
                else
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// The number of bytes sent.
        /// </summary>
        public long SentBytes
        {
            get
            {
                return _SentBytes;
            }
            internal set
            {
                _SentBytes = value;
            }
        }

        /// <summary>
        /// The number of messages sent.
        /// </summary>
        public long SentMessages
        {
            get
            {
                return _SentMessages;
            }
            internal set
            {
                _SentMessages = value;
            }
        }

        /// <summary>
        /// Average sent message size in bytes.
        /// </summary>
        public decimal SentMessageSizeAverage
        {
            get
            { 
                if (_SentBytes > 0 && _SentMessages > 0)
                {
                    return (int)(_SentBytes / _SentMessages);
                }
                else
                {
                    return 0;
                }
            }
        }

        #endregion

        #region Private-Members

        private DateTime _StartTime = DateTime.Now.ToUniversalTime();
        private long _ReceivedBytes = 0;
        private long _ReceivedMessages = 0;
        private long _SentBytes = 0;
        private long _SentMessages = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the object.
        /// </summary>
        public WatsonTcpStatistics()
        {

        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Return human-readable version of the object.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            string ret =
                "--- Statistics ---" + Environment.NewLine +
                "    Started     : " + _StartTime.ToString() + Environment.NewLine +
                "    Uptime      : " + UpTime.ToString() + Environment.NewLine +
                "    Received    : " + Environment.NewLine +
                "       Bytes    : " + ReceivedBytes + Environment.NewLine +
                "       Messages : " + ReceivedMessages + Environment.NewLine +
                "       Average  : " + ReceivedMessageSizeAverage + " bytes" + Environment.NewLine +
                "    Sent        : " + Environment.NewLine +
                "       Bytes    : " + SentBytes + Environment.NewLine +
                "       Messages : " + SentMessages + Environment.NewLine +
                "       Average  : " + SentMessageSizeAverage + " bytes" + Environment.NewLine;
            return ret;
        }

        /// <summary>
        /// Reset statistics other than StartTime and UpTime.
        /// </summary>
        public void Reset()
        {
            _ReceivedBytes = 0;
            _ReceivedMessages = 0;
            _SentBytes = 0;
            _SentMessages = 0;
        }

        internal void IncrementReceivedMessages()
        {
            _ReceivedMessages = Interlocked.Increment(ref _ReceivedMessages);
        }

        internal void IncrementSentMessages()
        {
            _SentMessages = Interlocked.Increment(ref _SentMessages);
        }

        internal void AddReceivedBytes(long bytes)
        {
            _ReceivedBytes = Interlocked.Add(ref _ReceivedBytes, bytes);
        }

        internal void AddSentBytes(long bytes)
        {
            _SentBytes = Interlocked.Add(ref _SentBytes, bytes);
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
