using System;
using System.Collections.Generic;
using System.Text;

namespace WatsonTcp
{
    /// <summary>
    /// Internal EventArgs for passing arguments for SyncResponseReceived event.
    /// </summary>
    internal class SyncResponseReceivedEventArgs
    {
        public SyncResponseReceivedEventArgs(WatsonMessage msg, byte[] data)
        {
            message = msg;
            Data = data;
        }
        public WatsonMessage message;
        public byte[] Data;
    }
}
