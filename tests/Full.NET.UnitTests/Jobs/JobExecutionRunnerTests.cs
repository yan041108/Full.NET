using System.Diagnostics.Metrics;
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
[DoNotParallelize]
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
                HandlerKind = "jobs.test-parallel-a",
                IsEnabled = true,
            },
            new JobDefinitionRecord
            {
                Id = secondDefinitionId,
                JobKey = "jobs.test-parallel-b",
                HandlerKind = "jobs.test-parallel-b",
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
        services.AddScoped<IJobHandlerExecutor>(
            provider => new CoordinatedJobHandler(
                definitions[0].JobKey,
                provider.GetRequiredService<ExecutionScopeIdentity>(),
                coordinator));
        services.AddScoped<IJobHandlerExecutor>(
            provider => new CoordinatedJobHandler(
                definitions[1].JobKey,
                provider.GetRequiredService<ExecutionScopeIdentity>(),
                coordinator));
        services.AddScoped<JobHandlerKindRegistry>();
        await using var provider = services.BuildServiceProvider();
        var runner = new JobExecutionRunner(
            new MultipleJobsQueryExecutor(executions, definitions),
            new RecordingCommandExecutor(),
            new RecordingTransaction(),
            new JobHandlerKindRegistry([]),
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
            HandlerKind = ImmediateJobHandler.Key,
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
            new JobHandlerKindRegistry([new ImmediateJobHandler()]),
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
        using var metrics = new JobsMetricCapture();

        var processed = await runner.ProcessPendingAsync(
            executions.Length,
            CancellationToken.None);

        Assert.AreEqual(executions.Length, processed);
        Assert.AreEqual(1, queryExecutor.DefinitionQueryCount);
        Assert.AreEqual(1, transaction.ExecutionCount);
        AssertOutcomeMeasurements(
            metrics,
            expectedCount: executions.Length,
            expectedOutcome: "succeeded");

        using var throwingListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Full.NET.Jobs")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        throwingListener.SetMeasurementEventCallback<long>(
            (_, _, _, _) =>
                throw new InvalidOperationException(
                    "模拟任务指标监听器故障。"));
        throwingListener.Start();
        try
        {
            JobsTelemetry.RecordSucceeded();
        }
        catch (InvalidOperationException exception)
        {
            Assert.Fail(
                $"任务指标监听器故障不得改变执行结果：{exception.Message}");
        }
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
                HandlerKind = RenewalAwaitingJobHandler.Key,
                IsEnabled = true,
            });
        var commandExecutor = new RenewalRecordingCommandExecutor();
        var runner = new JobExecutionRunner(
            queryExecutor,
            commandExecutor,
            new RecordingTransaction(),
            new JobHandlerKindRegistry(
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
                HandlerKind = CancellationAwaitingJobHandler.Key,
                IsEnabled = true,
            });
        var commandExecutor = new LostLeaseCommandExecutor();
        var handler = new CancellationAwaitingJobHandler();
        var runner = new JobExecutionRunner(
            queryExecutor,
            commandExecutor,
            new RecordingTransaction(),
            new JobHandlerKindRegistry([handler]),
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
                HandlerKind = RenewalAwaitingJobHandler.Key,
                IsEnabled = true,
            });
        var commandExecutor = new LostLeaseCommandExecutor();
        var runner = new JobExecutionRunner(
            queryExecutor,
            commandExecutor,
            new RecordingTransaction(),
            new JobHandlerKindRegistry(
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
                HandlerKind = CancellingJobHandler.Key,
                IsEnabled = true,
            });
        var commandExecutor = new RecordingCommandExecutor();
        using var cancellationTokenSource = new CancellationTokenSource();
        var runner = new JobExecutionRunner(
            queryExecutor,
            commandExecutor,
            new RecordingTransaction(),
            new JobHandlerKindRegistry(
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

    [TestMethod]
    public async Task ProcessPendingAsync_WhenRetryableFailureHasAttemptsRemaining_ReschedulesWithDueTime()
    {
        var now = new DateTimeOffset(
            2026,
            7,
            30,
            1,
            0,
            0,
            TimeSpan.Zero);
        var commandExecutor = new FailureRecordingCommandExecutor();
        using var metrics = new JobsMetricCapture();
        var runner = CreateFailureRunner(
            new RetryableFailureJobHandler(),
            attemptCount: 1,
            maxAttempts: 3,
            retryDelaySeconds: 45,
            now,
            commandExecutor);

        var processed = await runner.ProcessPendingAsync(
            1,
            CancellationToken.None);

        Assert.AreEqual(1, processed);
        CollectionAssert.Contains(
            commandExecutor.Statements,
            JobSql.RescheduleExecution);
        CollectionAssert.DoesNotContain(
            commandExecutor.Statements,
            JobSql.MarkExecutionFailed);
        Assert.AreEqual(
            now.AddSeconds(45),
            commandExecutor.ReadParameter<DateTimeOffset>(
                JobSql.RescheduleExecution,
                "NextAttemptAtUtc"));
        Assert.AreEqual(
            RetryableFailureJobHandler.ErrorMessage,
            commandExecutor.ReadParameter<string>(
                JobSql.RescheduleExecution,
                "ErrorMessage"));
        AssertOutcomeMeasurements(
            metrics,
            expectedCount: 1,
            expectedOutcome: "retry_scheduled");
        AssertCounterMeasurements(
            metrics,
            "fullnet.jobs.retry.scheduled",
            expectedCount: 1);
        AssertCounterMeasurements(
            metrics,
            "fullnet.jobs.retry.exhausted",
            expectedCount: 0);

    }

    [TestMethod]
    public async Task ProcessPendingAsync_WhenExponentialRetryIsConfigured_UsesAttemptBasedDelay()
    {
        var now = new DateTimeOffset(
            2026,
            7,
            30,
            1,
            0,
            0,
            TimeSpan.Zero);
        var commandExecutor = new FailureRecordingCommandExecutor();
        using var metrics = new JobsMetricCapture();
        var runner = CreateFailureRunner(
            new RetryableFailureJobHandler(),
            attemptCount: 3,
            maxAttempts: 4,
            retryDelaySeconds: 30,
            now,
            commandExecutor,
            workerOptions: new JobsWorkerOptions
            {
                MaxAttempts = 4,
                RetryDelaySeconds = 30,
                RetryBackoffMode = "exponential",
                RetryMaxDelaySeconds = 1000,
                RetryJitterPercent = 20,
            },
            retryJitterSource: new FixedJobsRetryJitterSource(1.0));

        await runner.ProcessPendingAsync(1, CancellationToken.None);

        Assert.AreEqual(
            now.AddSeconds(144),
            commandExecutor.ReadParameter<DateTimeOffset>(
                JobSql.RescheduleExecution,
                "NextAttemptAtUtc"));
        var retryDelays = metrics.DoubleMeasurements
            .Where(item => item.Name == "fullnet.jobs.retry.delay")
            .ToArray();
        Assert.HasCount(1, retryDelays);
        Assert.AreEqual("s", retryDelays[0].Unit);
        Assert.AreEqual(144d, retryDelays[0].Value);
        Assert.HasCount(0, retryDelays[0].Tags);
    }

    [TestMethod]
    public async Task ProcessPendingAsync_WhenRetryableFailureExhaustsAttempts_MarksFailure()
    {
        var commandExecutor = new FailureRecordingCommandExecutor();
        using var metrics = new JobsMetricCapture();
        var runner = CreateFailureRunner(
            new RetryableFailureJobHandler(),
            attemptCount: 3,
            maxAttempts: 3,
            retryDelaySeconds: 30,
            new DateTimeOffset(2026, 7, 30, 1, 0, 0, TimeSpan.Zero),
            commandExecutor);

        await runner.ProcessPendingAsync(1, CancellationToken.None);

        CollectionAssert.Contains(
            commandExecutor.Statements,
            JobSql.MarkExecutionFailed);
        CollectionAssert.DoesNotContain(
            commandExecutor.Statements,
            JobSql.RescheduleExecution);
        AssertOutcomeMeasurements(
            metrics,
            expectedCount: 1,
            expectedOutcome: "failed");
        AssertCounterMeasurements(
            metrics,
            "fullnet.jobs.retry.exhausted",
            expectedCount: 1);
    }

    [TestMethod]
    public async Task ProcessPendingAsync_WhenOrdinaryFailureHasAttemptsRemaining_MarksFailure()
    {
        var commandExecutor = new FailureRecordingCommandExecutor();
        using var metrics = new JobsMetricCapture();
        var runner = CreateFailureRunner(
            new TerminalFailureJobHandler(),
            attemptCount: 1,
            maxAttempts: 3,
            retryDelaySeconds: 30,
            new DateTimeOffset(2026, 7, 30, 1, 0, 0, TimeSpan.Zero),
            commandExecutor);

        await runner.ProcessPendingAsync(1, CancellationToken.None);

        CollectionAssert.Contains(
            commandExecutor.Statements,
            JobSql.MarkExecutionFailed);
        CollectionAssert.DoesNotContain(
            commandExecutor.Statements,
            JobSql.RescheduleExecution);
        AssertOutcomeMeasurements(
            metrics,
            expectedCount: 1,
            expectedOutcome: "failed");
        AssertCounterMeasurements(
            metrics,
            "fullnet.jobs.retry.exhausted",
            expectedCount: 0);

        using var lostOwnershipMetrics = new JobsMetricCapture();
        var lostOwnershipCommandExecutor =
            new FailureRecordingCommandExecutor(affectedRows: 0);
        var lostOwnershipRunner = CreateFailureRunner(
            new TerminalFailureJobHandler(),
            attemptCount: 1,
            maxAttempts: 3,
            retryDelaySeconds: 30,
            new DateTimeOffset(2026, 7, 30, 1, 0, 0, TimeSpan.Zero),
            lostOwnershipCommandExecutor);

        await lostOwnershipRunner.ProcessPendingAsync(
            1,
            CancellationToken.None);

        AssertOutcomeMeasurements(
            lostOwnershipMetrics,
            expectedCount: 0,
            expectedOutcome: "failed");
    }

    private static void AssertOutcomeMeasurements(
        JobsMetricCapture metrics,
        int expectedCount,
        string expectedOutcome)
    {
        var outcomes = metrics.Measurements
            .Where(item =>
                item.Name == "fullnet.jobs.execution.transitions")
            .ToArray();
        Assert.HasCount(expectedCount, outcomes);
        Assert.IsTrue(outcomes.All(item => item.Value == 1L));
        Assert.IsTrue(outcomes.All(item => item.Tags.Length == 1));
        Assert.IsTrue(outcomes.All(item => item.Tags[0].Key == "outcome"));
        Assert.IsTrue(outcomes.All(
            item => Equals(item.Tags[0].Value, expectedOutcome)));
    }

    private static void AssertCounterMeasurements(
        JobsMetricCapture metrics,
        string name,
        int expectedCount)
    {
        var measurements = metrics.Measurements
            .Where(item => item.Name == name)
            .ToArray();
        Assert.HasCount(expectedCount, measurements);
        Assert.IsTrue(measurements.All(item => item.Value == 1L));
        Assert.IsTrue(measurements.All(item => item.Tags.Length == 0));
    }

    private static JobExecutionRunner CreateFailureRunner(
        IJobHandlerExecutor handler,
        int attemptCount,
        int maxAttempts,
        int retryDelaySeconds,
        DateTimeOffset now,
        FailureRecordingCommandExecutor commandExecutor,
        JobsWorkerOptions? workerOptions = null,
        IJobsRetryJitterSource? retryJitterSource = null)
    {
        var definitionId = Guid.CreateVersion7();
        return new JobExecutionRunner(
            new StubQueryExecutor(
                new JobExecutionRecord
                {
                    Id = Guid.CreateVersion7(),
                    JobDefinitionId = definitionId,
                    Status = JobExecutionStatuses.Running,
                    AttemptCount = attemptCount,
                },
                new JobDefinitionRecord
                {
                    Id = definitionId,
                    JobKey = handler.HandlerKind,
                    HandlerKind = handler.HandlerKind,
                    IsEnabled = true,
                }),
            commandExecutor,
            new RecordingTransaction(),
            new JobHandlerKindRegistry([handler]),
            new FixedClock(now),
            new FixedIdGenerator(Guid.CreateVersion7()),
            Options.Create(
                new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                }),
            Options.Create(
                workerOptions
                ?? new JobsWorkerOptions
                {
                    MaxAttempts = maxAttempts,
                    RetryDelaySeconds = retryDelaySeconds,
                }),
            NullLogger<JobExecutionRunner>.Instance,
            retryJitterSource: retryJitterSource);
    }

    private sealed class FixedJobsRetryJitterSource(double value)
        : IJobsRetryJitterSource
    {
        public double NextUnitInterval()
        {
            return value;
        }
    }

    private sealed class CancellingJobHandler(
        CancellationTokenSource cancellationTokenSource) : IJobHandlerExecutor
    {
        public const string Key = "jobs.test-cancellation";

        public string HandlerKind => Key;

        public Task ExecuteAsync(
            JobExecutionContext context,
            CancellationToken cancellationToken)
        {
            cancellationTokenSource.Cancel();
            return Task.FromCanceled(cancellationToken);
        }
    }

    private sealed class ImmediateJobHandler : IJobHandlerExecutor
    {
        public const string Key = "jobs.test-immediate";

        public string HandlerKind => Key;

        public Task ExecuteAsync(
            JobExecutionContext context,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RetryableFailureJobHandler : IJobHandlerExecutor
    {
        public const string Key = "jobs.test-retryable-failure";

        public const string ErrorMessage = "retryable test failure";

        public string HandlerKind => Key;

        public Task ExecuteAsync(
            JobExecutionContext context,
            CancellationToken cancellationToken) =>
            throw new RetryableJobException(ErrorMessage);
    }

    private sealed class TerminalFailureJobHandler : IJobHandlerExecutor
    {
        public const string Key = "jobs.test-terminal-failure";

        public string HandlerKind => Key;

        public Task ExecuteAsync(
            JobExecutionContext context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("terminal test failure");
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
        ParallelExecutionCoordinator coordinator) : IJobHandlerExecutor
    {
        public string HandlerKind => jobKey;

        public Task ExecuteAsync(
            JobExecutionContext context,
            CancellationToken cancellationToken) =>
            coordinator.ExecuteAsync(
                context.JobKey,
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

    private sealed class RenewalAwaitingJobHandler(Task renewalObserved) : IJobHandlerExecutor
    {
        public const string Key = "jobs.test-active-renewal";

        public string HandlerKind => Key;

        public async Task ExecuteAsync(
            JobExecutionContext context,
            CancellationToken cancellationToken)
        {
            await renewalObserved
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private sealed class CancellationAwaitingJobHandler : IJobHandlerExecutor
    {
        private readonly TaskCompletionSource _cancellationObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public const string Key = "jobs.test-lost-lease";

        public string HandlerKind => Key;

        public Task CancellationObserved => _cancellationObserved.Task;

        public async Task ExecuteAsync(
            JobExecutionContext context,
            CancellationToken cancellationToken)
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

    private sealed class CompletionAfterCancellationJobHandler : IJobHandlerExecutor
    {
        public string HandlerKind => RenewalAwaitingJobHandler.Key;

        public async Task ExecuteAsync(
            JobExecutionContext context,
            CancellationToken cancellationToken)
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

    private sealed class FailureRecordingCommandExecutor(
        int affectedRows = 1) : ICommandExecutor
    {
        private readonly Dictionary<SqlStatement, object?> _parameters = [];

        public List<SqlStatement> Statements { get; } = [];

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            _parameters[statement] = parameters;
            return Task.FromResult(affectedRows);
        }

        public T ReadParameter<T>(SqlStatement statement, string name) =>
            (T?)_parameters[statement]?
                .GetType()
                .GetProperty(name)?
                .GetValue(_parameters[statement])
            ?? throw new InvalidOperationException(
                $"Command parameter '{name}' was not provided.");
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

    private sealed class JobsMetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public JobsMetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Full.NET.Jobs")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(
                (instrument, value, tags, _) =>
                    Measurements.Add(
                        new JobsMeasurement(
                            instrument.Name,
                            value,
                            tags.ToArray())));
            _listener.SetMeasurementEventCallback<double>(
                (instrument, value, tags, _) =>
                    DoubleMeasurements.Add(
                        new JobsDoubleMeasurement(
                            instrument.Name,
                            instrument.Unit,
                            value,
                            tags.ToArray())));
            _listener.Start();
        }

        public List<JobsMeasurement> Measurements { get; } = [];

        public List<JobsDoubleMeasurement> DoubleMeasurements { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    private sealed record JobsMeasurement(
        string Name,
        long Value,
        KeyValuePair<string, object?>[] Tags);

    private sealed record JobsDoubleMeasurement(
        string Name,
        string? Unit,
        double Value,
        KeyValuePair<string, object?>[] Tags);

}
