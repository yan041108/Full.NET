using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class JobExecutionRunnerTests
{
    [TestMethod]
    public async Task ProcessPendingAsync_WhenHostCancels_PropagatesWithoutMarkingFailure()
    {
        var definitionId = Guid.CreateVersion7();
        var executionId = Guid.CreateVersion7();
        var queryExecutor = new StubQueryExecutor(
            new JobExecutionRecord
            {
                Id = executionId,
                JobDefinitionId = definitionId,
                Status = JobExecutionStatuses.Running,
            },
            new JobDefinitionRecord
            {
                Id = definitionId,
                JobKey = CancellingJobHandler.Key,
                IsEnabled = true,
            });
        var commandExecutor = new RecordingCommandExecutor();
        using var cancellationTokenSource = new CancellationTokenSource();
        var runner = new JobExecutionRunner(
            queryExecutor,
            commandExecutor,
            new JobHandlerRegistry(
                [new CancellingJobHandler(cancellationTokenSource)]),
            new FixedClock(
                new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero)),
            new FixedIdGenerator(Guid.CreateVersion7()),
            Options.Create(
                new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                }),
            NullLogger<JobExecutionRunner>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.ProcessPendingAsync(
                1,
                cancellationTokenSource.Token));

        CollectionAssert.DoesNotContain(
            commandExecutor.Statements,
            JobSql.MarkExecutionFailed);
    }

    private sealed class CancellingJobHandler(
        CancellationTokenSource cancellationTokenSource) : IJobHandler
    {
        public const string Key = "jobs.test-cancellation";

        public string JobKey => Key;

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            cancellationTokenSource.Cancel();
            return Task.FromCanceled(cancellationToken);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FixedIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }

    private sealed class StubQueryExecutor(
        JobExecutionRecord execution,
        JobDefinitionRecord definition) : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(JobDefinitionRecord)
                && statement == JobSql.FindDefinitionById)
            {
                return Task.FromResult((T?)(object)definition);
            }

            throw new InvalidOperationException(
                $"Unexpected single-row statement '{statement.Name}'.");
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(JobExecutionRecord)
                && statement == JobSql.AcquireExecutionsSqlServer)
            {
                return Task.FromResult(
                    (IReadOnlyList<T>)(object)new[] { execution });
            }

            throw new InvalidOperationException(
                $"Unexpected list statement '{statement.Name}'.");
        }
    }

    private sealed class RecordingCommandExecutor : ICommandExecutor
    {
        public List<SqlStatement> Statements { get; } = [];

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            return Task.FromResult(1);
        }
    }
}
