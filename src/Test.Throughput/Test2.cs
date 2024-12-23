namespace TestThroughput
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonTcp;

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

        internal Test2(int messageSize, int numMessages, int numClients)
        {
            _MessageSize = messageSize;
            _NumMessages = numMessages;
            _NumClients = numClients;
            _MsgString = RandomString(_MessageSize);
            _MsgBytes = Encoding.UTF8.GetBytes(_MsgString); 
        }

        internal async Task RunTest()
        {
            bool finished = false;

            using (WatsonTcpServer server = new WatsonTcpServer("127.0.0.1", 10000))
            {
                server.Events.MessageReceived += Test2ServerMsgRcv;
                server.Start();

                _Stopwatch.Start();

                Task unawaited = Task.Run(async () =>
                {
                    while (!finished)
                    { 
                        await Task.Delay(1000);
                        Console.WriteLine("Server stats: " + server.Statistics.ReceivedMessages + " messages, " + server.Statistics.ReceivedBytes + " bytes");
                    }
                });

                for (int i = 0; i < _NumClients; i++)
                {
                    int clientNum = i;
                    Console.WriteLine("Starting client " + clientNum);

                    Task clientTask = Task.Run(() => Test2ClientWorker(clientNum));
                    _RunningTasks++;
                } 

                while (_RunningTasks > 0)
                {
                    Console.WriteLine("Waiting on " + _RunningTasks + " running tasks (1 second pause)");
                    await Task.Delay(1000);
                }

                Console.WriteLine("All tasks complete");

                _Stopwatch.Stop();

                while (_MessagesProcessing > 0)
                {
                    Console.WriteLine("Waiting on " + _MessagesProcessing + " to complete processing (1 second pause)");
                    await Task.Delay(1000);
                }

                Console.WriteLine("Processing complete");
                await Task.Delay(2500);
            }

            Console.WriteLine("");
            Console.WriteLine("Results:");
            Console.WriteLine("  Number of clients             : " + _NumClients);
            Console.WriteLine("  Number of messages per client : " + _NumMessages);
            Console.WriteLine("");
            Console.WriteLine("  Expected message count        : " + (_NumClients * _NumMessages));
            Console.WriteLine("  Messages sent successfully    : " + _MessagesSentSuccess);
            Console.WriteLine("  Messages failed               : " + _MessagesSentFailed);
            Console.WriteLine("");
            Console.WriteLine("  Expected bytes                : " + (_NumClients * _NumMessages * _MsgBytes.Length));
            Console.WriteLine("  Bytes sent successfully       : " + _BytesSent);
            Console.WriteLine("  Bytes received successfully   : " + _BytesReceived);
            Console.WriteLine("");

            long secondsTotal = _Stopwatch.ElapsedMilliseconds / 1000;
            if (secondsTotal < 1) secondsTotal = 1;

            decimal bytesPerSecond = _BytesSent / secondsTotal;
            decimal kbPerSecond = bytesPerSecond / 1024;
            decimal mbPerSecond = kbPerSecond / 1024;
            Console.WriteLine("  Elapsed time (ms)             : " + _Stopwatch.ElapsedMilliseconds + "ms");
            Console.WriteLine("  Elapsed time (seconds)        : " + decimal.Round(secondsTotal, 2) + "s");
            Console.WriteLine("");
            Console.WriteLine("  Messages per second           : " + decimal.Round((decimal)_MessagesSentSuccess / secondsTotal, 2) + " msg/s");
            Console.WriteLine("  Bytes per second              : " + decimal.Round(bytesPerSecond, 2) + "B/s");
            Console.WriteLine("  Kilobytes per second          : " + decimal.Round(kbPerSecond, 2) + "kB/s");
            Console.WriteLine("  Megabytes per second          : " + decimal.Round(mbPerSecond, 2) + "MB/s");
            Console.WriteLine("");
            
            finished = true;
        }

        private async Task Test2ClientWorker(int clientNum)
        { 
            try
            {
                long msgsSent = 0;
                long bytesSent = 0;

                using (WatsonTcpClient client = new WatsonTcpClient("127.0.0.1", 10000))
                {
                    client.Events.MessageReceived += Test2ClientMsgRcv;
                    client.Connect();

                    for (int i = 0; i < _NumMessages; i++)
                    {
                        await Task.Delay(1);

                        bool success = await client.SendAsync(_MsgBytes);
                        if (success)
                        {
                            msgsSent++;
                            bytesSent += _MsgBytes.Length;
                            Interlocked.Increment(ref _MessagesSentSuccess);
                            Interlocked.Increment(ref _MessagesProcessing);
                            Interlocked.Add(ref _BytesSent, _MsgBytes.Length);
                        }
                        else
                        {
                            Interlocked.Increment(ref _MessagesSentFailed);
                        }
                    }
                }

                Console.WriteLine("Client " + clientNum + " finished, sent " + msgsSent + " messages, " + bytesSent + " bytes");
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

            Interlocked.Decrement(ref _RunningTasks);
        }
         
        private void Test2ServerMsgRcv(object sender, MessageReceivedEventArgs args)
        {
            // Console.WriteLine("Processing message from client " + args.IpPort + " (" + args.Data.Length + " bytes)");
            Interlocked.Decrement(ref _MessagesProcessing);
            Interlocked.Add(ref _BytesReceived, args.Data.Length);
        }

        private void Test2ClientMsgRcv(object sender, MessageReceivedEventArgs args)
        {

        }
    }
}
