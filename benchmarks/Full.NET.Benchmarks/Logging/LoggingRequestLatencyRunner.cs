using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Full.NET.Hosting.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Benchmarks.Logging;

/// <summary>真实 Kestrel 上的有限日志请求对照；不冒充认证、数据库或 Kafka 的业务 API 容量。</summary>
public static class LoggingRequestLatencyRunner
{
    private const int Warmup = 200;
    private const int ShortRequests = 2000;
    private const int Concurrency = 8;
    private const string Route = "/api/logging-probe/{sequence:int}";

    /// <summary>独立请求进程只测量响应和出口丢弃；收据明确交给外部父进程验收。</summary>
    public static Task<object> RunIsolatedRouteCaseAsync(string directory, string route,
        IReadOnlyDictionary<string, string?>? kafkaConfiguration, Func<IConfiguration, IHostLogSnapshotExporter>? createExporter,
        Action<bool> measurementStateChanged, CancellationToken token, int? capacityRate = null)
    {
        if (route is not ("Collector" or "ApplicationKafka")
            || (route == "ApplicationKafka" && (kafkaConfiguration is null || createExporter is null))
            || (route == "Collector" && (kafkaConfiguration is not null || createExporter is not null))
            || kafkaConfiguration?.Keys.Any(key => !key.StartsWith("FullNet:Logging:Kafka:", StringComparison.Ordinal)) == true)
            throw new ArgumentException("独立请求档仅允许受控 Collector/ApplicationKafka 配置。");
        ArgumentNullException.ThrowIfNull(measurementStateChanged);
        var load = capacityRate is { } rate ? LoggingRequestLoadProfile.Capacity(rate) : LoggingRequestLoadProfile.Comparison;
        Directory.CreateDirectory(directory);
        if (File.Exists(Path.Combine(directory, LoggingRequestWarmupBarrier.FileName)))
            throw new InvalidOperationException("独立探针不得复用旧预热确认。");
        return RunCaseAsync(directory, "Projected", 1, load.Requests, load.RequestsPerSecond, token, kafkaConfiguration, createExporter,
            deliveryMode: route, measurementStateChanged: measurementStateChanged, externalReconciliation: true,
            maximumSendMilliseconds: load.SendBudgetMilliseconds,
            waitForWarmup: cancellation => LoggingRequestWarmupBarrier.WaitAsync(directory, cancellation));
    }

    /// <summary>独立采集回放使用真实 Collector 入口生成固定请求档，不持有 Kafka 凭据。</summary>
    public static Task<object> RunCollectorCaseAsync(string directory, CancellationToken cancellationToken,
        Action<bool>? measurementStateChanged = null)
    {
        Directory.CreateDirectory(directory);
        return RunCaseAsync(directory, "Projected", 1, 5000, 500, cancellationToken, deliveryMode: "Collector",
            measurementStateChanged: measurementStateChanged);
    }

    /// <summary>由真实 Broker 夹具提供正式出口及最终收据，复用固定持续请求档。</summary>
    public static Task<object> RunApplicationKafkaCaseAsync(string directory, string mode,
        IReadOnlyDictionary<string, string?> kafkaConfiguration,
        Func<IConfiguration, IHostLogSnapshotExporter> createExporter,
        Func<CancellationToken, Task<IReadOnlyList<string>>> readBrokerReceipts,
        CancellationToken cancellationToken, TimeSpan? receiptTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(kafkaConfiguration);
        ArgumentNullException.ThrowIfNull(createExporter);
        ArgumentNullException.ThrowIfNull(readBrokerReceipts);
        if (receiptTimeout is { } timeout && (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromSeconds(120)))
            throw new ArgumentOutOfRangeException(nameof(receiptTimeout));
        if (mode is not ("Summary" or "Projected")
            || kafkaConfiguration.Keys.Any(key => !key.StartsWith("FullNet:Logging:Kafka:", StringComparison.Ordinal)))
            throw new ArgumentException("Kafka 对照仅接受 Summary/Projected 和日志专用配置。");
        Directory.CreateDirectory(directory);
        return RunCaseAsync(directory, mode, 1, 5000, 500, cancellationToken,
            kafkaConfiguration, createExporter, readBrokerReceipts, receiptTimeout);
    }

    /// <summary>容量档显式使用实际 Local 文件，固定发送三十秒并保留原短档的行为。</summary>
    public static Task<object> RunLocalFileCapacityCaseAsync(string directory, string mode, int round,
        int requestsPerSecond, CancellationToken cancellationToken)
    {
        if (mode is not ("Disabled" or "Summary" or "Projected") || round is < 1 or > 2
            || requestsPerSecond is not (500 or 1000))
            throw new ArgumentException("Local 容量只允许既定捕获档、两轮及 500/1000 每秒。");
        return RunCaseAsync(directory, mode, round, requestsPerSecond * 30, requestsPerSecond,
            cancellationToken, localFile: true);
    }

    /// <summary>运行固定正反序两轮并在全部对账后保存通过报告；输出目录必须显式指定。</summary>
    public static async Task RunAsync(string[] args)
    {
        if ((args.Length != 2 && args.Length != 3) || args[0] != "--output" || (args.Length == 3 && args[2] != "--sustained"))
            throw new ArgumentException("用法：logging-request-latency --output <本地结果目录> [--sustained]");
        var sustained = args.Length == 3;
        var requests = sustained ? 5000 : ShortRequests;
        var requestsPerSecond = sustained ? 500 : 0;
        var directory = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(directory);
        var reportPath = Path.Combine(directory, "result.json");
        File.Delete(reportPath);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var reports = new List<object>();
        foreach (var (mode, round) in new[] { ("Disabled", 1), ("Summary", 1), ("Projected", 1),
                     ("Projected", 2), ("Summary", 2), ("Disabled", 2) })
        {
            reports.Add(await RunCaseAsync(directory, mode, round, requests, requestsPerSecond, deadline.Token));
            Console.WriteLine($"logging-request-latency {mode} round={round} verified");
        }
        var json = JsonSerializer.Serialize(new
        {
            passed = true, completedAtUtc = DateTimeOffset.UtcNow,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            processorCount = Environment.ProcessorCount, concurrency = Concurrency,
            warmupRequestsPerCase = Warmup, measuredRequestsPerCase = requests, targetRequestsPerSecond = requestsPerSecond, sustained, reports,
            scope = "Loopback Kestrel and real Full.NET B2 middleware; shared client/server process; Console JSON redirected to local file; HTTP receipts reconciled after disposal; drop counter sampled before disposal; CPU/allocation measured during send only; no authentication/database/Kafka or full business API P99 claim",
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(reportPath, json, deadline.Token);
    }

    private static async Task<object> RunCaseAsync(string directory, string mode, int round, int requests, int requestsPerSecond, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? kafkaConfiguration = null,
        Func<IConfiguration, IHostLogSnapshotExporter>? createExporter = null,
        Func<CancellationToken, Task<IReadOnlyList<string>>>? readBrokerReceipts = null, TimeSpan? receiptTimeout = null,
        string? deliveryMode = null, Action<bool>? measurementStateChanged = null, bool externalReconciliation = false, bool localFile = false,
        int? maximumSendMilliseconds = null, Func<CancellationToken, Task>? waitForWarmup = null)
    {
        var logPath = Path.Combine(directory, $"{mode}-{round}.jsonl");
        await using var output = new StreamWriter(logPath, false, new UTF8Encoding(false), 16384) { AutoFlush = true };
        var originalOutput = Console.Out;
        WebApplication? app = null;
        var latencies = new double[requests];
        var dispatchDelays = new double[requests];
        var unexpected = 0;
        var activeRequests = 0;
        var maximumActiveRequests = 0;
        long preDisposalDroppedMessages = 0;
        long allocated = 0;
        double cpuMilliseconds = 0;
        double elapsedMilliseconds = 0;
        long endOfSendWorkingSetBytes = 0;
        Console.SetOut(output);
        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = [], EnvironmentName = "Development", ApplicationName = typeof(LoggingRequestLatencyRunner).Assembly.GetName().Name,
            });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FullNet:Logging:DeliveryMode"] = deliveryMode ?? (kafkaConfiguration is null ? "Local" : "ApplicationKafka"),
                ["FullNet:Logging:ExpectedDeliveryMode"] = deliveryMode ?? (kafkaConfiguration is null ? "Local" : "ApplicationKafka"),
                ["FullNet:Logging:IndexRouteVersion"] = "1",
                ["FullNet:Logging:IndexRetentionDays"] = "30",
                ["Observability:HttpOperation:Enabled"] = mode == "Disabled" ? "false" : "true",
                ["Observability:HttpOperation:CaptureMode"] = mode == "Projected" ? "SanitizedPayload" : "Summary",
                ["Observability:HttpOperation:SuccessSampleRate"] = "1",
                ["Observability:HttpOperation:MaxRequestPayloadBytes"] = "1024",
                ["Observability:HttpOperation:MaxResponsePayloadBytes"] = "1024",
                ["Observability:HttpOperation:CaptureMaxEventsPerSecond"] = "100000",
                ["Observability:HttpOperation:CaptureMaxBytesPerSecond"] = "67108864",
                ["Observability:HttpOperation:PayloadRouteAllowList:0"] = Route,
            });
            if (localFile) builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FullNet:Logging:LocalFile:Enabled"] = "true",
                ["FullNet:Logging:LocalFile:Directory"] = Path.Combine(directory, "local"),
            });
            if (kafkaConfiguration is not null) builder.Configuration.AddInMemoryCollection(kafkaConfiguration);
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.AddFullNetServiceDefaults(createExporter);
            app = builder.Build();
            if (localFile || externalReconciliation) app.Use(async (context, next) =>
            {
                // 在真实服务端包围完整请求，不能把八个发送循环当作实际在途数。
                var current = Interlocked.Increment(ref activeRequests);
                int previous;
                while (current > (previous = Volatile.Read(ref maximumActiveRequests))
                    && Interlocked.CompareExchange(ref maximumActiveRequests, current, previous) != previous) { }
                try { await next(context); }
                finally { Interlocked.Decrement(ref activeRequests); }
            });
            app.UseFullNetRequestLogging();
            app.MapGet(Route, (int sequence, HttpContext context, HttpOperationPayloadProjection projection) =>
            {
                // 隔离比较按请求身份划分阶段，避免预热响应完成后才发出的日志混入实测。
                if (externalReconciliation || localFile)
                    context.TraceIdentifier = $"logging-probe:{context.Request.Query["phase"]}:{sequence}";
                context.Response.StatusCode = sequence % 10 == 0 ? 500 : 200;
                // 每档调用相同入口；未取得许可时不得提前构造投影。
                if (projection.TryBeginCapture(context, HttpLogCaptureTarget.B2InternalSummary,
                        HttpLogCaptureProjectionKeys.Pagination, out var requestLease))
                {
                    using (requestLease)
                        requestLease!.Capture(() => new HttpPaginationLogProjection(1, 20), HttpLogCaptureJsonContext.Default.HttpPaginationLogProjection);
                }
                if (projection.TryBeginCapture(context, HttpLogCaptureTarget.B2InternalSummary,
                        HttpLogCaptureProjectionKeys.PaginationResult, out var responseLease))
                {
                    using (responseLease)
                        responseLease!.Capture(() => new HttpPaginationResultLogProjection(1, 20, 100, 20), HttpLogCaptureJsonContext.Default.HttpPaginationResultLogProjection);
                }
                return Results.Text("logging-probe", statusCode: context.Response.StatusCode);
            });
            await app.StartAsync(cancellationToken);
            var monitors = app.Services.GetRequiredService<FullNetLoggingMonitors>();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var handler = new SocketsHttpHandler { UseProxy = false, MaxConnectionsPerServer = Concurrency };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
            async Task<int> SendAsync(int count, double[]? timings, CancellationToken sendToken)
            {
                var next = -1;
                var errors = 0;
                var scheduleStarted = Stopwatch.GetTimestamp();
                await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(async _ =>
                {
                    int sequence;
                    while ((sequence = Interlocked.Increment(ref next)) < count)
                    {
                        // 固定节奏只等待自己的发送时点，最多八个循环；晚到不隐藏，单独记录调度落后。
                        if (timings is not null && requestsPerSecond > 0)
                        {
                            var due = LoggingRequestEvidence.ScheduledMilliseconds(sequence, requestsPerSecond);
                            double remaining;
                            while ((remaining = due - Stopwatch.GetElapsedTime(scheduleStarted).TotalMilliseconds) > 0)
                                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, remaining)), sendToken);
                            dispatchDelays[sequence] = Math.Max(0, Stopwatch.GetElapsedTime(scheduleStarted).TotalMilliseconds - due);
                        }
                        var started = Stopwatch.GetTimestamp();
                        var phase = externalReconciliation || localFile ? $"?phase={(timings is null ? "warmup" : "measured")}" : "";
                        using var response = await client.GetAsync($"/api/logging-probe/{sequence}{phase}", sendToken);
                        var body = await response.Content.ReadAsStringAsync(sendToken);
                        if ((int)response.StatusCode != (sequence % 10 == 0 ? 500 : 200) || body != "logging-probe")
                            Interlocked.Increment(ref errors);
                        if (timings is not null) timings[sequence] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    }
                }));
                return errors;
            }
            if (await SendAsync(Warmup, null, cancellationToken) != 0) throw new InvalidOperationException("预热响应对账失败。");
            if (waitForWarmup is not null) await waitForWarmup(cancellationToken);
            Interlocked.Exchange(ref maximumActiveRequests, 0);
            using var process = Process.GetCurrentProcess();
            var cpuBefore = process.TotalProcessorTime;
            var allocatedBefore = GC.GetTotalAllocatedBytes(true);
            using var sendDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var sendBudgetMilliseconds = maximumSendMilliseconds ?? (localFile ? 50000 : 30000);
            if (requestsPerSecond > 0) sendDeadline.CancelAfter(TimeSpan.FromMilliseconds(sendBudgetMilliseconds));
            var clock = Stopwatch.StartNew();
            measurementStateChanged?.Invoke(true);
            try { unexpected = await SendAsync(requests, latencies, sendDeadline.Token); }
            finally { measurementStateChanged?.Invoke(false); }
            clock.Stop();
            process.Refresh();
            elapsedMilliseconds = clock.Elapsed.TotalMilliseconds;
            if (requestsPerSecond > 0) LoggingRequestEvidence.ValidateSustainedWindow(requests, requestsPerSecond, elapsedMilliseconds, sendBudgetMilliseconds);
            cpuMilliseconds = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            endOfSendWorkingSetBytes = process.WorkingSet64;
            allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
            await app.StopAsync(cancellationToken);
            preDisposalDroppedMessages = monitors.General.Snapshot.DroppedMessagesCount + monitors.HighPriority.Snapshot.DroppedMessagesCount;
        }
        finally
        {
            try { if (app is not null) await app.DisposeAsync(); }
            finally { Console.SetOut(originalOutput); }
        }
        await output.FlushAsync(cancellationToken);
        // Windows 的只读共享打开不能与仍持有写句柄的输出文件并存，先完成释放再对账。
        await output.DisposeAsync();
        if (localFile || externalReconciliation)
            await File.WriteAllTextAsync(Path.Combine(directory, "measurement.json"), JsonSerializer.Serialize(new
            {
                mode, round, requests, requestsPerSecond, unexpected, preDisposalDroppedMessages,
                elapsedMilliseconds, sharedProcessCpuMilliseconds = cpuMilliseconds, endOfSendWorkingSetBytes,
                latencyMilliseconds = latencies, dispatchDelayMilliseconds = dispatchDelays,
            }), cancellationToken);
        var receiptPath = logPath;
        if (readBrokerReceipts is not null)
        {
            using var receiptDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiptDeadline.CancelAfter(receiptTimeout ?? TimeSpan.FromSeconds(30));
            var receipts = await readBrokerReceipts(receiptDeadline.Token);
            receiptDeadline.Token.ThrowIfCancellationRequested();
            receiptPath = Path.Combine(directory, $"{mode}-{round}.broker.jsonl");
            // 快照本身已有行分隔符，按收据边界规范化，不能再额外生成空 JSON 行。
            await File.WriteAllLinesAsync(receiptPath, receipts.Select(line => line.TrimEnd('\r', '\n')), receiptDeadline.Token);
        }
        var records = 0;
        var measuredRecords = 0;
        long measuredJsonPayloadBytes = 0;
        var projections = 0;
        var requestIds = new HashSet<string>(StringComparer.Ordinal);
        var expectedErrors = 0;
        var receiptPaths = localFile
            ? Directory.EnumerateFiles(Path.Combine(directory, "local"), "*.txt", SearchOption.AllDirectories)
                .Where(file => LocalLogFileLayout.IsOwnedFileName(Path.GetFileName(file))).Order().ToArray()
            : new[] { receiptPath };
        if (localFile && receiptPaths.Length == 0) throw new InvalidOperationException("Local 容量档没有实际日志文件。");
        foreach (var line in externalReconciliation ? Array.Empty<string>() : receiptPaths.SelectMany(File.ReadLines))
        {
            using var json = JsonDocument.Parse(line);
            if (!json.RootElement.TryGetProperty("@mt", out var template) || template.GetString() != "HttpOperationCompleted") continue;
            records++;
            var requestId = json.RootElement.GetProperty("RequestId").GetString();
            if (string.IsNullOrWhiteSpace(requestId) || !requestIds.Add(requestId))
                throw new InvalidOperationException("HTTP 日志 RequestId 缺失或重复。");
            var status = json.RootElement.GetProperty("http.status_code").GetInt32();
            if (status == 500) expectedErrors++;
            else if (status != 200) throw new InvalidOperationException("HTTP 日志状态码异常。");
            if (localFile)
            {
                var parts = requestId.Split(':');
                if (parts.Length != 3 || parts[0] != "logging-probe"
                    || parts[1] is not ("warmup" or "measured")
                    || !int.TryParse(parts[2], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var sequence)
                    || sequence < 0 || sequence >= (parts[1] == "warmup" ? Warmup : requests)
                    || status != (sequence % 10 == 0 ? 500 : 200))
                    throw new InvalidOperationException("Local 日志阶段、请求序号或状态不匹配。");
                if (parts[1] == "measured")
                {
                    measuredRecords++;
                    // 只统计实测 HTTP JSON 内容，不把预热、系统事件或行尾算作吞吐。
                    measuredJsonPayloadBytes += Encoding.UTF8.GetByteCount(line);
                }
            }
            if (mode == "Summary" && (json.RootElement.TryGetProperty("RequestPayload", out _)
                || json.RootElement.TryGetProperty("ResponsePayload", out _)))
                throw new InvalidOperationException("Summary 不得携带任一 Payload。");
            if (json.RootElement.TryGetProperty("RequestPayload", out var request) && request.ValueKind == JsonValueKind.String
                && json.RootElement.TryGetProperty("ResponsePayload", out var response) && response.ValueKind == JsonValueKind.String)
            {
                using var requestJson = JsonDocument.Parse(request.GetString()!);
                using var responseJson = JsonDocument.Parse(response.GetString()!);
                if (requestJson.RootElement.GetProperty("pageSize").GetInt32() != 20
                    || responseJson.RootElement.GetProperty("totalCount").GetInt64() != 100)
                    throw new InvalidOperationException("安全投影内容对账失败。");
                projections++;
            }
        }
        var expected = externalReconciliation || mode == "Disabled" ? 0 : requests + Warmup;
        if (localFile && measuredRecords != (mode == "Disabled" ? 0 : requests))
            throw new InvalidOperationException("Local 实测 HTTP 日志数量不完整。");
        if (expectedErrors != expected / 10) throw new InvalidOperationException("预期 500 日志数量对账失败。");
        var statistics = LoggingRequestEvidence.Calculate(latencies, unexpected, expected, records, preDisposalDroppedMessages,
            mode == "Projected" ? expected : 0, projections);
        using var completedProcess = Process.GetCurrentProcess();
        return new { mode, round, deliveryMode = deliveryMode ?? (kafkaConfiguration is null ? "Local" : "ApplicationKafka"),
            statistics, unexpected, observedLogEvents = records, projectedEvents = projections,
            receiptVerificationDeferred = externalReconciliation, endOfSendWorkingSetBytes,
            preDisposalDroppedMessages, uniqueRequestIds = requestIds.Count, expectedErrorEvents = expectedErrors, elapsedMilliseconds, observedRequestsPerSecond = requests * 1000 / elapsedMilliseconds,
            sharedProcessCpuMilliseconds = cpuMilliseconds, sharedProcessAllocatedBytes = allocated,
            consoleJsonBytes = new FileInfo(logPath).Length, latencyMilliseconds = latencies,
            receiptJsonBytes = receiptPaths.Sum(file => new FileInfo(file).Length),
            measuredLocalHttpEvents = localFile ? measuredRecords : (int?)null,
            measuredLocalHttpJsonPayloadBytes = localFile ? measuredJsonPayloadBytes : (long?)null,
            localFileEnabled = localFile, observedServerActiveRequestsPeak = localFile || externalReconciliation ? maximumActiveRequests : (int?)null,
            processLifetimePeakWorkingSetBytes = completedProcess.PeakWorkingSet64,
            receiptFiles = localFile ? receiptPaths.Select(file => new { name = Path.GetFileName(file),
                bytes = new FileInfo(file).Length,
                sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))) }).ToArray() : null,
            plannedCompletionStatistics = requestsPerSecond > 0
                ? LoggingRequestEvidence.Calculate(latencies.Zip(dispatchDelays, (http, delay) => http + delay).ToArray(), 0, 0, 0, 0, 0, 0) : null,
            dispatchDelayStatistics = requestsPerSecond > 0 ? LoggingRequestEvidence.Calculate(dispatchDelays, 0, 0, 0, 0, 0, 0) : null,
            dispatchDelayMilliseconds = requestsPerSecond > 0 ? dispatchDelays : null };
    }
}
