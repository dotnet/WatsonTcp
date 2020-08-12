using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WatsonTcp
{
    internal class KeepAliveValues
    {
        UInt32 onoff;
        UInt32 keepalivetime;
        UInt32 keepaliveinterval;
        public KeepAliveValues()
        {
            onoff = 1;
            keepalivetime = 10000;
            keepaliveinterval = 1000;
        }
        public byte[] Values
        {
            get
            {
                MemoryStream bytes = new MemoryStream();
                BinaryWriter writer = new BinaryWriter(bytes);
                writer.Write(onoff);
                writer.Write(keepalivetime);
                writer.Write(keepaliveinterval);
                return bytes.GetBuffer().Take(12).ToArray();
            }
        }
    }
}
