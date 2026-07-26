using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Persistence;
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
        var queryExecutor = new BatchSizeRecordingQueryExecutor();
        var runner = new JobExecutionRunner(
            queryExecutor,
            new UnexpectedCommandExecutor(),
            new JobHandlerRegistry([]),
            new FixedClock(
                new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero)),
            new FixedIdGenerator(Guid.CreateVersion7()),
            Options.Create(
                new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                }),
            Options.Create(new JobsWorkerOptions()),
            NullLogger<JobExecutionRunner>.Instance);
        var services = new ServiceCollection();
        services.AddScoped(_ => runner);
        await using var provider = services.BuildServiceProvider();
        var options = new JobsWorkerOptions
        {
            BatchSize = 7,
            PollMilliseconds = 250,
        };
        var processor = new JobExecutionHostedProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<JobExecutionHostedProcessor>.Instance);

        await processor.ProcessOnceAsync(CancellationToken.None);

        Assert.AreEqual(options.BatchSize, queryExecutor.ObservedBatchSize);
        Assert.AreEqual(
            TimeSpan.FromMilliseconds(options.PollMilliseconds),
            processor.PollingDelay);
    }

    private sealed class BatchSizeRecordingQueryExecutor : IQueryExecutor
    {
        public int? ObservedBatchSize { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                $"Unexpected single-row statement '{statement.Name}'.");

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement != JobSql.AcquireExecutionsSqlServer)
            {
                throw new InvalidOperationException(
                    $"Unexpected list statement '{statement.Name}'.");
            }

            var property = parameters?
                .GetType()
                .GetProperty(nameof(JobsWorkerOptions.BatchSize));
            ObservedBatchSize = property?.GetValue(parameters) as int?
                ?? throw new InvalidOperationException(
                    "Jobs acquisition did not expose BatchSize.");
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

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FixedIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }
}
