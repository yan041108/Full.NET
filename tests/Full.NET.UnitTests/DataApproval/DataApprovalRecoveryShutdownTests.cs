using System.Collections.Concurrent;
using System.Data.Common;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Full.NET.UnitTests.DataApproval;

/// <summary>验证真实恢复循环在驱动返回非OCE停机异常时退出，同时保留运行期错误恢复。</summary>
[TestClass]
public sealed class DataApprovalRecoveryShutdownTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Host_cancellation_stops_non_oce_query_without_error_or_retry(bool application)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = new ControlledQuery(async token =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw new DriverFailure(); }
        });
        var log = new LogSink();
        using var services = Services(query);
        using var worker = Worker(application, services, log);
        await worker.StartAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, query.Calls);
        Assert.AreEqual(0, log.Entries.Count(item => item.Level >= LogLevel.Error));
        var stopped = log.Entries.Single(item => item.Level == LogLevel.Debug);
        StringAssert.Contains(stopped.Message, typeof(DriverFailure).FullName!);
        Assert.IsNull(stopped.Exception, "正常停机仅记录固定原因和类型，不记录驱动异常对象或正文。");
        Assert.IsTrue(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Running_query_failure_remains_error_and_retries(bool application)
    {
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = 0;
        var query = new ControlledQuery(async token =>
        {
            if (Interlocked.Increment(ref invocation) == 1) throw new DriverFailure();
            second.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        var log = new LogSink();
        using var services = Services(query);
        using var worker = Worker(application, services, log);
        await worker.StartAsync(CancellationToken.None);
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, query.Calls);
        var failure = log.Entries.Single(item => item.Level == LogLevel.Error);
        Assert.IsInstanceOfType<DriverFailure>(failure.Exception);
        Assert.IsTrue(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    private static ServiceProvider Services(ControlledQuery query)
    {
        var services = new ServiceCollection();
        var clock = new FixedClock();
        var database = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
        // 查询边界在物化任何业务行之前失败，其余业务端口不会被调用。
        services.AddScoped(_ => new DataApprovalRequestRecoveryBatchProcessor(query, clock, database,
            Options.Create(new DataApprovalRequestRecoveryWorkerOptions()), null!, null!, null!,
            NullLogger<DataApprovalRequestRecoveryBatchProcessor>.Instance));
        services.AddScoped(_ => new DataApprovalRequestApplicationRecoveryBatchProcessor(query, clock, database,
            Options.Create(new DataApprovalRequestApplicationRecoveryWorkerOptions()), null!, null!, null!,
            NullLogger<DataApprovalRequestApplicationRecoveryBatchProcessor>.Instance));
        return services.BuildServiceProvider();
    }

    private static BackgroundService Worker(bool application, ServiceProvider services, LogSink log) => application
        ? new DataApprovalRequestApplicationRecoveryHostedProcessor(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DataApprovalRequestApplicationRecoveryWorkerOptions { PollMilliseconds = 500 }),
            new CaptureLogger<DataApprovalRequestApplicationRecoveryHostedProcessor>(log))
        : new DataApprovalRequestRecoveryHostedProcessor(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DataApprovalRequestRecoveryWorkerOptions { PollMilliseconds = 500 }),
            new CaptureLogger<DataApprovalRequestRecoveryHostedProcessor>(log));

    private sealed class DriverFailure : DbException { }
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

    private sealed class ControlledQuery(Func<CancellationToken, Task> action) : IQueryExecutor
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public async Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            await action(cancellationToken);
            return [];
        }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record Entry(LogLevel Level, string Message, Exception? Exception);
    private sealed class LogSink { public ConcurrentQueue<Entry> Entries { get; } = new(); }
    private sealed class CaptureLogger<T>(LogSink sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => sink.Entries.Enqueue(new(logLevel, formatter(state, exception), exception));
    }
}
