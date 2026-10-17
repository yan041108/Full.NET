using System.Diagnostics;
using System.Reflection;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>用受控 JIT 进程和输出窗口验证原生验收夹具，不依赖数据库或 Native 发布。</summary>
[TestClass]
public sealed class NativeProcessFailureLifecycleTests
{
    [TestMethod]
    public async Task Api_stdout_and_stderr_serialize_writes_to_the_shared_log()
    {
        using var stream = new GatedWriteStream();
        await using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
        using var cancellation = new CancellationTokenSource();
        using var gate = new SemaphoreSlim(1, 1);
        var stdout = PumpApi(new StringReader("stdout-tail"), writer, gate, cancellation.Token);
        await stream.Writing.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // 第一行停在真实 StreamWriter 的异步写入中，第二条管道此时不能并发操作它。
        var stderr = PumpApi(new StringReader("stderr-tail"), writer, gate, cancellation.Token);
        stream.Release.TrySetResult();
        await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5));
        await writer.FlushAsync();
        var content = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        StringAssert.Contains(content, "stdout-tail");
        StringAssert.Contains(content, "stderr-tail");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Output_failure_disposal_releases_writer_and_process_handle(bool worker)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fullnet-process-failure-{Guid.NewGuid():N}.log");
        using var process = StartDotnet("--version");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await using var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        using var gate = new SemaphoreSlim(1, 1);
        using var cancellation = new CancellationTokenSource();
        var failure = new IOException("controlled-output-failure");
        var host = CreateHost(worker, process, path, writer, gate, cancellation, Task.FromException(failure));
        try
        {
            var thrown = await Assert.ThrowsExactlyAsync<IOException>(() => host.DisposeAsync().AsTask());
            Assert.AreSame(failure, thrown, "输出故障必须继续向调用方传播。");
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.ThrowsExactly<InvalidOperationException>(() => _ = process.Id,
                "排空失败也必须释放本任务进程句柄。");
        }
        finally
        {
            await writer.DisposeAsync();
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Startup_exit_keeps_primary_failure_and_complete_process_log(bool worker)
    {
        var root = CreateRoot();
        try
        {
            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => StartHostAsync(worker, root));
            StringAssert.Contains(exception.Message, worker ? "Native Worker 在启动前退出" : "Native Host.Api 在启动完成前退出");
            var logPath = Directory.GetFiles(root, "*.log", SearchOption.AllDirectories).Single();
            var content = await File.ReadAllTextAsync(logPath);
            StringAssert.Contains(content, "Usage: dotnet");
            // 启动失败未交付 Host，调用方没有第二次 Dispose 的机会。
            using var exclusive = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Startup_cancellation_keeps_original_cancellation_and_closes_log(bool worker)
    {
        var root = CreateRoot();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            var exception = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => StartHostAsync(worker, root, cancellation.Token));
            Assert.AreEqual(cancellation.Token, exception.CancellationToken);
            var logPath = Directory.GetFiles(root, "*.log", SearchOption.AllDirectories).Single();
            using var exclusive = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Task PumpApi(TextReader reader, TextWriter writer, SemaphoreSlim gate, CancellationToken cancellation)
    {
        var method = typeof(NativeApiProcessHost).GetMethod("PumpStreamAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(null, method.GetParameters().Length == 3
            ? [reader, writer, cancellation] : [reader, writer, gate, cancellation])!;
    }

    private static IAsyncDisposable CreateHost(bool worker, Process process, string path, StreamWriter writer,
        SemaphoreSlim gate, CancellationTokenSource cancellation, Task stdout)
    {
        var type = worker ? typeof(NativeWorkerProcessHost) : typeof(NativeApiProcessHost);
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        object?[] arguments = constructor.GetParameters().Select(parameter => parameter.Name switch
        {
            "process" => (object)process,
            "logFilePath" => path,
            "logWriter" => writer,
            "logWriteGate" => gate,
            "logPumpCancellation" => cancellation,
            "stdoutPump" => stdout,
            "stderrPump" => Task.CompletedTask,
            "baseAddress" => new Uri("http://127.0.0.1:1"),
            _ => throw new InvalidOperationException($"未知夹具参数 {parameter.Name}"),
        }).ToArray();
        return (IAsyncDisposable)constructor.Invoke(arguments);
    }

    private static async Task StartHostAsync(bool worker, string root, CancellationToken cancellation = default)
    {
        // dotnet 不带参数会正常输出帮助后退出，稳定触发未提供 HTTP 服务的启动失败。
        var runtimeDirectory = new DirectoryInfo(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
        var executable = Path.Combine(runtimeDirectory.Parent!.Parent!.Parent!.FullName,
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        Assert.IsTrue(File.Exists(executable), "夹具必须使用当前运行时所属的 dotnet 主机。");
        if (worker)
        {
            await using var host = await NativeWorkerProcessHost.StartAsync(
                new NativeWorkerArtifact(root, root, executable), DatabaseProvider.MySql,
                "Server=127.0.0.1;Database=unused", TimeSpan.FromSeconds(10), cancellation);
        }
        else
        {
            await using var host = await NativeApiProcessHost.StartAsync(
                new NativeApiArtifact(root, root, executable, 0), DatabaseProvider.MySql,
                "Server=127.0.0.1;Database=unused", new Dictionary<string, string?>(), TimeSpan.FromSeconds(10), cancellation);
        }
    }

    private static Process StartDotnet(string arguments) => Process.Start(new ProcessStartInfo("dotnet", arguments)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    })!;

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"fullnet-process-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class GatedWriteStream : MemoryStream
    {
        private int _writes;
        public TaskCompletionSource Writing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _writes) == 1)
            {
                Writing.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            await base.WriteAsync(buffer, cancellationToken);
        }
    }
}
