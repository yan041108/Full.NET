using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Persistence;
using Full.NET.Modules.Jobs.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class JobExecutionHostedProcessorTests
{
    [TestMethod]
    public async Task ProcessOnceAsync_UsesConfiguredBatchAndPollingValues()
    {
        var currentTenant = new CurrentTenantAccessor();
        var queryExecutor = new BatchSizeRecordingQueryExecutor(currentTenant);
        var clock = new FixedClock(
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero));
        var databaseOptions = Options.Create(
            new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            });
        var runner = new JobExecutionRunner(
            queryExecutor,
            new UnexpectedCommandExecutor(),
            new UnexpectedTransaction(),
            new JobHandlerKindRegistry([]),
            clock,
            new FixedIdGenerator(Guid.CreateVersion7()),
            databaseOptions,
            Options.Create(new JobsWorkerOptions()),
            NullLogger<JobExecutionRunner>.Instance);
        var options = new JobsWorkerOptions
        {
            BatchSize = 7,
            PollMilliseconds = 250,
        };
        var services = new ServiceCollection();
        var currentTenantResolutionCount = 0;
        services.AddScoped(_ => currentTenantResolutionCount++ == 0
            ? currentTenant
            : new CurrentTenantAccessor());
        services.AddScoped<ICurrentTenantContextWriter>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICommandExecutor>(_ => new HeartbeatCommandExecutor());
        services.AddScoped(_ =>
            new JobsBacklogReader(queryExecutor, databaseOptions));
        services.AddScoped(_ =>
            new JobScheduleDispatcher(
                queryExecutor,
                new UnexpectedCommandExecutor(),
                new PassThroughTransaction(),
                databaseOptions,
                clock,
                new FixedIdGenerator(Guid.CreateVersion7())));
        services.AddScoped(_ => runner);
        services.AddScoped(provider => new JobWorkerHeartbeatService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            databaseOptions,
            Options.Create(options)));
        await using var provider = services.BuildServiceProvider();
        var processor = new JobExecutionHostedProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            Options.Create(options),
            NullLogger<JobExecutionHostedProcessor>.Instance);

        await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.AreEqual(options.BatchSize, queryExecutor.ObservedBatchSize);
        Assert.IsTrue(
            queryExecutor.ObservedHostContext,
            "Jobs HostedProcessor 必须在领取 Host 任务前建立 Host Context。");
        Assert.IsTrue(
            queryExecutor.ObservedBacklogHostContext,
            "Jobs HostedProcessor 必须在 Host Context 内读取积压快照。");
        Assert.AreEqual(1, queryExecutor.BacklogReadCount);
        Assert.AreEqual(1, queryExecutor.ScheduleReadCount);
        CollectionAssert.AreEqual(
            new[] { "backlog", "schedules", "executions" },
            queryExecutor.QueryOrder.ToArray());
        Assert.IsFalse(
            currentTenant.IsAvailable,
            "Jobs HostedProcessor 每轮结束后必须清理 Host Context。");
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(options.PollMilliseconds),
            processor.PollingDelay);
    }

    [TestMethod]
    public void GetDelayAfterBatch_OnlyWaitsWhenBatchIsNotFull()
    {
        var queryExecutor = new BatchSizeRecordingQueryExecutor();
        var clock = new FixedClock(
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero));
        var databaseOptions = Options.Create(
            new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            });
        var runner = new JobExecutionRunner(
            queryExecutor,
            new UnexpectedCommandExecutor(),
            new UnexpectedTransaction(),
            new JobHandlerKindRegistry([]),
            clock,
            new FixedIdGenerator(Guid.CreateVersion7()),
            databaseOptions,
            Options.Create(new JobsWorkerOptions()),
            NullLogger<JobExecutionRunner>.Instance);
        var services = new ServiceCollection();
        services.AddScoped(_ => runner);
        using var provider = services.BuildServiceProvider();
        var options = new JobsWorkerOptions
        {
            BatchSize = 7,
            PollMilliseconds = 250,
        };
        var processor = new JobExecutionHostedProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            Options.Create(options),
            NullLogger<JobExecutionHostedProcessor>.Instance);

        Assert.AreEqual(TimeSpan.Zero, processor.GetDelayAfterBatch(7));
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(250),
            processor.GetDelayAfterBatch(6));
    }

    [TestMethod]
    public async Task ProcessOnceAsync_BacklogFailureIsThrottledAndDoesNotBlockAcquisition()
    {
        var currentTenant = new CurrentTenantAccessor();
        var queryExecutor = new BatchSizeRecordingQueryExecutor(
            currentTenant,
            new InvalidOperationException("Backlog sampling failed."));
        var clock = new FixedClock(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));
        var databaseOptions = Options.Create(
            new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            });
        var options = new JobsWorkerOptions
        {
            BacklogSampleSeconds = 60,
        };
        var runner = new JobExecutionRunner(
            queryExecutor,
            new UnexpectedCommandExecutor(),
            new UnexpectedTransaction(),
            new JobHandlerKindRegistry([]),
            clock,
            new FixedIdGenerator(Guid.CreateVersion7()),
            databaseOptions,
            Options.Create(options),
            NullLogger<JobExecutionRunner>.Instance);
        var services = new ServiceCollection();
        var currentTenantResolutionCount = 0;
        services.AddScoped(_ => currentTenantResolutionCount++ == 0
            ? currentTenant
            : new CurrentTenantAccessor());
        services.AddScoped<ICurrentTenantContextWriter>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICommandExecutor>(_ => new HeartbeatCommandExecutor());
        services.AddScoped(_ =>
            new JobsBacklogReader(queryExecutor, databaseOptions));
        services.AddScoped(_ =>
            new JobScheduleDispatcher(
                queryExecutor,
                new UnexpectedCommandExecutor(),
                new PassThroughTransaction(),
                databaseOptions,
                clock,
                new FixedIdGenerator(Guid.CreateVersion7())));
        services.AddScoped(_ => runner);
        services.AddScoped(provider => new JobWorkerHeartbeatService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            databaseOptions,
            Options.Create(options)));
        await using var provider = services.BuildServiceProvider();
        var processor = new JobExecutionHostedProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            Options.Create(options),
            NullLogger<JobExecutionHostedProcessor>.Instance);

        await processor.ProcessOnceAsync(CancellationToken.None);
        await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.AreEqual(1, queryExecutor.BacklogReadCount);
        Assert.AreEqual(2, queryExecutor.AcquisitionCount);
        Assert.IsFalse(currentTenant.IsAvailable);
    }

    private sealed class BatchSizeRecordingQueryExecutor(
        CurrentTenantAccessor? currentTenant = null,
        Exception? backlogFailure = null) : IQueryExecutor
    {
        public int? ObservedBatchSize { get; private set; }

        public bool ObservedHostContext { get; private set; }

        public bool ObservedBacklogHostContext { get; private set; }

        public int BacklogReadCount { get; private set; }

        public int AcquisitionCount { get; private set; }

        public int ScheduleReadCount { get; private set; }

        public List<string> QueryOrder { get; } = [];

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement != JobSql.ReadBacklogSqlServer)
            {
                throw new InvalidOperationException(
                    $"Unexpected single-row statement '{statement.Name}'.");
            }

            BacklogReadCount++;
            QueryOrder.Add("backlog");
            ObservedBacklogHostContext = currentTenant?.IsHost ?? false;
            return backlogFailure is not null
                ? Task.FromException<T?>(backlogFailure)
                : Task.FromResult<T?>(default);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement == JobSql.SelectDueSchedulesSqlServer)
            {
                ScheduleReadCount++;
                QueryOrder.Add("schedules");
                return Task.FromResult<IReadOnlyList<T>>([]);
            }

            if (statement != JobSql.AcquireExecutionsSqlServer)
            {
                throw new InvalidOperationException(
                    $"Unexpected list statement '{statement.Name}'.");
            }

            ObservedBatchSize = parameters is IReadOnlyDictionary<string, object?> dictionary
                && dictionary.TryGetValue(nameof(JobsWorkerOptions.BatchSize), out var value)
                && value is int batchSize
                    ? batchSize
                    : throw new InvalidOperationException(
                        "Jobs acquisition did not expose BatchSize.");
            ObservedHostContext = currentTenant?.IsHost ?? false;
            AcquisitionCount++;
            QueryOrder.Add("executions");
            return Task.FromResult<IReadOnlyList<T>>([]);
        }
    }

    private sealed class UnexpectedCommandExecutor : ICommandExecutor
    {
        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                $"Unexpected command statement '{statement.Name}'.");
    }

    private sealed class HeartbeatCommandExecutor : ICommandExecutor
    {
        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Assert.IsTrue(
                statement == JobSql.UpsertWorkerHeartbeat,
                $"Unexpected heartbeat statement '{statement.Name}'.");
            return Task.FromResult(1);
        }
    }

    private sealed class UnexpectedTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "SQL Server acquisition must not start a command transaction.");
    }

    private sealed class PassThroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FixedIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }
}
