namespace TestThroughput
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonTcp;

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

        internal async Task RunTest()
        {
            try
            {
                bool finished = false;

                using (WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", 10000))
                {
                    server.Events.MessageReceived += Test1ServerMsgRcv;
                    server.Start();
                    // server.Settings.Logger = ServerLogger;
                    // server.Debug = true; 

                    Task unawaited = Task.Run(async () =>
                    {
                        while (!finished)
                        {
                            await Task.Delay(1000);
                            Console.WriteLine("Server stats: " + server.Statistics.ReceivedMessages + " messages, " + server.Statistics.ReceivedBytes + " bytes");
                        }
                    });

                    using (WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 10000))
                    {
                        client.Events.MessageReceived += Test1ClientMsgRcv;
                        client.Connect();

                        _Stopwatch.Start();

                        for (int i = 0; i < _NumMessages; i++)
                        {
                            await Task.Delay(1);

                            bool success = await client.SendAsync(_MsgBytes);
                            if (success)
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
                         
                        while (_MessagesProcessing > 0)
                        {
                            Console.WriteLine("Waiting on " + _MessagesProcessing + " to complete processing (1 second pause)");
                            await Task.Delay(1000);
                        }

                        Console.WriteLine("Processing complete");
                        await Task.Delay(2500);

                        Console.WriteLine("");
                        Console.WriteLine("Results:");
                        Console.WriteLine("  Messages sent successfully     : " + _MessagesSentSuccess);
                        Console.WriteLine("  Messages failed                : " + _MessagesSentFailed);
                        Console.WriteLine("  Bytes sent successfully        : " + _BytesSent);
                        Console.WriteLine("  Bytes received successfully    : " + _BytesReceived);

                        long secondsTotal = _Stopwatch.ElapsedMilliseconds / 1000;
                        if (secondsTotal < 1) secondsTotal = 1;

                        decimal bytesPerSecond = _BytesSent / secondsTotal;
                        decimal kbPerSecond = bytesPerSecond / 1024;
                        decimal mbPerSecond = kbPerSecond / 1024;
                        Console.WriteLine("  Elapsed time (ms)              : " + _Stopwatch.ElapsedMilliseconds + "ms");
                        Console.WriteLine("  Elapsed time (seconds)         : " + decimal.Round(secondsTotal, 2) + "s");
                        Console.WriteLine("");
                        Console.WriteLine("  Messages per second            : " + decimal.Round((decimal)_MessagesSentSuccess / secondsTotal, 2) + " msg/s");
                        Console.WriteLine("  Bytes per second               : " + decimal.Round(bytesPerSecond, 2) + "B/s");
                        Console.WriteLine("  Kilobytes per second           : " + decimal.Round(kbPerSecond, 2) + "kB/s");
                        Console.WriteLine("  Megabytes per second           : " + decimal.Round(mbPerSecond, 2) + "MB/s");
                        Console.WriteLine("");

                        finished = true;
                    }
                } 
            }
            catch (TaskCanceledException)
            {

            }
            catch (OperationCanceledException)
            {

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
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.ToString());
            }

            Interlocked.Decrement(ref _MessagesProcessing);
        }

        private void Test1ClientMsgRcv(object sender, MessageReceivedEventArgs args)
        {

        } 
    }
}
