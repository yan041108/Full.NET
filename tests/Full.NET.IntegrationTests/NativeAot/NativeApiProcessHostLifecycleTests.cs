using System.Diagnostics;
using System.Reflection;

namespace Full.NET.IntegrationTests.NativeAot;

[TestClass]
public sealed class NativeApiProcessHostLifecycleTests
{
    [TestMethod]
    public async Task Exited_api_process_stop_waits_for_final_log_output()
    {
        await using var harness = await LogDrainHarness.CreateAsync();
        var stopping = harness.Host.StopGracefullyAsync();
        try
        {
            Assert.IsFalse(stopping.IsCompleted, "进程退出不代表输出泵已经排空。");
            harness.Reader.Release.TrySetResult();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            // 停止进程后日志句柄仍由 Host 持有，读取必须允许已有写入句柄继续存在。
            await using var log = new FileStream(harness.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(log);
            StringAssert.Contains(await reader.ReadToEndAsync(), "final-api-log");
        }
        finally
        {
            harness.Reader.Release.TrySetResult();
        }
    }

    [TestMethod]
    public async Task Exited_api_process_log_wait_honors_cancellation_without_losing_cleanup()
    {
        await using var harness = await LogDrainHarness.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
                harness.Host.StopGracefullyAsync(cancellation.Token));
            Assert.IsFalse(harness.LogCancellation.IsCancellationRequested);
        }
        finally
        {
            harness.Reader.Release.TrySetResult();
        }

        await harness.Host.DisposeAsync();
        StringAssert.Contains(await File.ReadAllTextAsync(harness.Path), "final-api-log");
    }

    [TestMethod]
    public async Task Exited_api_process_disposal_preserves_buffered_log_tail()
    {
        await using var harness = await LogDrainHarness.CreateAsync();
        var disposing = harness.Host.DisposeAsync().AsTask();
        try
        {
            Assert.IsFalse(harness.LogCancellation.IsCancellationRequested,
                "正常退出后必须等 EOF，不能通过取消丢弃仍在缓冲的日志。");
            harness.Reader.Release.TrySetResult();
            await disposing.WaitAsync(TimeSpan.FromSeconds(5));
            StringAssert.Contains(await File.ReadAllTextAsync(harness.Path), "final-api-log");
        }
        finally
        {
            harness.Reader.Release.TrySetResult();
            await disposing.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class GatedLogReader : TextReader
    {
        private int _reads;
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _reads) != 1)
            {
                return null;
            }

            Reading.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return "final-api-log";
        }
    }

    private sealed class LogDrainHarness : IAsyncDisposable
    {
        private readonly Process _process;
        public NativeApiProcessHost Host { get; }
        public GatedLogReader Reader { get; }
        public CancellationTokenSource LogCancellation { get; }
        public string Path { get; }

        private LogDrainHarness(NativeApiProcessHost host, Process process,
            GatedLogReader reader, CancellationTokenSource cancellation, string path)
        {
            Host = host;
            _process = process;
            Reader = reader;
            LogCancellation = cancellation;
            Path = path;
        }

        public static async Task<LogDrainHarness> CreateAsync()
        {
            var process = Process.Start(new ProcessStartInfo("dotnet", "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fullnet-api-log-drain-{Guid.NewGuid():N}.log");
            var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true,
            };
            var cancellation = new CancellationTokenSource();
            var gate = new SemaphoreSlim(1, 1);
            var reader = new GatedLogReader();
            // 用真实输出泵固定“进程已退出、日志尾部尚未读完”的窗口，不启动数据库或完整 Native 应用。
            var pump = (Task)typeof(NativeApiProcessHost)
                .GetMethod("PumpStreamAsync", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [reader, writer, gate, cancellation.Token])!;
            await reader.Reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var host = (NativeApiProcessHost)typeof(NativeApiProcessHost)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single()
                .Invoke([process, path, writer, gate, cancellation, pump, Task.CompletedTask, new Uri("http://127.0.0.1:1")]);
            return new LogDrainHarness(host, process, reader, cancellation, path);
        }

        public async ValueTask DisposeAsync()
        {
            Reader.Release.TrySetResult();
            await Host.DisposeAsync();
            LogCancellation.Dispose();
            _process.Dispose();
            File.Delete(Path);
        }
    }
}
