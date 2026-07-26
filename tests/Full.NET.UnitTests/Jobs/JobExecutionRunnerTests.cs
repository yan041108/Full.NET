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
    public async Task ProcessPendingAsync_WhenHandlerOutlivesRenewalInterval_RenewsOwnedLease()
    {
        var definitionId = Guid.CreateVersion7();
        var executionId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var initialNow = new DateTimeOffset(
            2026,
            7,
            27,
            0,
            0,
            0,
            TimeSpan.Zero);
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
                JobKey = RenewalAwaitingJobHandler.Key,
                IsEnabled = true,
            });
        var commandExecutor = new RenewalRecordingCommandExecutor();
        var runner = new JobExecutionRunner(
            queryExecutor,
            commandExecutor,
            new JobHandlerRegistry(
                [new RenewalAwaitingJobHandler(commandExecutor.RenewalObserved)]),
            new SteppingClock(initialNow),
            new FixedIdGenerator(leaseId),
            Options.Create(
                new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                }),
            Options.Create(
                new JobsWorkerOptions
                {
                    LeaseSeconds = 2,
                    LeaseRenewalSeconds = 1,
                }),
            NullLogger<JobExecutionRunner>.Instance);

        var processed = await runner
            .ProcessPendingAsync(1, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(1, processed);
        Assert.AreEqual(leaseId, commandExecutor.RenewedLeaseId);
        Assert.IsTrue(
            commandExecutor.RenewedUntilUtc
                > initialNow.AddSeconds(2));
        CollectionAssert.Contains(
            commandExecutor.Statements,
            JobSql.MarkExecutionSucceeded);
    }

    [TestMethod]
    public async Task ProcessPendingAsync_WhenLeaseOwnershipIsLost_CancelsHandlerAndFails()
    {
        var definitionId = Guid.CreateVersion7();
        var executionId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
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
                JobKey = CancellationAwaitingJobHandler.Key,
                IsEnabled = true,
            });
        var commandExecutor = new LostLeaseCommandExecutor();
        var handler = new CancellationAwaitingJobHandler();
        var runner = new JobExecutionRunner(
            queryExecutor,
            commandExecutor,
            new JobHandlerRegistry([handler]),
            new FixedClock(
                new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero)),
            new FixedIdGenerator(leaseId),
            Options.Create(
                new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                }),
            Options.Create(
                new JobsWorkerOptions
                {
                    LeaseSeconds = 2,
                    LeaseRenewalSeconds = 1,
                }),
            NullLogger<JobExecutionRunner>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner
                .ProcessPendingAsync(1, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(3)));

        StringAssert.Contains(exception.Message, "is no longer owned");
        await handler.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(1));
        CollectionAssert.DoesNotContain(
            commandExecutor.Statements,
            JobSql.MarkExecutionSucceeded);
        CollectionAssert.DoesNotContain(
            commandExecutor.Statements,
            JobSql.MarkExecutionFailed);
    }

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
            Options.Create(new JobsWorkerOptions()),
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

    private sealed class RenewalAwaitingJobHandler(Task renewalObserved) : IJobHandler
    {
        public const string Key = "jobs.test-active-renewal";

        public string JobKey => Key;

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await renewalObserved
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed class CancellationAwaitingJobHandler : IJobHandler
    {
        private readonly TaskCompletionSource _cancellationObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public const string Key = "jobs.test-lost-lease";

        public string JobKey => Key;

        public Task CancellationObserved => _cancellationObserved.Task;

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _cancellationObserved.TrySetResult();
                throw;
            }
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class SteppingClock(DateTimeOffset initialUtcNow) : IClock
    {
        private long _readCount;

        public DateTimeOffset UtcNow =>
            initialUtcNow.AddSeconds(Interlocked.Increment(ref _readCount) - 1);
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

    private sealed class RenewalRecordingCommandExecutor : ICommandExecutor
    {
        private readonly TaskCompletionSource _renewalObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<SqlStatement> Statements { get; } = [];

        public Task RenewalObserved => _renewalObserved.Task;

        public Guid? RenewedLeaseId { get; private set; }

        public DateTimeOffset? RenewedUntilUtc { get; private set; }

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            if (statement == JobSql.RenewExecutionLease)
            {
                RenewedLeaseId = ReadParameter<Guid>(parameters, "LeaseId");
                RenewedUntilUtc = ReadParameter<DateTimeOffset>(
                    parameters,
                    "LeaseExpiresAtUtc");
                _renewalObserved.TrySetResult();
            }

            return Task.FromResult(1);
        }

        private static T ReadParameter<T>(object? parameters, string name) =>
            (T?)parameters?
                .GetType()
                .GetProperty(name)?
                .GetValue(parameters)
            ?? throw new InvalidOperationException(
                $"Command parameter '{name}' was not provided.");
    }

    private sealed class LostLeaseCommandExecutor : ICommandExecutor
    {
        public List<SqlStatement> Statements { get; } = [];

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            return Task.FromResult(
                statement == JobSql.RenewExecutionLease ? 0 : 1);
        }
    }
}
