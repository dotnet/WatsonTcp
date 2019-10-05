using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WatsonTcp;

namespace TestThroughput
{
    internal class Test2
    {
        private Random _Random = new Random();
        private int _MessageSize = 64;
        private int _NumMessages = 65536;
        private int _NumClients = 4;

        private string _MsgString = null;
        private byte[] _MsgBytes = null;

        private Stopwatch _Stopwatch = new Stopwatch();
        private int _RunningTasks = 0;

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

        internal Test2(int messageSize, int numMessages, int numClients)
        {
            _MessageSize = messageSize;
            _NumMessages = numMessages;
            _NumClients = numClients;
            _MsgString = RandomString(_MessageSize);
            _MsgBytes = Encoding.UTF8.GetBytes(_MsgString); 
        }

        internal void RunTest()
        {  
            using (WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", 10000))
            {
                server.MessageReceived = Test2ServerMsgRcv;
                server.Start();
                 
                Stopwatch sw = new Stopwatch();

                for (int i = 0; i < _NumClients; i++)
                {
                    Task.Run(() => Test2ClientWorker());
                    _RunningTasks++;
                } 

                while (_RunningTasks > 0)
                {
                    Task.Delay(100).Wait();
                }

                _Stopwatch.Stop();
            }

            Console.WriteLine("");
            Console.WriteLine("Results:");
            Console.WriteLine("  Number of clients             : " + _NumClients);
            Console.WriteLine("  Number of messages per client : " + _NumMessages);
            Console.WriteLine("  Messages sent successfully    : " + _MessageSuccess);
            Console.WriteLine("  Messages failed               : " + _MessageFailed);
            Console.WriteLine("  Bytes sent successfully       : " + _BytesSent);
            Console.WriteLine("  Bytes received successfully   : " + _BytesReceived);

            decimal secondsTotal = _Stopwatch.ElapsedMilliseconds / 1000;
            decimal bytesPerSecond = _BytesSent / secondsTotal;
            decimal kbPerSecond = bytesPerSecond / 1024;
            decimal mbPerSecond = kbPerSecond / 1024;
            Console.WriteLine("  Elapsed time (ms)             : " + _Stopwatch.ElapsedMilliseconds + "ms");
            Console.WriteLine("  Elapsed time (seconds)        : " + decimal.Round(secondsTotal, 2) + "s");
            Console.WriteLine("  Messages per second           : " + decimal.Round(_MessageSuccess / secondsTotal, 2) + "msg/s");
            Console.WriteLine("  Bytes per second              : " + decimal.Round(bytesPerSecond, 2) + "B/s");
            Console.WriteLine("  Kilobytes per second          : " + decimal.Round(kbPerSecond, 2) + "kB/s");
            Console.WriteLine("  Megabytes per second          : " + decimal.Round(mbPerSecond, 2) + "MB/s");
            Console.WriteLine("");
        }

        private void Test2ClientWorker()
        {
            using (WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 10000))
            {
                client.MessageReceived = Test2ClientMsgRcv;
                client.Start();
                 
                for (int i = 0; i < _NumMessages; i++)
                {
                    if (client.Send(_MsgBytes))
                    {
                        _MessageSuccess++;
                        _BytesSent += _MsgBytes.Length;
                    }
                    else
                    {
                        _MessageFailed++;
                    }
                } 
            }

            _RunningTasks--;
        }

        private async Task Test2ServerMsgRcv(string ipPort, byte[] data)
        {
            if (!_Stopwatch.IsRunning) _Stopwatch.Start(); 
            _BytesReceived += data.Length;
        }

        private async Task Test2ClientMsgRcv(byte[] data)
        {

        }
    }
}
