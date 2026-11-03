using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>测试进程独占真实 JIT 或已校验 Native Host 的生命周期，不扫描或接管其他任务。</summary>
internal sealed partial class MessagingTestProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _log;
    private readonly SemaphoreSlim _logGate = new(1, 1);
    private readonly Task _stdout;
    private readonly Task _stderr;
    internal Uri Address { get; }
    internal string LogPath { get; }
    internal int Id => _process.Id;

    /// <summary>从本对象持有的实际进程句柄采样，退出后不按可复用 PID 误读别的任务。</summary>
    internal (int Pid, long WorkingSetBytes, double CpuSeconds)? ReadResources()
    {
        // Docker CLI 不是容器内 ELF，不能将客户端资源当成原生 Worker 资源。
        if (_nativeContainerName is not null) return null;
        try
        {
            if (_process.HasExited) return null;
            _process.Refresh();
            return (_process.Id, _process.WorkingSet64, _process.TotalProcessorTime.TotalSeconds);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    private MessagingTestProcess(Process process, Uri address, string logPath)
    {
        _process = process; Address = address; LogPath = logPath;
        _log = new StreamWriter(new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        _stdout = PumpAsync(process.StandardOutput); _stderr = PumpAsync(process.StandardError);
    }

    internal static async Task<MessagingTestProcess> StartAsync(string role, string instance, DatabaseOptions database,
        IReadOnlyDictionary<string, string?> settings)
    {
        Assert.IsTrue(role is "Api" or "Worker");
        if (NativeEnabled) return await StartNativeAsync(role, instance, database, settings);
        var output = Environment.GetEnvironmentVariable($"FULLNET_MESSAGING_{role.ToUpperInvariant()}_OUTPUT");
        var directory = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_BUILD_ROOT");
        if (output is null && directory is null) throw new InvalidOperationException("必须由验收入口提供本轮宿主构建目录。");
        var dll = output is not null ? Path.Combine(output, $"Full.NET.Host.{role}.dll")
            : Path.Combine(directory!, "bin", $"Full.NET.Host.{role}", "release", $"Full.NET.Host.{role}.dll");
        Assert.IsTrue(File.Exists(dll), "缺少本轮真实 Host 构建：" + dll);
        var evidence = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR")
            ?? throw new InvalidOperationException("缺少本任务日志目录。");
        Directory.CreateDirectory(evidence);
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start();
        var address = new Uri($"http://127.0.0.1:{((IPEndPoint)port.LocalEndpoint).Port}/"); port.Stop();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(dll)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add(dll);
        var values = new Dictionary<string, string?>
        {
            // Development 不装配仅 Testing 的 Native Replay 测试订阅；场景使用真实 Organization 试点。
            ["DOTNET_ENVIRONMENT"] = "Development", ["ASPNETCORE_ENVIRONMENT"] = "Development",
            // 从已构建目录读取配置，避免长跑期间工作区并行修改配置导致宿主输入漂移。
            ["ASPNETCORE_URLS"] = address.ToString(), ["ASPNETCORE_CONTENTROOT"] = Path.GetDirectoryName(dll),
            ["Database:Provider"] = database.Provider.ToString(), ["Database:ConnectionString"] = database.ConnectionString,
            ["Database:MySqlGuidStorageMode"] = "Binary16", ["Database:CommandTimeoutSeconds"] = "30",
            ["Identity:AllowDevelopmentEphemeralSigningKey"] = "true", ["Identity:RequireSecureCookies"] = "false",
            ["Identity:EnableRemoteSuperAdministratorManagement"] = "true", ["Identity:AllowedOrigins:0"] = "http://localhost",
            ["Identity:SessionLoginPolicy"] = "AllowMultiple", ["Tenancy:HostDomains:0"] = "localhost",
            ["Identity:LoginRateLimitPermitLimitPerMinute"] = "1000", ["Realtime:Enabled"] = "false",
            ["Files:Local:RootPath"] = Path.Combine(evidence, "files"), ["DataProtection:KeyRingPath"] = Path.Combine(evidence, "keys"),
            ["OutboxWorker:PollMilliseconds"] = "100", ["OutboxWorker:MaximumIdlePollMilliseconds"] = "100",
        };
        foreach (var item in settings) values[item.Key] = item.Value;
        foreach (var item in values) if (item.Value is not null) start.Environment[item.Key.Replace(":", "__", StringComparison.Ordinal)] = item.Value;
        var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动消息验收 Host。");
        var host = new MessagingTestProcess(process, address, Path.Combine(evidence, $"{role}-{instance}-{process.Id}.log"));
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            while (!timeout.IsCancellationRequested)
            {
                if (process.HasExited) throw new InvalidOperationException($"{role} 提前退出 {process.ExitCode}，日志 {host.LogPath}");
                try
                {
                    using var response = await client.GetAsync(new Uri(address, "health/live"), timeout.Token);
                    if (response.IsSuccessStatusCode) return host;
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { }
                await Task.Delay(200, timeout.Token);
            }
            throw new TimeoutException($"{role} 未就绪，日志 {host.LogPath}");
        }
        catch { await host.DisposeAsync(); throw; }
    }

    internal HttpClient Client()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = Address, Timeout = TimeSpan.FromMinutes(8) };
        client.DefaultRequestHeaders.Host = "localhost";
        return client;
    }

    internal async Task CrashAsync()
    {
        if (_nativeContainerName is not null)
        {
            await StopNativeContainerAsync(graceful: false);
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
        }
        else if (!_process.HasExited) { _process.Kill(entireProcessTree: true); await _process.WaitForExitAsync(); }
        await Task.WhenAll(_stdout, _stderr);
    }

    public async ValueTask DisposeAsync()
    {
        await CrashAsync(); await _log.DisposeAsync(); _logGate.Dispose(); _process.Dispose();
    }

    private async Task PumpAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            await _logGate.WaitAsync();
            try { await _log.WriteLineAsync(line); }
            finally { _logGate.Release(); }
        }
    }
}
