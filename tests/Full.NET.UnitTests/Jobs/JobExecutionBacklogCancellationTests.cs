using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class JobExecutionBacklogCancellationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Cancelled_backlog_query_preserves_exception_without_logging_or_continuing(
        DatabaseProvider databaseProvider,
        bool driverWrapsCancellation)
    {
        using var stopping = new CancellationTokenSource();
        Exception expected = driverWrapsCancellation
            ? new InvalidOperationException("Driver wrapped command cancellation.")
            : new OperationCanceledException(stopping.Token);
        var tenant = new CurrentTenantAccessor();
        var query = new CancellingQueryExecutor(tenant, stopping, expected, databaseProvider);
        var logger = new JobProcessorRecordingLogger();
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenantContextWriter>(_ => tenant);
        services.AddScoped(_ => new JobsBacklogReader(
            query,
            Options.Create(new DatabaseOptions { Provider = databaseProvider })));
        await using var provider = services.BuildServiceProvider();
        using var processor = new JobExecutionHostedProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(),
            Options.Create(new JobsWorkerOptions()),
            logger);

        Exception? actual = null;
        try
        {
            await processor.ProcessOnceAsync(stopping.Token);
        }
        catch (Exception exception)
        {
            actual = exception;
        }

        Assert.AreEqual(0, logger.Entries.Count,
            "停机取消不应报告积压采样故障。");
        Assert.AreSame(expected, actual,
            "应传播原始异常；没有注册后续服务，继续心跳将暴露为另一异常。");
        Assert.AreEqual(1, query.ReadCount);
        Assert.IsTrue(stopping.IsCancellationRequested);
        Assert.IsFalse(tenant.IsAvailable,
            "取消路径同样必须清理本轮 Host Context。");
    }

    private sealed class CancellingQueryExecutor(
        CurrentTenantAccessor tenant,
        CancellationTokenSource stopping,
        Exception failure,
        DatabaseProvider databaseProvider) : IQueryExecutor
    {
        public int ReadCount { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(databaseProvider == DatabaseProvider.SqlServer
                ? JobSql.ReadBacklogSqlServer : JobSql.ReadBacklogMySql, statement);
            Assert.AreEqual(stopping.Token, cancellationToken);
            Assert.IsTrue(tenant.IsHost);
            Assert.IsFalse(cancellationToken.IsCancellationRequested);
            ReadCount++;
            // 取消发生在查询期间，模拟 SQL Server 用非取消异常报告命令终止的时序。
            stopping.Cancel();
            return Task.FromException<T?>(failure);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Cancellation must stop the iteration.");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
    }
}

internal sealed class JobProcessorRecordingLogger : ILogger<JobExecutionHostedProcessor>
{
    public List<(LogLevel Level, EventId EventId, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, eventId, exception));
}
