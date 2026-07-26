using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Jobs;

/// <summary>验证长任务在 SQL Server 与 MySQL 上持续持有自己的执行租约。</summary>
internal static class JobsActiveLeaseRenewalAssertions
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RenewalInterval = TimeSpan.FromSeconds(1);

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(20));
        var testToken = timeoutSource.Token;
        var definitionId = Guid.CreateVersion7();
        var executionId = Guid.CreateVersion7();
        await SeedPendingExecutionAsync(
            factory,
            definitionId,
            executionId,
            testToken);

        await using var workerScope = factory.Services.CreateAsyncScope();
        await using var observerScope = factory.Services.CreateAsyncScope();
        await using var contenderScope = factory.Services.CreateAsyncScope();
        var workerTenant = workerScope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>();
        var observerTenant = observerScope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>();
        var contenderTenant = contenderScope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>();
        workerTenant.SetHost();
        observerTenant.SetHost();
        contenderTenant.SetHost();
        var handler = new BlockingJobHandler();
        var worker = CreateRunner(
            workerScope.ServiceProvider,
            new JobHandlerRegistry([handler]),
            (int)LeaseDuration.TotalSeconds,
            (int)RenewalInterval.TotalSeconds);
        var contender = CreateRunner(
            contenderScope.ServiceProvider,
            new JobHandlerRegistry([]),
            (int)LeaseDuration.TotalSeconds,
            (int)RenewalInterval.TotalSeconds);
        var workerTask = worker.ProcessPendingAsync(1, testToken);

        try
        {
            await handler.Started.WaitAsync(testToken);
            var initial = await WaitForStateAsync(
                observerScope.ServiceProvider,
                executionId,
                state => state.LeaseExpiresAtUtc is not null,
                testToken);
            var initialLeaseExpiresAtUtc = initial.LeaseExpiresAtUtc!.Value;
            var renewed = await WaitForStateAsync(
                observerScope.ServiceProvider,
                executionId,
                state => state.LeaseExpiresAtUtc > initialLeaseExpiresAtUtc,
                testToken);
            Assert.IsTrue(
                renewed.LeaseExpiresAtUtc > initialLeaseExpiresAtUtc,
                "续租必须把当前 Worker 持有的租约推进到初始到期时间之后。");

            var untilInitialExpiry =
                initialLeaseExpiresAtUtc - DateTimeOffset.UtcNow
                + TimeSpan.FromMilliseconds(150);
            if (untilInitialExpiry > TimeSpan.Zero)
            {
                await Task.Delay(untilInitialExpiry, testToken);
            }

            var contenderProcessed = await contender.ProcessPendingAsync(
                1,
                testToken);
            Assert.AreEqual(
                0,
                contenderProcessed,
                "初始租约到期后，其他 Worker 仍不得领取正在主动续租的任务。");

            handler.Release();
            Assert.AreEqual(1, await workerTask.WaitAsync(testToken));
            var final = await WaitForStateAsync(
                observerScope.ServiceProvider,
                executionId,
                state => state.Status == JobExecutionStatuses.Succeeded,
                testToken);
            Assert.AreEqual(1, final.AttemptCount);
            Assert.IsNull(final.LeaseId);
            Assert.IsNull(final.LeaseExpiresAtUtc);
            Assert.IsNotNull(final.FinishedAtUtc);
        }
        finally
        {
            handler.Release();
            try
            {
                await workerTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception) when (workerTask.IsCompleted)
            {
                // 主断言已保留原始故障；这里只观察任务，防止测试清理遗留后台异常。
            }

            workerTenant.Clear();
            observerTenant.Clear();
            contenderTenant.Clear();
        }
    }

    private static JobExecutionRunner CreateRunner(
        IServiceProvider services,
        JobHandlerRegistry registry,
        int leaseSeconds,
        int leaseRenewalSeconds) =>
        new(
            services.GetRequiredService<IQueryExecutor>(),
            services.GetRequiredService<ICommandExecutor>(),
            registry,
            services.GetRequiredService<IClock>(),
            services.GetRequiredService<IIdGenerator>(),
            services.GetRequiredService<IOptions<DatabaseOptions>>(),
            Options.Create(
                new JobsWorkerOptions
                {
                    LeaseSeconds = leaseSeconds,
                    LeaseRenewalSeconds = leaseRenewalSeconds,
                }),
            NullLogger<JobExecutionRunner>.Instance);

    private static async Task SeedPendingExecutionAsync(
        FullNetApiFactory factory,
        Guid definitionId,
        Guid executionId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var command = scope.ServiceProvider
                .GetRequiredService<ICommandExecutor>();
            var now = DateTimeOffset.UtcNow;
            await command.ExecuteAsync(
                new SqlStatement(
                    "test.jobs.insert_active_lease_definition",
                    """
                    INSERT INTO fn_jobs_definition
                        (Id, TenantId, JobKey, DisplayName, Description, IsEnabled,
                         CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version)
                    VALUES
                        (@Id, NULL, @JobKey, @DisplayName, NULL, @IsEnabled,
                         @CreatedAtUtc, NULL, @CreatedByUserId, NULL, 1)
                    """,
                    SqlDataScope.HostOnly),
                new
                {
                    Id = definitionId,
                    JobKey = BlockingJobHandler.Key,
                    DisplayName = "集成长任务主动续租",
                    IsEnabled = true,
                    CreatedAtUtc = now,
                    CreatedByUserId = Guid.CreateVersion7(),
                },
                cancellationToken);
            await command.ExecuteAsync(
                new SqlStatement(
                    "test.jobs.insert_active_lease_execution",
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
                    Id = executionId,
                    JobDefinitionId = definitionId,
                    Status = JobExecutionStatuses.Pending,
                    TriggerKind = JobTriggerKinds.Manual,
                    CreatedAtUtc = now,
                },
                cancellationToken);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static async Task<ExecutionState> WaitForStateAsync(
        IServiceProvider services,
        Guid executionId,
        Func<ExecutionState, bool> predicate,
        CancellationToken cancellationToken)
    {
        var query = services.GetRequiredService<IQueryExecutor>();
        var statement = new SqlStatement(
            "test.jobs.find_active_lease_execution_state",
            """
            SELECT Id, Status, AttemptCount, LeaseId,
                   LeaseExpiresAtUtc, FinishedAtUtc
            FROM fn_jobs_execution
            WHERE Id = @Id AND TenantId IS NULL
            """,
            SqlDataScope.HostOnly);
        while (true)
        {
            var state = await query.QuerySingleOrDefaultAsync<ExecutionState>(
                statement,
                new { Id = executionId },
                cancellationToken);
            if (state is not null && predicate(state))
            {
                return state;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private sealed class BlockingJobHandler : IJobHandler
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public const string Key = "jobs.test-active-lease-renewal";

        public string JobKey => Key;

        public Task Started => _started.Task;

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await _release.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        public void Release() => _release.TrySetResult();
    }

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
