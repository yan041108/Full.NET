using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Jobs;

/// <summary>验证多个 Worker 同时轮询时，每条 Host 任务只会被一个租约领取。</summary>
internal static class JobsMultiWorkerClaimAssertions
{
    private const int ExecutionCount = 32;
    private const int WorkerCount = 4;
    private const int WorkerBatchSize = ExecutionCount / WorkerCount;

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        Guid definitionId,
        CancellationToken cancellationToken = default)
    {
        await SetAllowConcurrentExecutionsAsync(
            factory,
            definitionId,
            allowConcurrentExecutions: true,
            cancellationToken);

        var executionIds = await SeedExecutionsAsync(
            factory,
            definitionId,
            cancellationToken);

        var workerScopes = Enumerable.Range(0, WorkerCount)
            .Select(_ => factory.Services.CreateAsyncScope())
            .ToArray();
        try
        {
            var startSignal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var workers = workerScopes
                .Select(scope => ProcessWorkerAsync(
                    scope,
                    startSignal.Task,
                    cancellationToken))
                .ToArray();

            startSignal.SetResult();
            var processedCounts = await Task.WhenAll(workers);

            Assert.AreEqual(
                ExecutionCount,
                processedCounts.Sum(),
                "多个 Worker 的处理总数必须与待处理任务数完全一致。");
        }
        finally
        {
            foreach (var scope in workerScopes)
            {
                await scope.DisposeAsync();
            }
        }

        await AssertFinalStateAsync(
            factory,
            definitionId,
            executionIds,
            cancellationToken);
    }

    private static async Task SetAllowConcurrentExecutionsAsync(
        FullNetApiFactory factory,
        Guid definitionId,
        bool allowConcurrentExecutions,
        CancellationToken cancellationToken)
    {
        await SetAllowConcurrentExecutionsForTestAsync(
            factory,
            definitionId,
            allowConcurrentExecutions,
            cancellationToken);
    }

    internal static async Task SetAllowConcurrentExecutionsForTestAsync(
        FullNetApiFactory factory,
        Guid definitionId,
        bool allowConcurrentExecutions,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant =
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>()
                .ExecuteAsync(
                    new SqlStatement(
                        "test.jobs.set_allow_concurrent_executions",
                        """
                        UPDATE fn_jobs_definition
                        SET AllowConcurrentExecutions = @AllowConcurrentExecutions
                        WHERE Id = @Id AND TenantId IS NULL
                        """,
                        SqlDataScope.HostOnly),
                    new
                    {
                        Id = definitionId,
                        AllowConcurrentExecutions = allowConcurrentExecutions,
                    },
                    cancellationToken);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static async Task<IReadOnlySet<Guid>> SeedExecutionsAsync(
        FullNetApiFactory factory,
        Guid definitionId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant =
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var command =
                scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var now = DateTimeOffset.UtcNow;
            var insertExecution = new SqlStatement(
                "test.jobs.insert_multi_worker_execution",
                """
                INSERT INTO fn_jobs_execution
                    (Id, TenantId, JobDefinitionId, Status, TriggerKind,
                     ErrorMessage, StartedAtUtc, FinishedAtUtc,
                     LeaseId, LeaseExpiresAtUtc, AttemptCount, CreatedAtUtc)
                VALUES
                    (@Id, NULL, @JobDefinitionId, @Status, @TriggerKind,
                     NULL, NULL, NULL, NULL, NULL, 0, @CreatedAtUtc)
                """,
                SqlDataScope.HostOnly);
            var executionIds = new HashSet<Guid>();
            for (var index = 0; index < ExecutionCount; index++)
            {
                var executionId = Guid.CreateVersion7();
                executionIds.Add(executionId);
                await command.ExecuteAsync(
                    insertExecution,
                    new
                    {
                        Id = executionId,
                        JobDefinitionId = definitionId,
                        Status = JobExecutionStatuses.Pending,
                        TriggerKind = JobTriggerKinds.Manual,
                        CreatedAtUtc = now.AddMilliseconds(index),
                    },
                    cancellationToken);
            }

            return executionIds;
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static async Task<int> ProcessWorkerAsync(
        AsyncServiceScope scope,
        Task startSignal,
        CancellationToken cancellationToken)
    {
        await startSignal.WaitAsync(cancellationToken);
        var currentTenant =
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var runner = scope.ServiceProvider
                .GetRequiredService<JobExecutionRunner>();
            var processedCount = 0;
            for (var batchIndex = 0;
                 batchIndex < ExecutionCount / WorkerBatchSize;
                 batchIndex++)
            {
                var batchCount = await runner.ProcessPendingAsync(
                    WorkerBatchSize,
                    cancellationToken);
                processedCount += batchCount;
                if (batchCount < WorkerBatchSize)
                {
                    break;
                }
            }

            return processedCount;
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static async Task AssertFinalStateAsync(
        FullNetApiFactory factory,
        Guid definitionId,
        IReadOnlySet<Guid> executionIds,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant =
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var executions = await scope.ServiceProvider
                .GetRequiredService<IQueryExecutor>()
                .QueryAsync<ExecutionState>(
                    new SqlStatement(
                        "test.jobs.list_multi_worker_execution_state",
                        """
                        SELECT Id, Status, AttemptCount, LeaseId,
                               LeaseExpiresAtUtc, FinishedAtUtc
                        FROM fn_jobs_execution
                        WHERE TenantId IS NULL
                          AND JobDefinitionId = @JobDefinitionId
                        ORDER BY CreatedAtUtc, Id
                        """,
                        SqlDataScope.HostOnly),
                    new { JobDefinitionId = definitionId },
                    cancellationToken);

            var rows = executions
                .Where(execution => executionIds.Contains(execution.Id))
                .ToArray();
            Assert.HasCount(ExecutionCount, rows);
            Assert.IsTrue(
                rows.All(row => row.Status == JobExecutionStatuses.Succeeded),
                "所有并发领取的任务都必须完成。");
            Assert.IsTrue(
                rows.All(row => row.AttemptCount == 1),
                "同一条任务不得被多个 Worker 重复领取。");
            Assert.IsTrue(
                rows.All(row => row.LeaseId is null
                    && row.LeaseExpiresAtUtc is null
                    && row.FinishedAtUtc is not null),
                "完成状态必须清空租约并记录结束时间。");
        }
        finally
        {
            currentTenant.Clear();
        }
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
