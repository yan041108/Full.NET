using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Jobs;

/// <summary>
/// 验证 AllowConcurrentExecutions=false 时，集群内同定义最多一条有效 running，
/// 且 Acquire 不会在已有 running 时领取额外 pending。
/// </summary>
internal static class JobsOverlapControlAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        Guid definitionId,
        CancellationToken cancellationToken = default)
    {
        await JobsMultiWorkerClaimAssertions.SetAllowConcurrentExecutionsForTestAsync(
            factory,
            definitionId,
            allowConcurrentExecutions: false,
            cancellationToken);

        await VerifyActiveRunningBlocksPendingAcquireAsync(
            factory,
            definitionId,
            cancellationToken);
        await VerifySingleBatchClaimsOnePendingAsync(
            factory,
            definitionId,
            cancellationToken);
    }

    private static async Task VerifyActiveRunningBlocksPendingAcquireAsync(
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
            await ClearExecutionsAsync(scope, definitionId, cancellationToken);
            var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var runner = scope.ServiceProvider.GetRequiredService<JobExecutionRunner>();
            var now = DateTimeOffset.UtcNow;
            var runningId = Guid.CreateVersion7();
            var pendingId = Guid.CreateVersion7();

            await InsertExecutionAsync(
                command,
                runningId,
                definitionId,
                JobExecutionStatuses.Running,
                now.AddMinutes(-1),
                Guid.CreateVersion7(),
                now.AddMinutes(5),
                now.AddMinutes(-1),
                cancellationToken);
            await InsertExecutionAsync(
                command,
                pendingId,
                definitionId,
                JobExecutionStatuses.Pending,
                null,
                null,
                null,
                now,
                cancellationToken);

            var processed = await runner.ProcessPendingAsync(4, cancellationToken);
            var pendingStatus = await query.QuerySingleOrDefaultAsync<string>(
                new SqlStatement(
                    "test.jobs.overlap_pending_status",
                    """
                    SELECT Status
                    FROM fn_jobs_execution
                    WHERE Id = @Id AND TenantId IS NULL
                    """,
                    SqlDataScope.HostOnly),
                new { Id = pendingId },
                cancellationToken);

            Assert.AreEqual(0, processed);
            Assert.AreEqual(JobExecutionStatuses.Pending, pendingStatus);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static async Task VerifySingleBatchClaimsOnePendingAsync(
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
            await ClearExecutionsAsync(scope, definitionId, cancellationToken);
            var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var runner = scope.ServiceProvider.GetRequiredService<JobExecutionRunner>();
            var now = DateTimeOffset.UtcNow;
            var firstPendingId = Guid.CreateVersion7();
            var secondPendingId = Guid.CreateVersion7();

            await InsertExecutionAsync(
                command,
                firstPendingId,
                definitionId,
                JobExecutionStatuses.Pending,
                null,
                null,
                null,
                now,
                cancellationToken);
            await InsertExecutionAsync(
                command,
                secondPendingId,
                definitionId,
                JobExecutionStatuses.Pending,
                null,
                null,
                null,
                now.AddMilliseconds(1),
                cancellationToken);

            var processed = await runner.ProcessPendingAsync(4, cancellationToken);
            var statusCounts = await query.QueryAsync<ExecutionStatusRow>(
                new SqlStatement(
                    "test.jobs.overlap_status_after_first_batch",
                    """
                    SELECT Id, Status
                    FROM fn_jobs_execution
                    WHERE TenantId IS NULL
                      AND Id IN @Ids
                    """,
                    SqlDataScope.HostOnly),
                new { Ids = new[] { firstPendingId, secondPendingId } },
                cancellationToken);

            Assert.AreEqual(1, processed);
            Assert.AreEqual(
                1,
                statusCounts.Count(row => row.Status == JobExecutionStatuses.Succeeded));
            Assert.AreEqual(
                1,
                statusCounts.Count(row => row.Status == JobExecutionStatuses.Pending));

            await runner.ProcessPendingAsync(4, cancellationToken);
            var finalStates = await query.QueryAsync<ExecutionStatusRow>(
                new SqlStatement(
                    "test.jobs.overlap_final_states",
                    """
                    SELECT Id, Status
                    FROM fn_jobs_execution
                    WHERE TenantId IS NULL
                      AND Id IN @Ids
                    """,
                    SqlDataScope.HostOnly),
                new { Ids = new[] { firstPendingId, secondPendingId } },
                cancellationToken);

            Assert.IsTrue(
                finalStates.All(row => row.Status == JobExecutionStatuses.Succeeded),
                "两条 pending 必须按顺序完成，不得同时 running。");
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static Task ClearExecutionsAsync(
        AsyncServiceScope scope,
        Guid definitionId,
        CancellationToken cancellationToken) =>
        scope.ServiceProvider.GetRequiredService<ICommandExecutor>()
            .ExecuteAsync(
                new SqlStatement(
                    "test.jobs.clear_overlap_executions",
                    """
                    DELETE FROM fn_jobs_execution
                    WHERE TenantId IS NULL
                      AND JobDefinitionId = @JobDefinitionId
                    """,
                    SqlDataScope.HostOnly),
                new { JobDefinitionId = definitionId },
                cancellationToken);

    private static Task InsertExecutionAsync(
        ICommandExecutor command,
        Guid id,
        Guid definitionId,
        string status,
        DateTimeOffset? startedAtUtc,
        Guid? leaseId,
        DateTimeOffset? leaseExpiresAtUtc,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken) =>
        command.ExecuteAsync(
            new SqlStatement(
                "test.jobs.insert_overlap_execution",
                """
                INSERT INTO fn_jobs_execution
                    (Id, TenantId, JobDefinitionId, Status, TriggerKind,
                     ErrorMessage, StartedAtUtc, FinishedAtUtc,
                     LeaseId, LeaseExpiresAtUtc, NextAttemptAtUtc,
                     AttemptCount, CreatedAtUtc)
                VALUES
                    (@Id, NULL, @JobDefinitionId, @Status, @TriggerKind,
                     NULL, @StartedAtUtc, NULL,
                     @LeaseId, @LeaseExpiresAtUtc, NULL, @AttemptCount, @CreatedAtUtc)
                """,
                SqlDataScope.HostOnly),
            new
            {
                Id = id,
                JobDefinitionId = definitionId,
                Status = status,
                TriggerKind = JobTriggerKinds.Manual,
                StartedAtUtc = startedAtUtc,
                LeaseId = leaseId,
                LeaseExpiresAtUtc = leaseExpiresAtUtc,
                AttemptCount = status == JobExecutionStatuses.Running ? 1 : 0,
                CreatedAtUtc = createdAtUtc,
            },
            cancellationToken);

    private sealed class ExecutionStatusRow
    {
        public Guid Id { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}
