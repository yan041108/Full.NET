using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>请求测量与测试桥接隔离；仅启动和清理本任务子进程，保存有界诊断。</summary>
internal sealed class LoggingRequestCaseProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _directory;
    private readonly Task _stdout;
    private readonly Task _stderr;
    private readonly StringBuilder _output = new();
    private readonly StringBuilder _errors = new();
    private readonly object _stageLock = new();
    private DateTimeOffset? _begin;
    private Task<JsonElement>? _completion;
    private LoggingRequestCaseProcess(Process process, string directory)
    {
        _process = process; _directory = directory;
        _stdout = PumpAsync(process.StandardOutput, _output, false);
        _stderr = PumpAsync(process.StandardError, _errors, true);
    }

    public int Id => _process.Id;
    public DateTimeOffset? MeasurementBegin { get { lock (_stageLock) return _begin; } }
    public bool Stopped { get; private set; }

    public static LoggingRequestCaseProcess Start(string root, string directory, string route,
        KafkaLogTlsFixture kafka, string general, string priority)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        // 与当前测试产物使用同一配置和目标框架，不能静默运行旧 Release 产物。
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var benchmark = Path.Combine(root, "benchmarks/Full.NET.Benchmarks/bin", output.Parent!.Name, output.Name, "Full.NET.Benchmarks.dll");
        foreach (var argument in new[] { benchmark,
            "logging-route-case", route, "--output", directory }) start.ArgumentList.Add(argument);
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["BOOTSTRAP_SERVERS"] = kafka.BootstrapServers, ["GENERAL_TOPIC"] = general,
            ["PRIORITY_TOPIC"] = priority, ["CA_PATH"] = kafka.CaPath,
        }) start.Environment["FULLNET_LOG_PROBE_" + key] = value;
        var process = Process.Start(start) ?? throw new InvalidOperationException("请求子进程未创建。");
        return new(process, directory);
    }

    private async Task PumpAsync(StreamReader reader, StringBuilder destination, bool detectStage)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (destination.Length + line.Length < 131072) destination.AppendLine(line);
            const string begin = "FULLNET_MEASUREMENT_BEGIN ";
            if (detectStage && line.StartsWith(begin, StringComparison.Ordinal))
            {
                var date = DateTimeOffset.Parse(line[begin.Length..], CultureInfo.InvariantCulture, DateTimeStyles.None);
                lock (_stageLock) _begin = date;
            }
        }
    }

    public Task<JsonElement> CompleteAsync(CancellationToken token) => _completion ??= ReadResultAsync(token);

    private async Task<JsonElement> ReadResultAsync(CancellationToken token)
    {
        await _process.WaitForExitAsync(token);
        await Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.AreEqual(0, _process.ExitCode, "请求子进程失败，查看有界 request.stderr.log。");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, "request.result.json"), token));
        Assert.IsTrue(json.RootElement.GetProperty("measurementOnly").GetBoolean());
        Assert.AreEqual(Id, json.RootElement.GetProperty("requestProcessId").GetInt32());
        Assert.AreNotEqual(Environment.ProcessId, Id);
        return json.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(10));
            await File.WriteAllTextAsync(Path.Combine(_directory, "request.stdout.log"), _output.ToString());
            await File.WriteAllTextAsync(Path.Combine(_directory, "request.stderr.log"), _errors.ToString());
            if (_completion is not null)
            {
                try { await _completion.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (Exception) when (_process.ExitCode != 0) { }
            }
            Stopped = true;
        }
        finally { _process.Dispose(); }
    }
}
