using System.Diagnostics;
using System.Text;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>只管理本测试创建的正式消费子进程；排空后的终止不代表优雅停机验收。</summary>
internal sealed class KafkaRequestConsumerProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task _stdout;
    private readonly Task _stderr;
    private readonly string _directory;
    private readonly StringBuilder _output = new();
    private readonly StringBuilder _errors = new();
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private KafkaRequestConsumerProcess(Process process, string directory)
    {
        _process = process;
        _directory = directory;
        _stdout = PumpAsync(process.StandardOutput, _output, true);
        _stderr = PumpAsync(process.StandardError, _errors, false);
    }

    public int Id => _process.Id;
    public bool IsAlive => !_process.HasExited;
    public bool Stopped { get; private set; }

    public static async Task<KafkaRequestConsumerProcess> StartAsync(KafkaLogTlsFixture kafka,
        KafkaRequestElasticsearchFixture es, string general, string priority, string group, string dlq,
        string directory, CancellationToken token)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Full.NET.Host.LogConsumer.dll"));
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["EXPERIMENTAL"] = "true", ["BATCH_MAX_RECORDS"] = "8",
            ["BOOTSTRAP_SERVERS"] = kafka.BootstrapServers, ["TOPICS"] = general + "," + priority,
            ["GROUP_ID"] = group, ["DLQ_TOPIC"] = dlq, ["ROUTES"] = "1:30", ["MAX_EVENT_BYTES"] = "16384",
            ["ES_URL"] = es.Url.ToString(), ["ES_API_KEY"] = es.ApiKey!, ["ES_CA_PATH"] = es.CaPath!,
            ["ES_REVOCATION_MODE"] = "NoCheck", ["SECURITY_PROTOCOL"] = "Ssl", ["SSL_CA_LOCATION"] = kafka.CaPath,
            ["HEALTH_PORT"] = "0",
        }) start.Environment["FULLNET_LOG_CONSUMER_" + key] = value;
        start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        var fixture = new KafkaRequestConsumerProcess(Process.Start(start) ?? throw new InvalidOperationException("消费进程未启动。"), directory);
        try
        {
            await fixture._started.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            Assert.IsTrue(fixture.IsAlive && fixture.Id != Environment.ProcessId);
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private async Task PumpAsync(StreamReader reader, StringBuilder destination, bool detectStart)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            // 只保留有界诊断文本，不输出进程环境及 API Key。
            if (destination.Length + line.Length < 131072) destination.AppendLine(line);
            if (detectStart && line == "Log consumer started; Offset commits require ES item or DLQ broker confirmation.")
                _started.TrySetResult();
        }
        if (detectStart) _started.TrySetException(new InvalidOperationException("消费进程在启动确认前退出。"));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(10));
            await File.WriteAllTextAsync(Path.Combine(_directory, "consumer.stdout.log"), _output.ToString());
            await File.WriteAllTextAsync(Path.Combine(_directory, "consumer.stderr.log"), _errors.ToString());
            Stopped = _process.HasExited;
        }
        finally { _process.Dispose(); }
    }
}
