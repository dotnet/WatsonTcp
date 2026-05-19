namespace TestPerformanceBenchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonTcp;

    internal static class Program
    {
        private const string Host = "127.0.0.1";
        private const string CertificatePassword = "password";
        private const int DrainBufferSize = 65536;
        private const int MinBenchmarkPort = 20000;
        private const int MaxBenchmarkPort = 60000;
        private const int MaxPortSelectionAttempts = 32;

        private static readonly object PayloadLock = new object();
        private static readonly Dictionary<int, byte[]> PayloadCache = new Dictionary<int, byte[]>();
        private static readonly BenchmarkTransportMode[] Transports =
        {
            BenchmarkTransportMode.Tcp,
            BenchmarkTransportMode.Ssl
        };
        private static readonly TextWriter OriginalStandardOutput = Console.Out;

        private static readonly PayloadBenchmarkCase[] PayloadCases =
        {
            new PayloadBenchmarkCase("64B", 64, 250, 2000, new[] { 1, 4 }, 30000, 120000),
            new PayloadBenchmarkCase("64KB", 64 * 1024, 40, 96, new[] { 1, 4 }, 90000, 180000),
            new PayloadBenchmarkCase("64MB", 64 * 1024 * 1024, 3, 2, new[] { 1, 2 }, 300000, 600000)
        };

        private static readonly ConnectionSetupCase[] SetupCases =
        {
            new ConnectionSetupCase("Sequential", 1, 20),
            new ConnectionSetupCase("Burst x4", 4, 10)
        };
        private static bool _summaryOnly;

        private static int Main(string[] args)
        {
            try
            {
                BenchmarkCommandLineOptions options = BenchmarkCommandLineOptions.Parse(args);
                ConfigureConsoleOutput(options);
                RunAsync().GetAwaiter().GetResult();
                RestoreConsoleOutput();
                return 0;
            }
            catch (Exception e)
            {
                RestoreConsoleOutput();
                TextWriter writer = _summaryOnly ? Console.Error : Console.Out;
                writer.WriteLine();
                writer.WriteLine("Fatal error:");
                writer.WriteLine(e.ToString());
                return 1;
            }
        }

        private static async Task RunAsync()
        {
            PrintHeader();
            PrintSuiteSummary();
            PrintDetailedSuiteSummary();
            Console.WriteLine("");

            await RunFullSuiteAsync().ConfigureAwait(false);
        }

        private static async Task RunFullSuiteAsync()
        {
            DateTimeOffset runStarted = DateTimeOffset.Now;
            Stopwatch stopwatch = Stopwatch.StartNew();

            Console.WriteLine("Running full suite.");
            Console.WriteLine("This covers response time, throughput, and connection setup time.");
            Console.WriteLine("Maximum connection count is intentionally not tested.");
            Console.WriteLine("");

            List<LatencyBenchmarkResult> latencyResults = await RunResponseTimeSuiteAsync().ConfigureAwait(false);
            List<ThroughputBenchmarkResult> throughputResults = await RunThroughputSuiteAsync().ConfigureAwait(false);
            List<ConnectionSetupBenchmarkResult> connectionSetupResults = await RunConnectionSetupSuiteAsync().ConfigureAwait(false);

            stopwatch.Stop();
            DateTimeOffset runCompleted = DateTimeOffset.Now;
            Console.WriteLine("");
            Console.WriteLine("Full suite completed in " + FormatDuration(stopwatch.Elapsed.TotalSeconds) + ".");
            Console.WriteLine("");

            PrintFinalSummary(runStarted, runCompleted, stopwatch.Elapsed, latencyResults, throughputResults, connectionSetupResults);
        }

        private static async Task<List<LatencyBenchmarkResult>> RunResponseTimeSuiteAsync()
        {
            Console.WriteLine("=== Response Time Suite ===");
            Console.WriteLine("Method: client SendAndWaitAsync -> server synchronous response callback");
            Console.WriteLine("Request sizes: 64B, 64KB, 64MB");
            Console.WriteLine("Response size: 4-byte acknowledgement payload");
            Console.WriteLine("");

            List<LatencyBenchmarkResult> results = new List<LatencyBenchmarkResult>();

            foreach (BenchmarkTransportMode transport in Transports)
            {
                foreach (PayloadBenchmarkCase payloadCase in PayloadCases)
                {
                    results.Add(await RunResponseTimeCaseAsync(transport, payloadCase).ConfigureAwait(false));
                }
            }

            return results;
        }

        private static async Task<LatencyBenchmarkResult> RunResponseTimeCaseAsync(BenchmarkTransportMode transport, PayloadBenchmarkCase payloadCase)
        {
            ForceCollection();

            Console.WriteLine("Case: " + transport + " / " + payloadCase.Name + " / " + payloadCase.LatencyIterations + " measured iterations");

            int port = GetAvailablePort();
            byte[] payload = GetPayload(payloadCase.SizeBytes);
            List<double> samples = new List<double>(payloadCase.LatencyIterations);

            WatsonTcpServer server = null;
            WatsonTcpClient client = null;

            try
            {
                server = CreateServer(transport, port);
                client = CreateClient(transport, port);

                server.Events.MessageReceived += NoOpServerMessageReceived;
                server.Callbacks.SyncRequestReceivedAsync = req =>
                {
                    int length = req.Data != null ? req.Data.Length : 0;
                    SyncResponse response = new SyncResponse(req, BitConverter.GetBytes(length));
                    return Task.FromResult(response);
                };

                client.Events.MessageReceived += NoOpClientMessageReceived;

                server.Start();
                client.Connect();

                int warmupIterations = Math.Min(3, payloadCase.LatencyIterations);
                for (int i = 0; i < warmupIterations; i++)
                {
                    await SendLatencyProbeAsync(client, payload, payloadCase).ConfigureAwait(false);
                }

                for (int i = 0; i < payloadCase.LatencyIterations; i++)
                {
                    long start = Stopwatch.GetTimestamp();
                    int length = await SendLatencyProbeAsync(client, payload, payloadCase).ConfigureAwait(false);
                    double elapsedMs = StopwatchTicksToMilliseconds(Stopwatch.GetTimestamp() - start);

                    if (length != payloadCase.SizeBytes)
                    {
                        throw new InvalidOperationException(
                            "Latency probe length mismatch. Expected " + payloadCase.SizeBytes + ", received " + length + ".");
                    }

                    samples.Add(elapsedMs);
                }

            }
            finally
            {
                SafeCleanupClient(client);
                SafeCleanupServer(server);
            }

            StatisticsSummary summary = StatisticsSummary.From(samples);
            Console.WriteLine(
                "  Min " + summary.Min.ToString("N3") + " ms" +
                " | Avg " + summary.Average.ToString("N3") + " ms" +
                " | P50 " + summary.P50.ToString("N3") + " ms" +
                " | P95 " + summary.P95.ToString("N3") + " ms" +
                " | P99 " + summary.P99.ToString("N3") + " ms" +
                " | Max " + summary.Max.ToString("N3") + " ms");
            Console.WriteLine("");

            return new LatencyBenchmarkResult(transport, payloadCase.Name, payloadCase.LatencyIterations, summary);
        }

        private static async Task<int> SendLatencyProbeAsync(WatsonTcpClient client, byte[] payload, PayloadBenchmarkCase payloadCase)
        {
            using (MemoryStream stream = new MemoryStream(payload, false))
            {
                SyncResponse response = await client
                    .SendAndWaitAsync(payloadCase.ResponseTimeoutMs, payload.LongLength, stream)
                    .ConfigureAwait(false);

                if (response == null || response.Data == null || response.Data.Length != 4)
                {
                    throw new InvalidOperationException("Latency probe did not return a valid acknowledgement payload.");
                }

                return BitConverter.ToInt32(response.Data, 0);
            }
        }

        private static async Task<List<ThroughputBenchmarkResult>> RunThroughputSuiteAsync()
        {
            Console.WriteLine("=== Throughput Suite ===");
            Console.WriteLine("Method: client SendAsync(stream) -> server StreamReceivedAsync");
            Console.WriteLine("Sizes: 64B, 64KB, 64MB");
            Console.WriteLine("The 64MB case uses the stream path to avoid a max-connection style test while still exercising large transfers.");
            Console.WriteLine("");

            List<ThroughputBenchmarkResult> results = new List<ThroughputBenchmarkResult>();

            foreach (BenchmarkTransportMode transport in Transports)
            {
                foreach (PayloadBenchmarkCase payloadCase in PayloadCases)
                {
                    foreach (int clientCount in payloadCase.ThroughputClientCounts)
                    {
                        results.Add(await RunThroughputCaseAsync(transport, payloadCase, clientCount).ConfigureAwait(false));
                    }
                }
            }

            return results;
        }

        private static async Task<ThroughputBenchmarkResult> RunThroughputCaseAsync(BenchmarkTransportMode transport, PayloadBenchmarkCase payloadCase, int clientCount)
        {
            ForceCollection();

            int messageCount = payloadCase.ThroughputMessagesPerClient;
            int expectedMessages = clientCount * messageCount;
            long expectedBytes = (long)payloadCase.SizeBytes * (long)expectedMessages;

            Console.WriteLine(
                "Case: " + transport +
                " / " + payloadCase.Name +
                " / clients " + clientCount +
                " / messages per client " + messageCount +
                " / total bytes " + FormatBytes(expectedBytes));

            int port = GetAvailablePort();
            byte[] payload = GetPayload(payloadCase.SizeBytes);
            long receivedBytes = 0;
            int receivedMessages = 0;
            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();

            WatsonTcpServer server = null;
            try
            {
                server = CreateServer(transport, port);
                server.Callbacks.StreamReceivedAsync = async (args, token) =>
                {
                    try
                    {
                        long drained = await DrainStreamAsync(args.DataStream, token).ConfigureAwait(false);
                        Interlocked.Add(ref receivedBytes, drained);

                        if (drained != args.ContentLength)
                        {
                            throw new InvalidOperationException(
                                "Server drained " + drained + " bytes but expected " + args.ContentLength + ".");
                        }

                        int current = Interlocked.Increment(ref receivedMessages);
                        if (current == expectedMessages)
                        {
                            completion.TrySetResult(true);
                        }
                    }
                    catch (Exception e)
                    {
                        completion.TrySetException(e);
                        throw;
                    }
                };

                server.Start();

                List<WatsonTcpClient> clients = new List<WatsonTcpClient>();
                try
                {
                    for (int i = 0; i < clientCount; i++)
                    {
                        WatsonTcpClient client = CreateClient(transport, port);
                        client.Events.MessageReceived += NoOpClientMessageReceived;
                        client.Connect();
                        clients.Add(client);
                    }

                    long suiteStart = Stopwatch.GetTimestamp();
                    List<Task> sendTasks = new List<Task>(clientCount);

                    foreach (WatsonTcpClient client in clients)
                    {
                        sendTasks.Add(SendPayloadBurstAsync(client, payload, messageCount));
                    }

                    await Task.WhenAll(sendTasks).ConfigureAwait(false);
                    double sendCompletionMs = StopwatchTicksToMilliseconds(Stopwatch.GetTimestamp() - suiteStart);

                    Task timeoutTask = Task.Delay(payloadCase.ThroughputTimeoutMs);
                    Task finishedTask = await Task.WhenAny(completion.Task, timeoutTask).ConfigureAwait(false);
                    if (finishedTask != completion.Task)
                    {
                        throw new TimeoutException("Timed out waiting for throughput case to finish receiving.");
                    }

                    await completion.Task.ConfigureAwait(false);
                    double totalCompletionMs = StopwatchTicksToMilliseconds(Stopwatch.GetTimestamp() - suiteStart);

                    if (receivedMessages != expectedMessages)
                    {
                        throw new InvalidOperationException(
                            "Expected " + expectedMessages + " messages but received " + receivedMessages + ".");
                    }

                    if (receivedBytes != expectedBytes)
                    {
                        throw new InvalidOperationException(
                            "Expected " + expectedBytes + " bytes but received " + receivedBytes + ".");
                    }

                    double totalSeconds = totalCompletionMs / 1000.0;
                    if (totalSeconds <= 0) totalSeconds = 0.001;

                    double messagesPerSecond = expectedMessages / totalSeconds;
                    double mebibytesPerSecond = (expectedBytes / 1024d / 1024d) / totalSeconds;

                    Console.WriteLine(
                        "  Send completion " + sendCompletionMs.ToString("N2") + " ms" +
                        " | End-to-end completion " + totalCompletionMs.ToString("N2") + " ms");
                    Console.WriteLine(
                        "  Throughput " + messagesPerSecond.ToString("N2") + " msg/s" +
                        " | " + mebibytesPerSecond.ToString("N2") + " MiB/s");
                    Console.WriteLine("");

                    return new ThroughputBenchmarkResult(
                        transport,
                        payloadCase.Name,
                        clientCount,
                        messageCount,
                        expectedBytes,
                        sendCompletionMs,
                        totalCompletionMs,
                        messagesPerSecond,
                        mebibytesPerSecond);
                }
                finally
                {
                    foreach (WatsonTcpClient client in clients)
                    {
                        SafeCleanupClient(client);
                    }
                }
            }
            finally
            {
                SafeCleanupServer(server);
            }

            throw new InvalidOperationException("Throughput case ended without producing a benchmark result.");
        }

        private static async Task SendPayloadBurstAsync(WatsonTcpClient client, byte[] payload, int messageCount)
        {
            for (int i = 0; i < messageCount; i++)
            {
                using (MemoryStream stream = new MemoryStream(payload, false))
                {
                    bool success = await client.SendAsync(payload.LongLength, stream).ConfigureAwait(false);
                    if (!success)
                    {
                        throw new InvalidOperationException("SendAsync returned false during throughput benchmarking.");
                    }
                }
            }
        }

        private static async Task<long> DrainStreamAsync(Stream stream, CancellationToken token)
        {
            byte[] buffer = new byte[DrainBufferSize];
            long total = 0;

            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                if (read <= 0) break;
                total += read;
            }

            return total;
        }

        private static async Task<List<ConnectionSetupBenchmarkResult>> RunConnectionSetupSuiteAsync()
        {
            Console.WriteLine("=== Connection Setup Suite ===");
            Console.WriteLine("Method: repeated connect/disconnect cycles against a live loopback server");
            Console.WriteLine("This suite measures connection establishment only. It does not test maximum simultaneous connections.");
            Console.WriteLine("");

            List<ConnectionSetupBenchmarkResult> results = new List<ConnectionSetupBenchmarkResult>();

            foreach (BenchmarkTransportMode transport in Transports)
            {
                foreach (ConnectionSetupCase setupCase in SetupCases)
                {
                    results.Add(await RunConnectionSetupCaseAsync(transport, setupCase).ConfigureAwait(false));
                }
            }

            return results;
        }

        private static async Task<ConnectionSetupBenchmarkResult> RunConnectionSetupCaseAsync(BenchmarkTransportMode transport, ConnectionSetupCase setupCase)
        {
            ForceCollection();

            Console.WriteLine(
                "Case: " + transport +
                " / " + setupCase.Name +
                " / batch size " + setupCase.BatchSize +
                " / batches " + setupCase.BatchIterations);

            int port = GetAvailablePort();
            List<double> connectionSamples = new List<double>(setupCase.BatchSize * setupCase.BatchIterations);
            List<double> batchSamples = new List<double>(setupCase.BatchIterations);

            WatsonTcpServer server = null;
            try
            {
                server = CreateServer(transport, port);
                server.Events.MessageReceived += NoOpServerMessageReceived;
                server.Start();

                for (int batchIndex = 0; batchIndex < setupCase.BatchIterations; batchIndex++)
                {
                    List<ConnectionMeasurement> measurements = new List<ConnectionMeasurement>(setupCase.BatchSize);
                    long batchStart = Stopwatch.GetTimestamp();

                    try
                    {
                        List<Task<ConnectionMeasurement>> tasks = new List<Task<ConnectionMeasurement>>(setupCase.BatchSize);
                        for (int i = 0; i < setupCase.BatchSize; i++)
                        {
                            tasks.Add(Task.Run(() => ConnectClientAsync(transport, port)));
                        }

                        ConnectionMeasurement[] batchMeasurements = await Task.WhenAll(tasks).ConfigureAwait(false);
                        measurements.AddRange(batchMeasurements);

                        foreach (ConnectionMeasurement measurement in measurements)
                        {
                            connectionSamples.Add(measurement.ElapsedMilliseconds);
                        }
                    }
                    finally
                    {
                        foreach (ConnectionMeasurement measurement in measurements)
                        {
                            SafeCleanupClient(measurement.Client);
                        }
                    }

                    double batchElapsed = StopwatchTicksToMilliseconds(Stopwatch.GetTimestamp() - batchStart);
                    batchSamples.Add(batchElapsed);
                }
            }
            finally
            {
                SafeCleanupServer(server);
            }

            StatisticsSummary perConnection = StatisticsSummary.From(connectionSamples);
            StatisticsSummary perBatch = StatisticsSummary.From(batchSamples);

            Console.WriteLine(
                "  Per-connection: Min " + perConnection.Min.ToString("N3") + " ms" +
                " | Avg " + perConnection.Average.ToString("N3") + " ms" +
                " | P50 " + perConnection.P50.ToString("N3") + " ms" +
                " | P95 " + perConnection.P95.ToString("N3") + " ms" +
                " | Max " + perConnection.Max.ToString("N3") + " ms");
            Console.WriteLine(
                "  Per-batch     : Min " + perBatch.Min.ToString("N3") + " ms" +
                " | Avg " + perBatch.Average.ToString("N3") + " ms" +
                " | P50 " + perBatch.P50.ToString("N3") + " ms" +
                " | P95 " + perBatch.P95.ToString("N3") + " ms" +
                " | Max " + perBatch.Max.ToString("N3") + " ms");
            Console.WriteLine("");

            return new ConnectionSetupBenchmarkResult(transport, setupCase.Name, setupCase.BatchSize, setupCase.BatchIterations, perConnection, perBatch);
        }

        private static ConnectionMeasurement ConnectClientAsync(BenchmarkTransportMode transport, int port)
        {
            WatsonTcpClient client = CreateClient(transport, port);
            client.Events.MessageReceived += NoOpClientMessageReceived;

            long start = Stopwatch.GetTimestamp();
            client.Connect();
            double elapsedMs = StopwatchTicksToMilliseconds(Stopwatch.GetTimestamp() - start);

            return new ConnectionMeasurement(client, elapsedMs);
        }

        private static WatsonTcpServer CreateServer(BenchmarkTransportMode transport, int port)
        {
            WatsonTcpServer server;

            if (transport == BenchmarkTransportMode.Tcp)
            {
                server = new WatsonTcpServer(Host, port);
            }
            else
            {
                server = new WatsonTcpServer(Host, port, GetCertificatePath(), CertificatePassword);
                server.Settings.AcceptInvalidCertificates = true;
                server.Settings.MutuallyAuthenticate = false;
            }

            server.Settings.NoDelay = true;
            return server;
        }

        private static WatsonTcpClient CreateClient(BenchmarkTransportMode transport, int port)
        {
            WatsonTcpClient client;

            if (transport == BenchmarkTransportMode.Tcp)
            {
                client = new WatsonTcpClient(Host, port);
            }
            else
            {
                client = new WatsonTcpClient(Host, port, (string)null, (string)null);
                client.Settings.AcceptInvalidCertificates = true;
                client.Settings.MutuallyAuthenticate = false;
            }

            client.Settings.NoDelay = true;
            return client;
        }

        private static string GetCertificatePath()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "test.pfx");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Unable to find test certificate at " + path + ".");
            }

            return path;
        }

        private static byte[] GetPayload(int sizeBytes)
        {
            lock (PayloadLock)
            {
                byte[] payload;
                if (PayloadCache.TryGetValue(sizeBytes, out payload))
                {
                    return payload;
                }

                payload = new byte[sizeBytes];
                for (int i = 0; i < payload.Length; i++)
                {
                    payload[i] = (byte)(i % 251);
                }

                PayloadCache[sizeBytes] = payload;
                return payload;
            }
        }

        private static int GetAvailablePort()
        {
            // Prefer randomized high ports to reduce collisions with other local test runs.
            for (int attempt = 0; attempt < MaxPortSelectionAttempts; attempt++)
            {
                int candidatePort = RandomNumberGenerator.GetInt32(MinBenchmarkPort, MaxBenchmarkPort);
                if (TryBindLoopbackPort(candidatePort))
                {
                    return candidatePort;
                }
            }

            // Fall back to the OS ephemeral allocator if the randomized attempts are exhausted.
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static bool TryBindLoopbackPort(int port)
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, port);

            try
            {
                listener.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                try
                {
                    listener.Stop();
                }
                catch (SocketException)
                {
                }
            }
        }

        private static void PrintHeader()
        {
            Console.WriteLine("WatsonTcp Performance Benchmark");
            Console.WriteLine("===============================");
            Console.WriteLine("Host      : " + Host);
            Console.WriteLine("Branch    : feature/performance");
            Console.WriteLine("Scope     : response time, throughput, connection setup time");
            Console.WriteLine("Excluded  : total number of connections");
            Console.WriteLine("");
        }

        private static void PrintSuiteSummary()
        {
            Console.WriteLine("Configured suite summary");
            Console.WriteLine("  Transports : TCP, SSL");
            Console.WriteLine("  Payloads   : 64B, 64KB, 64MB");
            Console.WriteLine("  Setup      : sequential and burst x4 connection establishment");
            Console.WriteLine("");
        }

        private static void PrintDetailedSuiteSummary()
        {
            Console.WriteLine("Suite details");
            Console.WriteLine("");
            Console.WriteLine("Response time cases");
            foreach (PayloadBenchmarkCase payloadCase in PayloadCases)
            {
                Console.WriteLine(
                    "  " + payloadCase.Name +
                    " -> " + payloadCase.LatencyIterations +
                    " measured iterations, timeout " + payloadCase.ResponseTimeoutMs + "ms");
            }

            Console.WriteLine("");
            Console.WriteLine("Throughput cases");
            foreach (PayloadBenchmarkCase payloadCase in PayloadCases)
            {
                Console.WriteLine(
                    "  " + payloadCase.Name +
                    " -> " + payloadCase.ThroughputMessagesPerClient +
                    " messages/client, client counts [" + String.Join(", ", payloadCase.ThroughputClientCounts) + "], timeout " + payloadCase.ThroughputTimeoutMs + "ms");
            }

            Console.WriteLine("");
            Console.WriteLine("Connection setup cases");
            foreach (ConnectionSetupCase setupCase in SetupCases)
            {
                Console.WriteLine(
                    "  " + setupCase.Name +
                    " -> batch size " + setupCase.BatchSize +
                    ", batches " + setupCase.BatchIterations);
            }
        }

        private static void PrintFinalSummary(
            DateTimeOffset runStarted,
            DateTimeOffset runCompleted,
            TimeSpan elapsed,
            List<LatencyBenchmarkResult> latencyResults,
            List<ThroughputBenchmarkResult> throughputResults,
            List<ConnectionSetupBenchmarkResult> connectionSetupResults)
        {
            RestoreConsoleOutput();
            Console.WriteLine("=== Final Summary ===");
            Console.WriteLine("Run started  : " + runStarted.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            Console.WriteLine("Run completed: " + runCompleted.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            Console.WriteLine("Elapsed      : " + FormatDuration(elapsed.TotalSeconds));
            Console.WriteLine("");
            Console.WriteLine("The tables below are the skim view for this run.");
            Console.WriteLine("");

            PrintTable(
                "Response Time",
                new[] { "Transport", "Payload", "Iter", "Min ms", "Avg ms", "P50 ms", "P95 ms", "P99 ms", "Max ms" },
                latencyResults.Select(
                    result => new[]
                    {
                        result.Transport.ToString(),
                        result.PayloadName,
                        result.Iterations.ToString(),
                        FormatNumber(result.Summary.Min, 3),
                        FormatNumber(result.Summary.Average, 3),
                        FormatNumber(result.Summary.P50, 3),
                        FormatNumber(result.Summary.P95, 3),
                        FormatNumber(result.Summary.P99, 3),
                        FormatNumber(result.Summary.Max, 3)
                    }));

            PrintTable(
                "Throughput",
                new[] { "Transport", "Payload", "Clients", "Msg/Client", "Total Bytes", "Send ms", "End-to-End ms", "Msg/s", "MiB/s" },
                throughputResults.Select(
                    result => new[]
                    {
                        result.Transport.ToString(),
                        result.PayloadName,
                        result.ClientCount.ToString(),
                        result.MessagesPerClient.ToString(),
                        FormatBytes(result.TotalBytes),
                        FormatNumber(result.SendCompletionMs, 2),
                        FormatNumber(result.EndToEndCompletionMs, 2),
                        FormatNumber(result.MessagesPerSecond, 2),
                        FormatNumber(result.MebibytesPerSecond, 2)
                    }));

            PrintTable(
                "Connection Setup",
                new[] { "Transport", "Case", "Batch", "Batches", "Conn Avg ms", "Conn P95 ms", "Conn Max ms", "Batch Avg ms", "Batch P95 ms", "Batch Max ms" },
                connectionSetupResults.Select(
                    result => new[]
                    {
                        result.Transport.ToString(),
                        result.CaseName,
                        result.BatchSize.ToString(),
                        result.BatchIterations.ToString(),
                        FormatNumber(result.PerConnectionSummary.Average, 3),
                        FormatNumber(result.PerConnectionSummary.P95, 3),
                        FormatNumber(result.PerConnectionSummary.Max, 3),
                        FormatNumber(result.PerBatchSummary.Average, 3),
                        FormatNumber(result.PerBatchSummary.P95, 3),
                        FormatNumber(result.PerBatchSummary.Max, 3)
                    }));
        }

        private static void ConfigureConsoleOutput(BenchmarkCommandLineOptions options)
        {
            _summaryOnly = options.SummaryOnly;

            if (_summaryOnly)
            {
                Console.SetOut(TextWriter.Null);
            }
        }

        private static void RestoreConsoleOutput()
        {
            if (_summaryOnly && !ReferenceEquals(Console.Out, OriginalStandardOutput))
            {
                Console.SetOut(OriginalStandardOutput);
            }
        }

        private static void PrintTable(string title, string[] headers, IEnumerable<string[]> rows)
        {
            List<string[]> rowList = rows.ToList();
            int[] widths = new int[headers.Length];

            for (int i = 0; i < headers.Length; i++)
            {
                widths[i] = headers[i].Length;
            }

            foreach (string[] row in rowList)
            {
                for (int i = 0; i < headers.Length; i++)
                {
                    widths[i] = Math.Max(widths[i], row[i] != null ? row[i].Length : 0);
                }
            }

            string border = "+" + String.Join("+", widths.Select(width => new string('-', width + 2))) + "+";

            Console.WriteLine(title);
            Console.WriteLine(border);
            Console.WriteLine("| " + String.Join(" | ", headers.Select((header, index) => PadRight(header, widths[index]))) + " |");
            Console.WriteLine(border);

            foreach (string[] row in rowList)
            {
                Console.WriteLine("| " + String.Join(" | ", row.Select((value, index) => PadRight(value, widths[index]))) + " |");
            }

            Console.WriteLine(border);
            Console.WriteLine("");
        }

        private static string PadRight(string value, int width)
        {
            return (value ?? String.Empty).PadRight(width);
        }

        private static double StopwatchTicksToMilliseconds(long ticks)
        {
            return (ticks * 1000d) / Stopwatch.Frequency;
        }

        private static string FormatDuration(double totalSeconds)
        {
            TimeSpan duration = TimeSpan.FromSeconds(totalSeconds);
            if (duration.TotalMinutes >= 1)
            {
                return duration.TotalMinutes.ToString("N2") + " minutes";
            }

            return duration.TotalSeconds.ToString("N2") + " seconds";
        }

        private static string FormatBytes(long bytes)
        {
            double value = bytes;
            string[] units = { "B", "KB", "MB", "GB" };
            int unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return value.ToString("N2") + " " + units[unit];
        }

        private static string FormatNumber(double value, int decimals)
        {
            return value.ToString("N" + decimals);
        }

        private static void SafeCleanupClient(WatsonTcpClient client)
        {
            if (client == null) return;

            // Benchmark teardown only closes active transports; WatsonTcp.Dispose currently
            // may surface cancellation aggregates during normal shutdown and abort the suite.
            try
            {
                client.Disconnect(false);
            }
            catch (Exception e)
            {
                HandleCleanupException("client disconnect", e);
            }
        }

        private static void SafeCleanupServer(WatsonTcpServer server)
        {
            if (server == null) return;

            // Benchmark teardown only closes active transports; WatsonTcp.Dispose currently
            // may surface cancellation aggregates during normal shutdown and abort the suite.
            try
            {
                server.Stop();
            }
            catch (Exception e)
            {
                HandleCleanupException("server stop", e);
            }
        }

        private static void HandleCleanupException(string operation, Exception exception)
        {
            if (IsExpectedCleanupException(exception))
            {
                return;
            }

            Console.WriteLine("Cleanup warning (" + operation + "): " + exception.Message);
        }

        private static bool IsExpectedCleanupException(Exception exception)
        {
            if (exception == null)
            {
                return true;
            }

            if (exception is AggregateException aggregateException)
            {
                return aggregateException
                    .Flatten()
                    .InnerExceptions
                    .All(inner => IsExpectedCleanupException(inner));
            }

            if (exception is TaskCanceledException) return true;
            if (exception is OperationCanceledException) return true;
            if (exception is ObjectDisposedException) return true;
            if (exception is IOException) return true;
            if (exception is InvalidOperationException) return true;

            return false;
        }

        private static void ForceCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static void NoOpServerMessageReceived(object sender, MessageReceivedEventArgs args)
        {
        }

        private static void NoOpClientMessageReceived(object sender, MessageReceivedEventArgs args)
        {
        }
    }

    internal enum BenchmarkTransportMode
    {
        Tcp,
        Ssl
    }

    internal sealed class PayloadBenchmarkCase
    {
        internal string Name { get; private set; }
        internal int SizeBytes { get; private set; }
        internal int LatencyIterations { get; private set; }
        internal int ThroughputMessagesPerClient { get; private set; }
        internal int[] ThroughputClientCounts { get; private set; }
        internal int ResponseTimeoutMs { get; private set; }
        internal int ThroughputTimeoutMs { get; private set; }

        internal PayloadBenchmarkCase(
            string name,
            int sizeBytes,
            int latencyIterations,
            int throughputMessagesPerClient,
            int[] throughputClientCounts,
            int responseTimeoutMs,
            int throughputTimeoutMs)
        {
            Name = name;
            SizeBytes = sizeBytes;
            LatencyIterations = latencyIterations;
            ThroughputMessagesPerClient = throughputMessagesPerClient;
            ThroughputClientCounts = throughputClientCounts;
            ResponseTimeoutMs = responseTimeoutMs;
            ThroughputTimeoutMs = throughputTimeoutMs;
        }
    }

    internal sealed class ConnectionSetupCase
    {
        internal string Name { get; private set; }
        internal int BatchSize { get; private set; }
        internal int BatchIterations { get; private set; }

        internal ConnectionSetupCase(string name, int batchSize, int batchIterations)
        {
            Name = name;
            BatchSize = batchSize;
            BatchIterations = batchIterations;
        }
    }

    internal sealed class ConnectionMeasurement
    {
        internal WatsonTcpClient Client { get; private set; }
        internal double ElapsedMilliseconds { get; private set; }

        internal ConnectionMeasurement(WatsonTcpClient client, double elapsedMilliseconds)
        {
            Client = client;
            ElapsedMilliseconds = elapsedMilliseconds;
        }
    }

    internal sealed class StatisticsSummary
    {
        internal double Min { get; private set; }
        internal double Average { get; private set; }
        internal double P50 { get; private set; }
        internal double P95 { get; private set; }
        internal double P99 { get; private set; }
        internal double Max { get; private set; }

        private StatisticsSummary()
        {
        }

        internal static StatisticsSummary From(IList<double> values)
        {
            if (values == null || values.Count < 1)
            {
                throw new ArgumentException("At least one value is required.", nameof(values));
            }

            List<double> ordered = values.OrderBy(v => v).ToList();
            double average = values.Average();

            StatisticsSummary summary = new StatisticsSummary();
            summary.Min = ordered[0];
            summary.Average = average;
            summary.P50 = Percentile(ordered, 0.50);
            summary.P95 = Percentile(ordered, 0.95);
            summary.P99 = Percentile(ordered, 0.99);
            summary.Max = ordered[ordered.Count - 1];
            return summary;
        }

        private static double Percentile(IList<double> orderedValues, double percentile)
        {
            if (orderedValues.Count == 1)
            {
                return orderedValues[0];
            }

            double position = percentile * (orderedValues.Count - 1);
            int lowerIndex = (int)Math.Floor(position);
            int upperIndex = (int)Math.Ceiling(position);

            if (lowerIndex == upperIndex)
            {
                return orderedValues[lowerIndex];
            }

            double weight = position - lowerIndex;
            return orderedValues[lowerIndex] + ((orderedValues[upperIndex] - orderedValues[lowerIndex]) * weight);
        }
    }

    internal sealed class LatencyBenchmarkResult
    {
        internal BenchmarkTransportMode Transport { get; private set; }
        internal string PayloadName { get; private set; }
        internal int Iterations { get; private set; }
        internal StatisticsSummary Summary { get; private set; }

        internal LatencyBenchmarkResult(
            BenchmarkTransportMode transport,
            string payloadName,
            int iterations,
            StatisticsSummary summary)
        {
            Transport = transport;
            PayloadName = payloadName;
            Iterations = iterations;
            Summary = summary;
        }
    }

    internal sealed class ThroughputBenchmarkResult
    {
        internal BenchmarkTransportMode Transport { get; private set; }
        internal string PayloadName { get; private set; }
        internal int ClientCount { get; private set; }
        internal int MessagesPerClient { get; private set; }
        internal long TotalBytes { get; private set; }
        internal double SendCompletionMs { get; private set; }
        internal double EndToEndCompletionMs { get; private set; }
        internal double MessagesPerSecond { get; private set; }
        internal double MebibytesPerSecond { get; private set; }

        internal ThroughputBenchmarkResult(
            BenchmarkTransportMode transport,
            string payloadName,
            int clientCount,
            int messagesPerClient,
            long totalBytes,
            double sendCompletionMs,
            double endToEndCompletionMs,
            double messagesPerSecond,
            double mebibytesPerSecond)
        {
            Transport = transport;
            PayloadName = payloadName;
            ClientCount = clientCount;
            MessagesPerClient = messagesPerClient;
            TotalBytes = totalBytes;
            SendCompletionMs = sendCompletionMs;
            EndToEndCompletionMs = endToEndCompletionMs;
            MessagesPerSecond = messagesPerSecond;
            MebibytesPerSecond = mebibytesPerSecond;
        }
    }

    internal sealed class ConnectionSetupBenchmarkResult
    {
        internal BenchmarkTransportMode Transport { get; private set; }
        internal string CaseName { get; private set; }
        internal int BatchSize { get; private set; }
        internal int BatchIterations { get; private set; }
        internal StatisticsSummary PerConnectionSummary { get; private set; }
        internal StatisticsSummary PerBatchSummary { get; private set; }

        internal ConnectionSetupBenchmarkResult(
            BenchmarkTransportMode transport,
            string caseName,
            int batchSize,
            int batchIterations,
            StatisticsSummary perConnectionSummary,
            StatisticsSummary perBatchSummary)
        {
            Transport = transport;
            CaseName = caseName;
            BatchSize = batchSize;
            BatchIterations = batchIterations;
            PerConnectionSummary = perConnectionSummary;
            PerBatchSummary = perBatchSummary;
        }
    }

    internal sealed class BenchmarkCommandLineOptions
    {
        internal bool SummaryOnly { get; private set; }

        internal static BenchmarkCommandLineOptions Parse(string[] args)
        {
            BenchmarkCommandLineOptions options = new BenchmarkCommandLineOptions();

            if (args == null)
            {
                return options;
            }

            foreach (string arg in args)
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(arg, "--summary-only"))
                {
                    options.SummaryOnly = true;
                }
            }

            return options;
        }
    }
}
