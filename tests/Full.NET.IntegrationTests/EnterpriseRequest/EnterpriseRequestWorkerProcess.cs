using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.EnterpriseRequest;

/// <summary>只持有本测试创建的 Worker 进程句柄；重启使用同一已构建宿主与持久数据库。</summary>
internal sealed class EnterpriseRequestWorkerProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task output;
    private readonly Task errors;
    private readonly string logPrefix;
    internal int Id => process.Id;

    private EnterpriseRequestWorkerProcess(Process process, string log)
    {
        this.process = process;
        logPrefix = log;
        output = PumpAsync(process.StandardOutput, log + ".stdout.log");
        errors = PumpAsync(process.StandardError, log + ".stderr.log");
    }

    internal static async Task<EnterpriseRequestWorkerProcess> StartAsync(FullNetApiFactory factory, string evidence, string mode, string gateFile, int sequence, string? webhookReceiverUrl=null, bool webhookFailAfterEnqueueOnce=false, Guid? dataTaskId=null,
        IReadOnlyDictionary<string, string?>? additionalSettings = null)
    {
        var root = FullNetApiFactory.FindRepositoryRoot();
        var directory = Environment.GetEnvironmentVariable("FULLNET_ENTERPRISE_WORKER_FIXTURE_OUTPUT")
            ?? Path.Combine(root, "tests/Tools/Full.NET.EnterpriseRequest.WorkerFixture/bin/Release/net10.0");
        var dll = Path.Combine(directory, "Full.NET.EnterpriseRequest.WorkerFixture.dll");
        Assert.IsTrue(File.Exists(dll), "请先构建本场景 WorkerFixture：" + dll);
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var address = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/"); listener.Stop();
        var redis = await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(dll);
        var settings = new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Testing", ["ASPNETCORE_ENVIRONMENT"] = "Testing", ["DOTNET_PROCESSOR_COUNT"] = "2",
            ["ASPNETCORE_URLS"] = address.ToString(), ["ASPNETCORE_CONTENTROOT"] = directory,
            ["Database:Provider"] = factory.Provider.ToString(), ["Database:ConnectionString"] = factory.ConnectionString,
            ["Database:MySqlGuidStorageMode"] = "Binary16", ["Database:CommandTimeoutSeconds"] = "30",
            ["Cache:RedisConnectionString"] = redis, ["Realtime:RedisBackplaneConnectionString"] = redis,
            ["ConnectionStrings:redis"] = redis, ["Realtime:Enabled"] = "false", ["Realtime:AllowSharedRedisInDevelopment"] = "true",
            ["Files:Local:RootPath"] = Path.Combine(evidence, "files"), ["DataProtection:KeyRingPath"] = Path.Combine(evidence, "keys"),
            ["Identity:AllowDevelopmentEphemeralSigningKey"] = "true", ["Identity:AllowedOrigins:0"] = "http://localhost",
            ["OutboxWorker:PollMilliseconds"] = "100", ["OutboxWorker:MaximumIdlePollMilliseconds"] = "1000",
            ["OutboxWorker:LeaseSeconds"] = "10", ["OutboxWorker:LeaseRenewalSeconds"] = "3", ["OutboxWorker:MaxConcurrency"] = "1",
            ["OutboxWorker:MaxAttempts"] = webhookFailAfterEnqueueOnce?"3":"1",
            ["RecoveryFixture:Pause"] = mode, ["RecoveryFixture:GateFile"] = gateFile,
            ["RecoveryFixture:DataTaskId"] = dataTaskId?.ToString("D"),
            ["FullNet:Reporting:Export:PollSeconds"] = mode.StartsWith("reporting-", StringComparison.Ordinal) ? "5" : null,
            ["FullNet:Reporting:Export:LeaseSeconds"] = mode.StartsWith("reporting-", StringComparison.Ordinal) ? "30" : null,
            ["FullNet:ImportExport:PollSeconds"] = mode.StartsWith("import-", StringComparison.Ordinal) ? "5" : null,
            ["FullNet:ImportExport:LeaseSeconds"] = mode.StartsWith("import-", StringComparison.Ordinal) ? "30" : null,
            ["FullNet:ImportExport:ExecutionEnabled"] = mode.StartsWith("import-", StringComparison.Ordinal) ? "true" : null,
            ["RecoveryFixture:WebhookReceiverUrl"] = webhookReceiverUrl, ["Webhooks:Delivery:PollMilliseconds"]="100",
            ["RecoveryFixture:WebhookFailAfterEnqueueOnce"]=webhookFailAfterEnqueueOnce.ToString(),
            ["Notifications:Worker:PollMilliseconds"] = "100", ["Workflow:RecoveryWorker:PollMilliseconds"] = "100"
        };
        if (mode is "office-conversion" or "office-recovery")
        {
            settings["Document:OfficePreviewConversion:Enabled"] = "true";
            settings["Document:OfficePreviewConversion:ProviderKey"] = "external_http";
            settings["Document:OfficePreviewConversion:ExternalHttp:BaseUrl"] = "https://example.test/convert";
            settings["Document:OfficePreviewConversion:MaxAttempts"] = "1";
            settings["Document:OfficePreviewConversion:TimeoutSeconds"] = "15";
            settings["Document:OfficePreviewConversion:PollSeconds"] = "5";
            settings["Document:OfficePreviewConversion:TempRootPath"] = Path.Combine(evidence, "office-work");
        }
        if (additionalSettings is not null)
            foreach (var setting in additionalSettings) settings[setting.Key] = setting.Value;
        foreach (var setting in settings) if (setting.Value is not null) start.Environment[setting.Key.Replace(":", "__", StringComparison.Ordinal)] = setting.Value;
        var child = Process.Start(start) ?? throw new InvalidOperationException("无法启动业务恢复 Worker。");
        var host = new EnterpriseRequestWorkerProcess(child, Path.Combine(evidence, $"worker-{sequence}-{child.Id}"));
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            while (true)
            {
                if (child.HasExited) throw new InvalidOperationException($"Worker 提前退出 {child.ExitCode}，证据目录：{evidence}");
                try { using var ready = await client.GetAsync(new Uri(address, "health/live"), timeout.Token); if (ready.IsSuccessStatusCode) return host; }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { }
                await Task.Delay(100, timeout.Token);
            }
        }
        catch { await host.DisposeAsync(); throw; }
    }

    internal async Task CrashAsync()
    {
        if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        await Task.WhenAll(output, errors);
    }

    internal async Task<bool> HasAdmissionFailureAsync(string statementPrefix, CancellationToken cancellationToken)
    {
        foreach (var suffix in new[] { ".stdout.log", ".stderr.log" })
        {
            var path = logPrefix + suffix;
            if (!File.Exists(path)) continue;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
                if (line.Contains(statementPrefix, StringComparison.Ordinal) &&
                    (line.Contains("admission_rejected", StringComparison.Ordinal) || line.Contains("admission_timeout", StringComparison.Ordinal)))
                    return true;
        }
        return false;
    }

    public async ValueTask DisposeAsync() { await CrashAsync(); process.Dispose(); }

    private static async Task PumpAsync(StreamReader source, string file)
    {
        await using var target = new StreamWriter(new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        while (await source.ReadLineAsync() is { } line) await target.WriteLineAsync(line);
    }
}
