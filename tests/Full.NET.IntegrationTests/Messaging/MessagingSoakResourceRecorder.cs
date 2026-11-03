using System.Diagnostics;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>仅采样本用例明确持有的容器；记录原始 Docker 数值，不能以资源限额冒充峰值实测。</summary>
internal sealed class MessagingSoakResourceRecorder : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _recording;
    private int _disposed;

    internal MessagingSoakResourceRecorder(IReadOnlyList<string> ids, string path, Func<CancellationToken, Task>? sampleBacklog = null)
        => _recording = RecordAsync(ids, path, sampleBacklog);
    internal Task Completion => _recording;

    private async Task RecordAsync(IReadOnlyList<string> ids, string path, Func<CancellationToken, Task>? sampleBacklog)
    {
        Assert.AreEqual(3, ids.Count, "资源采样只接受本轮独享数据库、Broker 和 Connect。");
        while (!_stop.IsCancellationRequested)
        {
            var start = new ProcessStartInfo("docker")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var value in new[] { "stats", "--no-stream", "--format", "{{json .}}" }.Concat(ids)) start.ArgumentList.Add(value);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动本轮资源采样。");
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(deadline.Token);
                var samples = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                await File.AppendAllTextAsync(path, JsonSerializer.Serialize(new
                {
                    ObservedAtUtc = DateTimeOffset.UtcNow, process.ExitCode,
                    Samples = samples,
                }) + Environment.NewLine);
                await error;
                Assert.AreEqual(0, process.ExitCode, "资源采样失败，不能交付未测峰值的长稳结论。");
                Assert.AreEqual(ids.Count, samples.Length, "必须取得每个独享资源的实际样本。");
                if (sampleBacklog is not null) await sampleBacklog(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                await Task.WhenAll(output, error);
                if (!_stop.IsCancellationRequested) throw;
                break;
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), _stop.Token); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { await _stop.CancelAsync(); await _recording; }
        finally { _stop.Dispose(); }
    }
}
