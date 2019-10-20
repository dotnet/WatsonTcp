using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WatsonTcp;

namespace TestThroughput
{
    internal class Test1
    {
        private Random _Random = new Random();
        private int _MessageSize = 64;
        private int _NumMessages = 65536; 

        private string _MsgString = null;
        private byte[] _MsgBytes = null;

        private Stopwatch _Stopwatch = new Stopwatch(); 

        private int _MessageSuccess = 0;
        private int _MessageFailed = 0;
        private long _BytesSent = 0;
        private long _BytesReceived = 0;

        private string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[_Random.Next(s.Length)]).ToArray());
        }

        internal Test1(int messageSize, int numMessages)
        {
            _MessageSize = messageSize;
            _NumMessages = numMessages;
            _MsgString = RandomString(_MessageSize);
            _MsgBytes = Encoding.UTF8.GetBytes(_MsgString); 
        }

        internal void RunTest()
        {   
            using (WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", 10000))
            {
                server.MessageReceived = Test1ServerMsgRcv;
                server.Start();

                using (WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 10000))
                {
                    client.MessageReceived = Test1ClientMsgRcv;
                    client.Start();

                    _Stopwatch.Start();

                    for (int i = 0; i < _NumMessages; i++)
                    {
                        if (client.Send(_MsgBytes))
                        {
                            _MessageSuccess++;
                            _BytesSent += _MessageSize;
                        }
                        else
                        {
                            _MessageFailed++;
                        }
                    }

                    _Stopwatch.Stop();
                }
            }

            Console.WriteLine("");
            Console.WriteLine("Results:");
            Console.WriteLine("  Messages sent successfully  : " + _MessageSuccess);
            Console.WriteLine("  Messages failed             : " + _MessageFailed);
            Console.WriteLine("  Bytes sent successfully     : " + _BytesSent);
            Console.WriteLine("  Bytes received successfully : " + _BytesReceived);

            decimal secondsTotal = _Stopwatch.ElapsedMilliseconds / 1000;
            decimal bytesPerSecond = _BytesSent / secondsTotal;
            decimal kbPerSecond = bytesPerSecond / 1024;
            decimal mbPerSecond = kbPerSecond / 1024;
            Console.WriteLine("  Elapsed time (ms)           : " + _Stopwatch.ElapsedMilliseconds + "ms");
            Console.WriteLine("  Elapsed time (seconds)      : " + decimal.Round(secondsTotal, 2) + "s");
            Console.WriteLine("  Bytes per second            : " + decimal.Round(bytesPerSecond, 2) + "B/s");
            Console.WriteLine("  Kilobytes per second        : " + decimal.Round(kbPerSecond, 2) + "kB/s");
            Console.WriteLine("  Megabytes per second        : " + decimal.Round(mbPerSecond, 2) + "MB/s");
            Console.WriteLine("");
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task Test1ServerMsgRcv(string ipPort, byte[] data)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            _BytesReceived += data.Length;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task Test1ClientMsgRcv(byte[] data)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {

        }
    }
}
