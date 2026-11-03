using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Channels;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Full.NET.UnitTests.Auditing;

/// <summary>固定入队端已关闭而宿主尚未取消的真实协调器时序，保护正常关闭与运行期故障边界。</summary>
[TestClass]
public sealed class AuditMicroBatchShutdownTests
{
    [TestMethod]
    public async Task Normal_channel_completion_exits_without_error_before_host_cancellation()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var log = new LogSink();
        using var coordinator = new AuditMicroBatchCoordinator(
            services.GetRequiredService<IServiceScopeFactory>(), new OptionsMonitor(), log);
        // 直接固定StopAsync关闭通道与BackgroundService取消之间的时序，不依赖线程调度碰巧复现。
        Channel(coordinator).Writer.TryComplete();
        await coordinator.StartAsync(CancellationToken.None);
        await coordinator.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(coordinator.ExecuteTask.IsCompletedSuccessfully);
        Assert.AreEqual(0, log.Levels.Count(level => level >= LogLevel.Error));
        Assert.AreEqual(0, coordinator.QueueBytesInUse);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task Running_options_failure_remains_error_and_recovers_before_normal_completion()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var log = new LogSink();
        var options = new OptionsMonitor(failLoopRead: true);
        using var coordinator = new AuditMicroBatchCoordinator(
            services.GetRequiredService<IServiceScopeFactory>(), options, log);
        await coordinator.StartAsync(CancellationToken.None);
        await options.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Channel(coordinator).Writer.TryComplete();
        await coordinator.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(coordinator.ExecuteTask.IsCompletedSuccessfully);
        Assert.AreEqual(1, log.Levels.Count(level => level == LogLevel.Error));
        Assert.AreEqual(0, coordinator.QueueBytesInUse);
        await coordinator.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task Faulted_channel_remains_error_until_host_stop()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var log = new LogSink();
        using var coordinator = new AuditMicroBatchCoordinator(
            services.GetRequiredService<IServiceScopeFactory>(), new OptionsMonitor(), log);
        var channel = Channel(coordinator);
        channel.Writer.TryComplete(new InvalidOperationException("channel failure"));
        await coordinator.StartAsync(CancellationToken.None);
        await log.FirstError.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(channel.Reader.Completion.IsFaulted);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => channel.Reader.Completion);
        Assert.IsFalse(coordinator.ExecuteTask!.IsCompleted, "异常关闭必须保留运行期故障路径，不能按正常关闭退出。");
        await coordinator.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(coordinator.ExecuteTask.IsCompletedSuccessfully);
        Assert.IsTrue(log.Levels.Any(level => level == LogLevel.Error));
        Assert.AreEqual(0, coordinator.QueueBytesInUse);
    }

    private static Channel<AuditWriteEnvelope> Channel(AuditMicroBatchCoordinator coordinator) =>
        (Channel<AuditWriteEnvelope>)typeof(AuditMicroBatchCoordinator)
            .GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;

    private sealed class OptionsMonitor(bool failLoopRead = false) : IOptionsMonitor<AuditMicroBatchOptions>
    {
        private int _reads;
        public TaskCompletionSource Recovered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AuditMicroBatchOptions CurrentValue
        {
            get
            {
                var read = Interlocked.Increment(ref _reads);
                if (failLoopRead && read == 3) throw new InvalidOperationException("invalid options");
                if (failLoopRead && read > 3) Recovered.TrySetResult();
                return new AuditMicroBatchOptions();
            }
        }
        public AuditMicroBatchOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuditMicroBatchOptions, string?> listener) => null;
    }

    private sealed class LogSink : ILogger<AuditMicroBatchCoordinator>
    {
        public ConcurrentQueue<LogLevel> Levels { get; } = new();
        public TaskCompletionSource FirstError { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Levels.Enqueue(logLevel);
            if (logLevel == LogLevel.Error) FirstError.TrySetResult();
        }
    }
}
