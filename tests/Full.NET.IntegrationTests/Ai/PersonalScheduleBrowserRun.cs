using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>协调日程用例与真实 Playwright 进程，限制临时凭据和浏览器产物的生命周期。</summary>
internal sealed class PersonalScheduleBrowserRun : IAsyncDisposable
{
    private readonly DirectoryInfo? _temporary;
    private Process? _process;
    private Task<string>? _stdout;
    private Task<string>? _stderr;

    public PersonalScheduleBrowserRun(string externalBridgeVariable)
    {
        var bridge = Environment.GetEnvironmentVariable(externalBridgeVariable);
        if (string.IsNullOrWhiteSpace(bridge))
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            AdminOrigin = $"http://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}";
            _temporary = Directory.CreateTempSubdirectory("fullnet-k06-native-browser-");
            Bridge = Path.Combine(_temporary.FullName, "bridge");
        }
        else
        {
            AdminOrigin = "http://localhost:34492";
            ApiListenAddress = new Uri("http://0.0.0.0:64215/");
            Bridge = bridge;
        }

        if (Directory.Exists(Bridge)) throw new InvalidOperationException("不能采用旧日程页面桥接目录。");
        Directory.CreateDirectory(Bridge);
    }

    public string Bridge { get; }
    public string AdminOrigin { get; }
    public Uri? ApiListenAddress { get; }
    public string? OwnedDirectory => _temporary?.FullName;

    public void Start(Uri apiAddress, string configName, string readyVariable)
    {
        if (_temporary is null) return;
        var workspace = Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "tests", "e2e", "admin-real-stack");
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = workspace,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { Path.Combine(workspace, "node_modules", "@playwright", "test", "cli.js"), "test",
                     "--config", configName, "--project", "vue-admin", "--output", Path.Combine(_temporary.FullName, "browser") })
            start.ArgumentList.Add(argument);
        start.Environment["FULLNET_E2E_API_URL"] = apiAddress.AbsoluteUri;
        start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = AdminOrigin;
        start.Environment["FULLNET_E2E_SKIP_BOOTSTRAP"] = "1";
        start.Environment["FULLNET_E2E_SKIP_VIEWER_PROVISION"] = "1";
        start.Environment["FULLNET_E2E_SKIP_OIDC_FIXTURES"] = "1";
        start.Environment["FULLNET_E2E_REUSE_SERVER"] = "0";
        start.Environment["FULLNET_E2E_L3_JSON_REPORT"] = Path.Combine(_temporary.FullName, "browser.json");
        start.Environment[readyVariable] = Path.Combine(Bridge, "ready.json");
        _process = Process.Start(start) ?? throw new InvalidOperationException("无法启动日程 Playwright。");
        _stdout = _process.StandardOutput.ReadToEndAsync();
        _stderr = _process.StandardError.ReadToEndAsync();
    }

    public async Task WaitForMarkerAsync(string name, string flag, string failureMarker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var path = Path.Combine(Bridge, name);
        var failed = Path.Combine(Bridge, failureMarker);
        while (!File.Exists(path))
        {
            await FailIfUnavailableAsync(failed, timeout.Token);
            await Task.Delay(200, timeout.Token);
        }

        if (await ReadFlagAsync(path, flag, timeout.Token)) return;
        await FailIfUnavailableAsync(failed, timeout.Token);
        Assert.Fail($"页面结果 {name} 缺少成功标记。");
    }

    public async Task RequireSuccessAsync()
    {
        if (_process is null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await _process.WaitForExitAsync(timeout.Token);
        Assert.AreEqual(0, _process.ExitCode, await ReadProcessOutputAsync());
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(_temporary!.FullName, "browser.json"), timeout.Token));
        var stats = report.RootElement.GetProperty("stats");
        Assert.AreEqual(2, stats.GetProperty("expected").GetInt32(), "必须实际通过桌面和移动端各一个用例。");
        foreach (var key in new[] { "unexpected", "skipped", "flaky" })
            Assert.AreEqual(0, stats.GetProperty(key).GetInt32(), key);
    }

    private async Task FailIfUnavailableAsync(string failed, CancellationToken token)
    {
        // 页面可先写失败标记再退出；应保留本次进程输出，避免把依赖故障误报成结果文件超时。
        if (_process is { HasExited: true })
            Assert.Fail("日程浏览器进程提前退出。\n" + await ReadProcessOutputAsync());
        if (!File.Exists(failed) || await ReadFlagAsync(failed, "passed", token)) return;
        if (_process is not null)
        {
            using var outputTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            outputTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await _process.WaitForExitAsync(outputTimeout.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            if (_process.HasExited) Assert.Fail("日程浏览器用例失败。\n" + await ReadProcessOutputAsync());
        }
        Assert.Fail("日程浏览器记录了失败。");
    }

    private async Task<string> ReadProcessOutputAsync() =>
        await _stdout! + Environment.NewLine + await _stderr!;

    private static async Task<bool> ReadFlagAsync(string path, string flag, CancellationToken token)
    {
        using var result = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
        return result.RootElement.TryGetProperty(flag, out var value) && value.ValueKind is JsonValueKind.True;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_process is null) return;
            using (_process)
            {
                if (!_process.HasExited)
                {
                    try { _process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (_process.HasExited) { }
                }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _process.WaitForExitAsync(timeout.Token);
            }
        }
        finally
        {
            // 临时登录凭据与浏览器跟踪均可能包含会话信息。
            _temporary?.Delete(recursive: true);
        }
    }
}
