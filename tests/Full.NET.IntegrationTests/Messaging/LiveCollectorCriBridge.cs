using System.Text;
using System.Text.Json.Nodes;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>本地运行时模拟桥接：只转发完整 Console 行，有界处理，不等待全部请求结束。</summary>
internal sealed class LiveCollectorCriBridge : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping;
    private readonly Task _pump;
    private volatile bool _completed;
    private LiveCollectorCriBridge(string source, string directory, CancellationToken token)
    {
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(token);
        _pump = PumpAsync(source, directory);
    }

    public static async Task<LiveCollectorCriBridge> StartAsync(string source, string directory, CancellationToken token)
    {
        await File.WriteAllTextAsync(source, "", token);
        return new(source, directory, token);
    }

    internal static string PodFile(string pod) => $"{pod}_default_api-{new string('a', 64)}.log";

    private async Task PumpAsync(string source, string directory)
    {
        var token = _stopping.Token;
        await using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192, FileOptions.Asynchronous);
        using var input = new StreamReader(file, new UTF8Encoding(false, true));
        await using var trusted = new StreamWriter(Path.Combine(directory, PodFile("fullnet-collector")), true, new UTF8Encoding(false)) { AutoFlush = true };
        await using var mirror = new StreamWriter(Path.Combine(directory, PodFile("fullnet-mirror")), true, new UTF8Encoding(false)) { AutoFlush = true };
        var buffer = new char[8192];
        var pending = new StringBuilder();
        var records = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(buffer.AsMemory(), token);
            if (read == 0)
            {
                if (_completed) { Assert.AreEqual(0, pending.Length, "不能把未完成 JSON 行丢弃后报告通过。"); return; }
                await Task.Delay(10, token);
                continue;
            }
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] != '\n')
                {
                    pending.Append(buffer[i]);
                    Assert.IsTrue(pending.Length <= 32768, "实时桥接单行超出有界预算。");
                    continue;
                }
                var line = pending.ToString().TrimEnd('\r');
                pending.Clear();
                Assert.IsTrue(++records <= 10000);
                var json = JsonNode.Parse(line)!.AsObject();
                var prefix = DateTimeOffset.UtcNow.ToString("O") + " stdout F ";
                await trusted.WriteLineAsync((prefix + line).AsMemory(), token);
                json["LogEventId"] = Guid.CreateVersion7().ToString();
                json["kubernetes"] = JsonNode.Parse("{\"labels\":{\"fullnet.io/log-ingress\":\"collector\"}}");
                if (json.ContainsKey("RequestId")) json["RequestId"] = "mirror-" + json["RequestId"]!.GetValue<string>();
                await mirror.WriteLineAsync((prefix + json.ToJsonString()).AsMemory(), token);
            }
        }
    }

    public async Task CompleteAsync()
    {
        _completed = true;
        await _pump.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _stopping.CancelAsync();
            try { await _pump.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        }
        finally { _stopping.Dispose(); }
    }
}
