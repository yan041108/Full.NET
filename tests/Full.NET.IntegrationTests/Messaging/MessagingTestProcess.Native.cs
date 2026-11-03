using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Messaging;

internal sealed partial class MessagingTestProcess
{
    private string? _nativeContainerName;
    private string? _nativeOwner;
    private int _nativeStopping;
    internal string? NativeContainerName => _nativeContainerName;

    internal sealed record ResourceSample(int Pid, long WorkingSetBytes, double CpuSeconds, string Kind, string? Container);

    /// <summary>原生采样读取容器内实际ELF的PID 1；不以Docker CLI的CPU或内存冒充被测进程。</summary>
    internal async Task<ResourceSample?> ReadResourcesAsync(CancellationToken token)
    {
        if (_nativeContainerName is null)
        {
            var sample = ReadResources();
            return sample is { } value ? new(value.Pid, value.WorkingSetBytes, value.CpuSeconds, "windows_process_handle", null) : null;
        }
        if (_process.HasExited || Volatile.Read(ref _nativeStopping) != 0) return null;
        var owner = await DockerAsync(token, "inspect", "--format", "{{ index .Config.Labels \"fullnet.messaging.native.test.owner\" }}", _nativeContainerName);
        if (_process.HasExited || Volatile.Read(ref _nativeStopping) != 0) return null;
        Assert.AreEqual(0, owner.Exit); Assert.AreEqual(_nativeOwner, owner.Output.Trim());
        var result = await DockerAsync(token, "exec", _nativeContainerName, "/bin/sh", "-ec", "cat /proc/1/stat; getconf CLK_TCK; grep '^VmRSS:' /proc/1/status");
        if (_process.HasExited || Volatile.Read(ref _nativeStopping) != 0) return null;
        Assert.AreEqual(0, result.Exit, "原生进程采样失败，不能填零或使用CLI资源。");
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.AreEqual(3, lines.Length);
        var fields = lines[0][(lines[0].LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var ticks = double.Parse(fields[11], culture) + double.Parse(fields[12], culture);
        var frequency = double.Parse(lines[1], culture);
        var rss = long.Parse(lines[2].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[1], culture) * 1024;
        Assert.IsGreaterThan(0d, frequency); Assert.IsGreaterThan(0L, rss);
        return new(1, rss, ticks / frequency, "linux_pid1_rss", _nativeContainerName);
    }

    internal static bool NativeEnabled
    {
        get
        {
            var api = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_NATIVE_API_ROOT");
            var worker = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_NATIVE_WORKER_ROOT");
            if (string.IsNullOrWhiteSpace(api) != string.IsNullOrWhiteSpace(worker))
                throw new InvalidOperationException("Native 故障矩阵必须同时提供本轮 API 和 Worker 发布目录，不能混入 JIT。");
            return !string.IsNullOrWhiteSpace(api);
        }
    }

    private static async Task<MessagingTestProcess> StartNativeAsync(string role, string instance,
        DatabaseOptions database, IReadOnlyDictionary<string, string?> settings)
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable($"FULLNET_MESSAGING_NATIVE_{role.ToUpperInvariant()}_ROOT")!);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "manifest.json")));
        var document = manifest.RootElement;
        Assert.AreEqual(2, document.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(role.ToLowerInvariant(), document.GetProperty("host").GetString());
        foreach (var guard in new[] { "managedInputsVerified", "nativeInputsUnchanged", "managedArtifactsUnchanged", "headUnchanged", "toolingInputsUnchanged" })
            Assert.IsTrue(document.GetProperty(guard).GetBoolean(), guard);
        var executable = Path.Combine(root, "publish", $"Full.NET.Host.{role}");
        var bytes = await File.ReadAllBytesAsync(executable);
        Assert.IsTrue(bytes.Length > 8_000_000 && bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 0x7f, 0x45, 0x4c, 0x46 }));
        Assert.AreEqual(document.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var evidence = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR")!;
        Directory.CreateDirectory(evidence);
        using var port = new TcpListener(IPAddress.Loopback, 0); port.Start();
        var hostPort = ((IPEndPoint)port.LocalEndpoint).Port; port.Stop();
        var address = new Uri($"http://127.0.0.1:{hostPort}/");
        var owner = Guid.NewGuid().ToString("N");
        var name = "fullnet-messaging-native-test-" + owner;
        var start = new ProcessStartInfo("docker")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "run", "--rm", "--name", name, "--label", "fullnet.messaging.native.test.owner=" + owner,
            "--cpus", "1", "--memory", "1g", "--memory-swap", "1g", "--add-host", "host.docker.internal:host-gateway",
            "--publish", $"127.0.0.1:{hostPort}:8080", "--mount", $"type=bind,source={Path.Combine(root, "publish")},target=/app,readonly", "--workdir", "/app" })
            start.ArgumentList.Add(argument);
        var values = new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Development", ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Database:Provider"] = database.Provider.ToString(), ["Database:ConnectionString"] = database.ConnectionString,
            ["Database:MySqlGuidStorageMode"] = "Binary16", ["Database:CommandTimeoutSeconds"] = "30",
            ["Identity:AllowDevelopmentEphemeralSigningKey"] = "true", ["Identity:RequireSecureCookies"] = "false",
            ["Identity:EnableRemoteSuperAdministratorManagement"] = "true", ["Identity:AllowedOrigins:0"] = "http://localhost",
            ["Identity:SessionLoginPolicy"] = "AllowMultiple", ["Tenancy:HostDomains:0"] = "localhost",
            ["Identity:LoginRateLimitPermitLimitPerMinute"] = "1000", ["Realtime:Enabled"] = "false",
            ["OutboxWorker:PollMilliseconds"] = "100", ["OutboxWorker:MaximumIdlePollMilliseconds"] = "100",
        };
        foreach (var entry in settings) values[entry.Key] = entry.Value;
        values["ASPNETCORE_URLS"] = "http://0.0.0.0:8080"; values["ASPNETCORE_CONTENTROOT"] = "/app";
        values["Files:Local:RootPath"] = "/tmp/files"; values["DataProtection:KeyRingPath"] = "/tmp/keys";
        foreach (var entry in values)
        {
            if (entry.Value is null) continue;
            var key = entry.Key.Replace(":", "__", StringComparison.Ordinal);
            // 代理只为本轮显式开放宿主网关，端口与资源身份不变；秘密只经环境传递，不进入命令参数。
            start.Environment[key] = entry.Value.Replace("127.0.0.1", "host.docker.internal", StringComparison.Ordinal);
            start.ArgumentList.Add("--env"); start.ArgumentList.Add(key);
        }
        start.ArgumentList.Add(document.GetProperty("imageId").GetString()!);
        start.ArgumentList.Add($"/app/Full.NET.Host.{role}");
        var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Native Host 容器。");
        var host = new MessagingTestProcess(process, address, Path.Combine(evidence, $"{role}-{instance}-{process.Id}.log"))
        { _nativeContainerName = name, _nativeOwner = owner };
        await File.AppendAllTextAsync(Path.Combine(evidence, "native-hosts.ndjson"), JsonSerializer.Serialize(new
        { Provider = database.Provider.ToString(), Role = role, Instance = instance, Container = name, CliPid = process.Id,
            Root = root, Sha256 = document.GetProperty("sha256").GetString(), ImageId = document.GetProperty("imageId").GetString() }) + Environment.NewLine);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            while (!timeout.IsCancellationRequested)
            {
                if (process.HasExited) throw new InvalidOperationException($"Native {role} 退出 {process.ExitCode}，日志 {host.LogPath}");
                try
                {
                    using var response = await client.GetAsync(new Uri(address, "health/live"), timeout.Token);
                    if (response.IsSuccessStatusCode) return host;
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { }
                await Task.Delay(200, timeout.Token);
            }
            throw new TimeoutException($"Native {role} 未就绪，日志 {host.LogPath}");
        }
        catch { await host.DisposeAsync(); throw; }
    }

    internal async Task StopGracefullyAsync()
    {
        Assert.IsNotNull(_nativeContainerName, "优雅停止验收必须到达真实 Native 容器。");
        await StopNativeContainerAsync(graceful: true);
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
        await Task.WhenAll(_stdout, _stderr);
        Assert.AreEqual(0, _process.ExitCode, "原生 Worker 必须在 SIGTERM 后正常退出：" + LogPath);
        await File.AppendAllTextAsync(Path.Combine(Path.GetDirectoryName(LogPath)!, "native-stops.ndjson"),
            JsonSerializer.Serialize(new { Container = _nativeContainerName, Graceful = true, ExitCode = _process.ExitCode }) + Environment.NewLine);
    }

    private async Task StopNativeContainerAsync(bool graceful)
    {
        var inspected = await DockerAsync("inspect", "--format", "{{ index .Config.Labels \"fullnet.messaging.native.test.owner\" }}", _nativeContainerName!);
        if (inspected.Exit != 0)
        {
            if (_process.HasExited) return;
            throw new InvalidOperationException("运行中的 Native 容器归属无法确认：" + _nativeContainerName);
        }
        Assert.AreEqual(_nativeOwner, inspected.Output.Trim(), "不能终止归属变化的容器。");
        Interlocked.Exchange(ref _nativeStopping, 1);
        var stopped = graceful ? await DockerAsync("stop", "--time", "30", _nativeContainerName!)
            : await DockerAsync("kill", _nativeContainerName!);
        Assert.AreEqual(0, stopped.Exit, "无法终止本轮原生容器：" + stopped.Output);
    }

    private static Task<(int Exit, string Output)> DockerAsync(params string[] arguments) => DockerAsync(CancellationToken.None, arguments);

    private static async Task<(int Exit, string Output)> DockerAsync(CancellationToken token, params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(45), token); }
        catch
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await Task.WhenAll(stdout, stderr); throw;
        }
        return (process.ExitCode, await stdout + await stderr);
    }
}
