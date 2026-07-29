using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class JobExecutionRunnerTests
{
    [TestMethod]
    public async Task ProcessPendingAsync_WithBoundedConcurrency_IsolatesScopesAndSerializesSameJobKey()
    {
        var firstDefinitionId = Guid.CreateVersion7();
        var secondDefinitionId = Guid.CreateVersion7();
        var executions = new[]
        {
            CreateExecution(firstDefinitionId),
            CreateExecution(firstDefinitionId),
            CreateExecution(secondDefinitionId),
        };
        var definitions = new[]
        {
            new JobDefinitionRecord
            {
                Id = firstDefinitionId,
                JobKey = "jobs.test-parallel-a",
                IsEnabled = true,
            },
            new JobDefinitionRecord
            {
                Id = secondDefinitionId,
                JobKey = "jobs.test-parallel-b",
                IsEnabled = true,
            },
        };
        var coordinator = new ParallelExecutionCoordinator(2);
        var services = new ServiceCollection();
        services.AddSingleton(coordinator);
        services.AddSingleton<IClock>(
            new FixedClock(
                new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero)));
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ExecutionScopeIdentity>();
        services.AddScoped<ICommandExecutor, ScopedRecordingCommandExecutor>();
        services.AddScoped<IJobHandler>(
            provider => new CoordinatedJobHandler(
                definitions[0].JobKey,
                provider.GetRequiredService<ExecutionScopeIdentity>(),
                coordinator));
        services.AddScoped<IJobHandler>(
            provider => new CoordinatedJobHandler(
                definitions[1].JobKey,
                provider.GetRequiredService<ExecutionScopeIdentity>(),
                coordinator));
        services.AddScoped<JobHandlerRegistry>();
        await using var provider = services.BuildServiceProvider();
        var runner = new JobExecutionRunner(
            new MultipleJobsQueryExecutor(executions, definitions),
            new RecordingCommandExecutor(),
            new RecordingTransaction(),
            new JobHandlerRegistry([]),
            new FixedClock(
                new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero)),
            new FixedIdGenerator(Guid.CreateVersion7()),
            Options.Create(
                new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                }),
            Options.Create(
                new JobsWorkerOptions
                {
                    BatchSize = 3,
                    MaxConcurrency = 2,
                }),
            NullLogger<JobExecutionRunner>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>());

        var processed = await runner
            .ProcessPendingAsync(3, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(3, processed);
        Assert.AreEqual(2, coordinator.PeakConcurrency);
        Assert.AreEqual(1, coordinator.GetPeakFor(definitions[0].JobKey));
        Assert.AreEqual(1, coordinator.GetPeakFor(definitions[1].JobKey));
        Assert.HasCount(3, coordinator.HandlerScopeIds.Distinct().ToArray());
        CollectionAssert.AreEquivalent(
            coordinator.HandlerScopeIds.ToArray(),
            coordinator.CommandScopeIds.ToArray());
    }

    [TestMethod]
    public async Task ProcessPendingAsync_MultipleExecutionsLoadDefinitionsOncePerBatch()
    {
        var definitionId = Guid.CreateVersion7();
        var executions = new[]
        {
            new JobExecutionRecord
            {
                Id = Guid.CreateVersion7(),
                JobDefinitionId = definitionId,
                Status = JobExecutionStatuses.Running,
            },
            new JobExecutionRecord
            {
                Id = Guid.CreateVersion7(),
                JobDefinitionId = definitionId,
                Status = JobExecutionStatuses.Running,
            },
        };
        var definition = new JobDefinitionRecord
        {
            Id = definitionId,
            JobKey = ImmediateJobHandler.Key,
            IsEnabled = true,
        };
        var queryExecutor = new BatchDefinitionRecordingQueryExecutor(
            executions,
            definition);
        var transaction = new RecordingTransaction();
        var runner = new JobExecutionRunner(
            queryExecutor,
            new RecordingCommandExecutor(),
            transaction,
            new JobHandlerRegistry([new ImmediateJobHandler()]),
            new FixedClock(
                new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero)),
            new FixedIdGenerator(Guid.CreateVersion7()),
            Options.Create(
                new DatabaseOptions
                {
                    Provider = DatabaseProvider.MySql,
                }),
            Options.Create(new JobsWorkerOptions()),
            NullLogger<JobExecutionRunner>.Instance);

        var processed = await runner.ProcessPendingAsync(
            executions.Length,
            CancellationToken.None);

        Assert.AreEqual(executions.Length, processed);
        Assert.AreEqual(1, queryExecutor.DefinitionQueryCount);
        Assert.AreEqual(1, transaction.ExecutionCount);
    }

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
            new RecordingTransaction(),
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
            new RecordingTransaction(),
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
    public async Task ProcessPendingAsync_WhenFinalCompletionRacesWithZeroRowRenewal_ReturnsSuccess()
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
                JobKey = RenewalAwaitingJobHandler.Key,
                IsEnabled = true,
            });
        var commandExecutor = new LostLeaseCommandExecutor();
        var runner = new JobExecutionRunner(
            queryExecutor,
            commandExecutor,
            new RecordingTransaction(),
            new JobHandlerRegistry(
                [new CompletionAfterCancellationJobHandler()]),
            new FixedClock(
                new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero)),
            new FixedIdGenerator(Guid.CreateVersion7()),
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
        CollectionAssert.Contains(
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
            new RecordingTransaction(),
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

    private sealed class ImmediateJobHandler : IJobHandler
    {
        public const string Key = "jobs.test-immediate";

        public string JobKey => Key;

        public Task ExecuteAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static JobExecutionRecord CreateExecution(Guid definitionId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            JobDefinitionId = definitionId,
            Status = JobExecutionStatuses.Running,
        };

    private sealed class CoordinatedJobHandler(
        string jobKey,
        ExecutionScopeIdentity scopeIdentity,
        ParallelExecutionCoordinator coordinator) : IJobHandler
    {
        public string JobKey => jobKey;

        public Task ExecuteAsync(CancellationToken cancellationToken) =>
            coordinator.ExecuteAsync(
                JobKey,
                scopeIdentity.Id,
                cancellationToken);
    }

    private sealed class ExecutionScopeIdentity
    {
        public Guid Id { get; } = Guid.CreateVersion7();
    }

    private sealed class ParallelExecutionCoordinator(int expectedConcurrency)
    {
        private readonly TaskCompletionSource _allStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gate = new();
        private readonly Dictionary<string, int> _activeByKey =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _peakByKey =
            new(StringComparer.Ordinal);
        private int _active;

        public int PeakConcurrency { get; private set; }

        public List<Guid> HandlerScopeIds { get; } = [];

        public List<Guid> CommandScopeIds { get; } = [];

        public int GetPeakFor(string jobKey)
        {
            lock (_gate)
            {
                return _peakByKey.GetValueOrDefault(jobKey);
            }
        }

        public async Task ExecuteAsync(
            string jobKey,
            Guid scopeId,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                HandlerScopeIds.Add(scopeId);
                _active++;
                PeakConcurrency = Math.Max(PeakConcurrency, _active);
                var activeForKey = _activeByKey.GetValueOrDefault(jobKey) + 1;
                _activeByKey[jobKey] = activeForKey;
                _peakByKey[jobKey] = Math.Max(
                    _peakByKey.GetValueOrDefault(jobKey),
                    activeForKey);
                if (_active >= expectedConcurrency)
                {
                    _allStarted.TrySetResult();
                }
            }

            try
            {
                await _allStarted.Task
                    .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                {
                    _active--;
                    _activeByKey[jobKey]--;
                }
            }
        }

        public void RecordCommandScope(Guid scopeId)
        {
            lock (_gate)
            {
                CommandScopeIds.Add(scopeId);
            }
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

    private sealed class CompletionAfterCancellationJobHandler : IJobHandler
    {
        public string JobKey => RenewalAwaitingJobHandler.Key;

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
                // 模拟续租失败触发取消后，Handler 正常完成最终业务收尾。
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
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                $"Unexpected single-row statement '{statement.Name}'.");

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

            if (typeof(T) == typeof(JobDefinitionRecord)
                && statement == JobSql.FindDefinitionsByIds)
            {
                return Task.FromResult(
                    (IReadOnlyList<T>)(object)new[] { definition });
            }

            throw new InvalidOperationException(
                $"Unexpected list statement '{statement.Name}'.");
        }
    }

    private sealed class BatchDefinitionRecordingQueryExecutor(
        IReadOnlyList<JobExecutionRecord> executions,
        JobDefinitionRecord definition) : IQueryExecutor
    {
        public int DefinitionQueryCount { get; private set; }

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
            if (typeof(T) == typeof(Guid)
                && statement == JobSql.SelectClaimableExecutionIdsMySql)
            {
                return Task.FromResult(
                    (IReadOnlyList<T>)(object)executions
                        .Select(execution => execution.Id)
                        .ToArray());
            }

            if (typeof(T) == typeof(JobExecutionRecord)
                && statement == JobSql.SelectExecutionsByLeaseMySql)
            {
                return Task.FromResult(
                    (IReadOnlyList<T>)(object)executions);
            }

            if (typeof(T) == typeof(JobExecutionRecord)
                && statement == JobSql.AcquireExecutionsSqlServer)
            {
                return Task.FromResult(
                    (IReadOnlyList<T>)(object)executions);
            }

            if (typeof(T) == typeof(JobDefinitionRecord)
                && statement == JobSql.FindDefinitionsByIds)
            {
                DefinitionQueryCount++;
                return Task.FromResult(
                    (IReadOnlyList<T>)(object)new[] { definition });
            }

            throw new InvalidOperationException(
                $"Unexpected list statement '{statement.Name}'.");
        }
    }

    private sealed class MultipleJobsQueryExecutor(
        IReadOnlyList<JobExecutionRecord> executions,
        IReadOnlyList<JobDefinitionRecord> definitions) : IQueryExecutor
    {
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
            if (typeof(T) == typeof(JobExecutionRecord)
                && statement == JobSql.AcquireExecutionsSqlServer)
            {
                return Task.FromResult(
                    (IReadOnlyList<T>)(object)executions);
            }

            if (typeof(T) == typeof(JobDefinitionRecord)
                && statement == JobSql.FindDefinitionsByIds)
            {
                return Task.FromResult(
                    (IReadOnlyList<T>)(object)definitions);
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

    private sealed class ScopedRecordingCommandExecutor(
        ExecutionScopeIdentity scopeIdentity,
        ParallelExecutionCoordinator coordinator) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            coordinator.RecordCommandScope(scopeIdentity.Id);
            return Task.FromResult(1);
        }
    }

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public int ExecutionCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return await action(cancellationToken).ConfigureAwait(false);
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
