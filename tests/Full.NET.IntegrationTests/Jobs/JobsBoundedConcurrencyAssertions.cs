using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Jobs;

/// <summary>验证 Jobs 显式并发在真实双库上的顺序、Scope、续租和失败隔离边界。</summary>
internal static class JobsBoundedConcurrencyAssertions
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RenewalInterval = TimeSpan.FromSeconds(1);

    public static void ConfigureServices(
        IServiceCollection services,
        JobsBoundedConcurrencyProbe probe)
    {
        services.AddSingleton(probe);
        services.AddScoped<JobsExecutionScopeIdentity>();
        services.AddScoped<IJobHandler, FirstBlockingJobHandler>();
        services.AddScoped<IJobHandler, SecondBlockingJobHandler>();
        services.AddScoped<IJobHandler, FailingJobHandler>();
    }

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        JobsBoundedConcurrencyProbe probe,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(25));
        var testToken = timeoutSource.Token;
        var seeded = await SeedAsync(factory, testToken);
        await using var runnerScope = factory.Services.CreateAsyncScope();
        await using var observerScope = factory.Services.CreateAsyncScope();
        runnerScope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>()
            .SetHost();
        observerScope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>()
            .SetHost();
        var runner = CreateRunner(runnerScope.ServiceProvider);
        var runnerTask = runner.ProcessPendingAsync(4, testToken);

        try
        {
            await probe.BlockingHandlersStarted.WaitAsync(testToken);
            var initial = await ReadStateAsync(
                observerScope.ServiceProvider,
                seeded.FirstExecutionId,
                testToken);
            Assert.IsNotNull(initial.LeaseExpiresAtUtc);
            var initialLeaseExpiresAtUtc = initial.LeaseExpiresAtUtc.Value;
            var renewed = await WaitForStateAsync(
                observerScope.ServiceProvider,
                seeded.FirstExecutionId,
                state => state.LeaseExpiresAtUtc > initialLeaseExpiresAtUtc,
                testToken,
                runnerTask);
            Assert.IsTrue(
                renewed.LeaseExpiresAtUtc > initialLeaseExpiresAtUtc,
                "慢 Handler 必须在并发批次中继续推进租约。");

            probe.Release();
            Assert.AreEqual(4, await runnerTask.WaitAsync(testToken));
            var states = await ReadStatesAsync(
                observerScope.ServiceProvider,
                seeded.ExecutionIds,
                testToken);

            Assert.AreEqual(2, probe.PeakConcurrency);
            Assert.AreEqual(1, probe.GetPeakFor(FirstBlockingJobHandler.Key));
            Assert.AreEqual(1, probe.GetPeakFor(SecondBlockingJobHandler.Key));
            Assert.HasCount(4, probe.ScopeIds.Distinct().ToArray());
            Assert.AreEqual(
                JobExecutionStatuses.Failed,
                states.Single(state => state.Id == seeded.FailingExecutionId).Status);
            Assert.IsTrue(
                states
                    .Where(state => state.Id != seeded.FailingExecutionId)
                    .All(state => state.Status == JobExecutionStatuses.Succeeded),
                "单条 Handler 失败不得阻断同批健康执行进入成功终态。");
            Assert.IsTrue(
                states.All(state =>
                    state.AttemptCount == 1
                    && state.LeaseId is null
                    && state.LeaseExpiresAtUtc is null
                    && state.FinishedAtUtc is not null),
                "四条执行都必须只领取一次并清理终态租约。");
        }
        finally
        {
            probe.Release();
            try
            {
                await runnerTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception) when (runnerTask.IsCompleted)
            {
                // 主断言保留原始故障；这里只观察任务，避免清理阶段遗留未观察异常。
            }
        }
    }

    private static JobExecutionRunner CreateRunner(IServiceProvider services) =>
        new(
            services.GetRequiredService<IQueryExecutor>(),
            services.GetRequiredService<ICommandExecutor>(),
            services.GetRequiredService<ICommandTransaction>(),
            services.GetRequiredService<JobHandlerRegistry>(),
            services.GetRequiredService<Full.NET.Abstractions.Time.IClock>(),
            services.GetRequiredService<Full.NET.Abstractions.Ids.IIdGenerator>(),
            services.GetRequiredService<IOptions<DatabaseOptions>>(),
            Options.Create(
                new JobsWorkerOptions
                {
                    BatchSize = 4,
                    MaxConcurrency = 3,
                    LeaseSeconds = (int)LeaseDuration.TotalSeconds,
                    LeaseRenewalSeconds = (int)RenewalInterval.TotalSeconds,
                }),
            NullLogger<JobExecutionRunner>.Instance,
            services.GetRequiredService<IServiceScopeFactory>());

    private static async Task<SeededExecutions> SeedAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            (Id: Guid.CreateVersion7(), Key: FirstBlockingJobHandler.Key),
            (Id: Guid.CreateVersion7(), Key: SecondBlockingJobHandler.Key),
            (Id: Guid.CreateVersion7(), Key: FailingJobHandler.Key),
        };
        var executionDefinitions = new[]
        {
            definitions[0].Id,
            definitions[0].Id,
            definitions[1].Id,
            definitions[2].Id,
        };
        var executionIds = executionDefinitions
            .Select(_ => Guid.CreateVersion7())
            .ToArray();
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var now = DateTimeOffset.UtcNow;
        foreach (var definition in definitions)
        {
            await command.ExecuteAsync(
                new SqlStatement(
                    "test.jobs.insert_bounded_concurrency_definition",
                    """
                    INSERT INTO fn_jobs_definition
                        (Id, TenantId, JobKey, DisplayName, Description, IsEnabled,
                         AllowConcurrentExecutions,
                         CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version)
                    VALUES
                        (@Id, NULL, @JobKey, @DisplayName, NULL, @IsEnabled,
                         1,
                         @CreatedAtUtc, NULL, @CreatedByUserId, NULL, 1)
                    """,
                    SqlDataScope.HostOnly),
                new
                {
                    definition.Id,
                    JobKey = definition.Key,
                    DisplayName = definition.Key,
                    IsEnabled = true,
                    CreatedAtUtc = now,
                    CreatedByUserId = Guid.CreateVersion7(),
                },
                cancellationToken);
        }

        for (var index = 0; index < executionIds.Length; index++)
        {
            await command.ExecuteAsync(
                new SqlStatement(
                    "test.jobs.insert_bounded_concurrency_execution",
                    """
                    INSERT INTO fn_jobs_execution
                        (Id, TenantId, JobDefinitionId, Status, TriggerKind,
                         ErrorMessage, StartedAtUtc, FinishedAtUtc,
                         LeaseId, LeaseExpiresAtUtc, AttemptCount, CreatedAtUtc)
                    VALUES
                        (@Id, NULL, @JobDefinitionId, @Status, @TriggerKind,
                         NULL, NULL, NULL, NULL, NULL, 0, @CreatedAtUtc)
                    """,
                    SqlDataScope.HostOnly),
                new
                {
                    Id = executionIds[index],
                    JobDefinitionId = executionDefinitions[index],
                    Status = JobExecutionStatuses.Pending,
                    TriggerKind = JobTriggerKinds.Manual,
                    CreatedAtUtc = now.AddMilliseconds(index),
                },
                cancellationToken);
        }

        return new SeededExecutions(
            executionIds,
            executionIds[0],
            executionIds[3]);
    }

    private static async Task<ExecutionState> WaitForStateAsync(
        IServiceProvider services,
        Guid executionId,
        Func<ExecutionState, bool> predicate,
        CancellationToken cancellationToken,
        Task<int>? runnerTask = null)
    {
        while (true)
        {
            if (runnerTask?.IsCompleted == true)
            {
                await runnerTask.ConfigureAwait(false);
                throw new InvalidOperationException(
                    "Jobs runner completed before the expected state was observed.");
            }

            var state = await ReadStateAsync(
                services,
                executionId,
                cancellationToken);
            if (predicate(state))
            {
                return state;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private static async Task<ExecutionState> ReadStateAsync(
        IServiceProvider services,
        Guid executionId,
        CancellationToken cancellationToken) =>
        await services.GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<ExecutionState>(
                StateQuery,
                new { Id = executionId },
                cancellationToken)
        ?? throw new InvalidOperationException(
            $"Job execution '{executionId:D}' was not found.");

    private static Task<IReadOnlyList<ExecutionState>> ReadStatesAsync(
        IServiceProvider services,
        Guid[] executionIds,
        CancellationToken cancellationToken) =>
        services.GetRequiredService<IQueryExecutor>().QueryAsync<ExecutionState>(
            StatesQuery,
            new { Ids = executionIds },
            cancellationToken);

    private static readonly SqlStatement StateQuery = new(
        "test.jobs.find_bounded_concurrency_state",
        """
        SELECT Id, Status, AttemptCount, LeaseId,
               LeaseExpiresAtUtc, FinishedAtUtc
        FROM fn_jobs_execution
        WHERE Id = @Id AND TenantId IS NULL
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement StatesQuery = new(
        "test.jobs.list_bounded_concurrency_states",
        """
        SELECT Id, Status, AttemptCount, LeaseId,
               LeaseExpiresAtUtc, FinishedAtUtc
        FROM fn_jobs_execution
        WHERE TenantId IS NULL AND Id IN @Ids
        """,
        SqlDataScope.HostOnly);

    private sealed record SeededExecutions(
        Guid[] ExecutionIds,
        Guid FirstExecutionId,
        Guid FailingExecutionId);

    private sealed class ExecutionState
    {
        public Guid Id { get; set; }

        public string Status { get; set; } = string.Empty;

        public int AttemptCount { get; set; }

        public Guid? LeaseId { get; set; }

        public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

        public DateTimeOffset? FinishedAtUtc { get; set; }
    }
}

internal sealed class JobsBoundedConcurrencyProbe
{
    private readonly TaskCompletionSource _blockingHandlersStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _activeByKey =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _peakByKey =
        new(StringComparer.Ordinal);
    private int _active;

    public Task BlockingHandlersStarted => _blockingHandlersStarted.Task;

    public int PeakConcurrency { get; private set; }

    public List<Guid> ScopeIds { get; } = [];

    public int GetPeakFor(string jobKey)
    {
        lock (_gate)
        {
            return _peakByKey.GetValueOrDefault(jobKey);
        }
    }

    public async Task ExecuteBlockingAsync(
        string jobKey,
        Guid scopeId,
        CancellationToken cancellationToken)
    {
        Enter(jobKey, scopeId);
        try
        {
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit(jobKey);
        }
    }

    public void RecordFailureScope(Guid scopeId)
    {
        lock (_gate)
        {
            ScopeIds.Add(scopeId);
        }
    }

    public void Release() => _release.TrySetResult();

    private void Enter(string jobKey, Guid scopeId)
    {
        lock (_gate)
        {
            ScopeIds.Add(scopeId);
            _active++;
            PeakConcurrency = Math.Max(PeakConcurrency, _active);
            var activeForKey = _activeByKey.GetValueOrDefault(jobKey) + 1;
            _activeByKey[jobKey] = activeForKey;
            _peakByKey[jobKey] = Math.Max(
                _peakByKey.GetValueOrDefault(jobKey),
                activeForKey);
            if (_active >= 2)
            {
                _blockingHandlersStarted.TrySetResult();
            }
        }
    }

    private void Exit(string jobKey)
    {
        lock (_gate)
        {
            _active--;
            _activeByKey[jobKey]--;
        }
    }
}

internal sealed class JobsExecutionScopeIdentity
{
    public Guid Id { get; } = Guid.CreateVersion7();
}

internal sealed class FirstBlockingJobHandler(
    JobsExecutionScopeIdentity scopeIdentity,
    JobsBoundedConcurrencyProbe probe) : IJobHandler
{
    public const string Key = "jobs.test-bounded-concurrency-a";

    public string JobKey => Key;

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        probe.ExecuteBlockingAsync(Key, scopeIdentity.Id, cancellationToken);
}

internal sealed class SecondBlockingJobHandler(
    JobsExecutionScopeIdentity scopeIdentity,
    JobsBoundedConcurrencyProbe probe) : IJobHandler
{
    public const string Key = "jobs.test-bounded-concurrency-b";

    public string JobKey => Key;

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        probe.ExecuteBlockingAsync(Key, scopeIdentity.Id, cancellationToken);
}

internal sealed class FailingJobHandler(
    JobsExecutionScopeIdentity scopeIdentity,
    JobsBoundedConcurrencyProbe probe) : IJobHandler
{
    public const string Key = "jobs.test-bounded-concurrency-failure";

    public string JobKey => Key;

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        probe.RecordFailureScope(scopeIdentity.Id);
        throw new InvalidOperationException("Expected bounded concurrency failure.");
    }
}
