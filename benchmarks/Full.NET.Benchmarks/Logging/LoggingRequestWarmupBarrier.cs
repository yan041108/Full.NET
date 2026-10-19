namespace Full.NET.Benchmarks.Logging;

/// <summary>独立请求探针等待父进程确认预热记录的 Broker/ES 位点，避免将依赖冷启动混入持续窗口。</summary>
public static class LoggingRequestWarmupBarrier
{
    public const string FileName = "warmup.confirmed";

    /// <summary>只在受控测试目录等待，最多一分钟；取消不会遗留后台轮询。</summary>
    public static async Task WaitAsync(string directory, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(1));
        while (!File.Exists(Path.Combine(directory, FileName)))
            await Task.Delay(25, deadline.Token);
        deadline.Token.ThrowIfCancellationRequested();
    }

    /// <summary>写完后原子发布就绪标记；父进程必须先核对全部预热记录的真实位点。</summary>
    public static async Task ConfirmAsync(string directory, CancellationToken token)
    {
        var temporary = Path.Combine(directory, FileName + ".pending");
        await File.WriteAllTextAsync(temporary, "broker-and-es-confirmed", token);
        File.Move(temporary, Path.Combine(directory, FileName), overwrite: false);
    }
}
