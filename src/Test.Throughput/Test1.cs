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

        private int _MessagesSentSuccess = 0;
        private int _MessagesSentFailed = 0;
        private int _MessagesProcessing = 0;
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
            try
            {
                using (WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", 10000))
                {
                    server.Events.MessageReceived += Test1ServerMsgRcv;
                    server.Start();
                    // server.Settings.Logger = ServerLogger;
                    // server.Debug = true; 

                    using (WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 10000))
                    {
                        client.Events.MessageReceived += Test1ClientMsgRcv;
                        client.Connect();

                        _Stopwatch.Start();

                        for (int i = 0; i < _NumMessages; i++)
                        {
                            if (client.Send(_MsgBytes))
                            {
                                Interlocked.Increment(ref _MessagesSentSuccess);
                                Interlocked.Increment(ref _MessagesProcessing);
                                Interlocked.Add(ref _BytesSent, _MessageSize);
                            }
                            else
                            {
                                Interlocked.Increment(ref _MessagesSentFailed);
                            }
                        }

                        _Stopwatch.Stop();
                         
                        decimal secondsTotal = _Stopwatch.ElapsedMilliseconds / 1000;
                        if (secondsTotal < 1) secondsTotal = 1;

                        Console.WriteLine("Messages sent after " + secondsTotal + " seconds");
                        while (_MessagesProcessing > 0)
                        {
                            Console.WriteLine("Waiting on " + _MessagesProcessing + " to complete processing (1 second pause)");
                            Thread.Sleep(1000);
                        }

                        Console.WriteLine("");
                        Console.WriteLine("Results:");
                        Console.WriteLine("  Messages sent successfully     : " + _MessagesSentSuccess);
                        Console.WriteLine("  Messages failed                : " + _MessagesSentFailed);
                        Console.WriteLine("  Bytes sent successfully        : " + _BytesSent);
                        Console.WriteLine("  Bytes received successfully    : " + _BytesReceived);

                        decimal bytesPerSecond = _BytesSent / secondsTotal;
                        decimal kbPerSecond = bytesPerSecond / 1024;
                        decimal mbPerSecond = kbPerSecond / 1024;
                        Console.WriteLine("  Elapsed time sending (ms)      : " + _Stopwatch.ElapsedMilliseconds + "ms");
                        Console.WriteLine("  Elapsed time sending (seconds) : " + decimal.Round(secondsTotal, 2) + "s");
                        Console.WriteLine("  Send bytes per second          : " + decimal.Round(bytesPerSecond, 2) + "B/s");
                        Console.WriteLine("  Send kilobytes per second      : " + decimal.Round(kbPerSecond, 2) + "kB/s");
                        Console.WriteLine("  Send megabytes per second      : " + decimal.Round(mbPerSecond, 2) + "MB/s");
                        Console.WriteLine("");
                    }
                } 
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.ToString());
            }
        }
         
        private void Test1ServerMsgRcv(object sender, MessageReceivedEventArgs args)
        {
            try
            {
                // Console.WriteLine("Processing message from client " + args.IpPort + " (" + args.Data.Length + " bytes)");
                Interlocked.Add(ref _BytesReceived, args.Data.Length);
                Interlocked.Decrement(ref _MessagesProcessing);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.ToString());
            }
        }

        private void Test1ClientMsgRcv(object sender, MessageReceivedEventArgs args)
        {

        } 
    }
}
