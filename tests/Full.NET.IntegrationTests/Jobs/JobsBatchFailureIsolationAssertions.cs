using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Jobs;

/// <summary>验证同一领取批次中的失败任务不会阻断后续健康任务。</summary>
internal static class JobsBatchFailureIsolationAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        Guid succeededDefinitionId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant =
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var command =
                scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var failedDefinitionId = Guid.CreateVersion7();
            var failedExecutionId = Guid.CreateVersion7();
            var succeededExecutionId = Guid.CreateVersion7();
            var actorId = Guid.CreateVersion7();
            var now = DateTimeOffset.UtcNow;

            const string missingHandlerKind = "missing";
            await command.ExecuteAsync(
                new SqlStatement(
                    "test.jobs.insert_batch_failure_definition",
                    """
                    INSERT INTO fn_jobs_definition
                        (Id, TenantId, JobKey, HandlerKind, DisplayName, Description, IsEnabled,
                         CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version)
                    VALUES
                        (@Id, NULL, @JobKey, @HandlerKind, @DisplayName, NULL, @IsEnabled,
                         @CreatedAtUtc, NULL, @CreatedByUserId, NULL, 1)
                    """,
                    SqlDataScope.HostOnly),
                new
                {
                    Id = failedDefinitionId,
                    JobKey = $"jobs.missing-handler.{Guid.NewGuid():N}",
                    HandlerKind = missingHandlerKind,
                    DisplayName = "集成测试缺失处理器任务",
                    IsEnabled = true,
                    CreatedAtUtc = now.AddSeconds(-2),
                    CreatedByUserId = actorId,
                },
                cancellationToken);

            var insertExecution = new SqlStatement(
                "test.jobs.insert_batch_failure_execution",
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
            await command.ExecuteAsync(
                insertExecution,
                new
                {
                    Id = failedExecutionId,
                    JobDefinitionId = failedDefinitionId,
                    Status = JobExecutionStatuses.Pending,
                    TriggerKind = JobTriggerKinds.Manual,
                    CreatedAtUtc = now.AddSeconds(-2),
                },
                cancellationToken);
            await command.ExecuteAsync(
                insertExecution,
                new
                {
                    Id = succeededExecutionId,
                    JobDefinitionId = succeededDefinitionId,
                    Status = JobExecutionStatuses.Pending,
                    TriggerKind = JobTriggerKinds.Manual,
                    CreatedAtUtc = now.AddSeconds(-1),
                },
                cancellationToken);

            var processed = await scope.ServiceProvider
                .GetRequiredService<JobExecutionRunner>()
                .ProcessPendingAsync(2, cancellationToken);
            var executions = await scope.ServiceProvider
                .GetRequiredService<IQueryExecutor>()
                .QueryAsync<ExecutionState>(
                    new SqlStatement(
                        "test.jobs.list_batch_failure_execution_state",
                        """
                        SELECT Id, Status, ErrorMessage, AttemptCount,
                               LeaseId, LeaseExpiresAtUtc, FinishedAtUtc
                        FROM fn_jobs_execution
                        WHERE TenantId IS NULL
                          AND (Id = @FailedExecutionId OR Id = @SucceededExecutionId)
                        ORDER BY CreatedAtUtc, Id
                        """,
                        SqlDataScope.HostOnly),
                    new
                    {
                        FailedExecutionId = failedExecutionId,
                        SucceededExecutionId = succeededExecutionId,
                    },
                    cancellationToken);

            Assert.AreEqual(2, processed);
            Assert.HasCount(2, executions);
            var failed = executions.Single(execution => execution.Id == failedExecutionId);
            var succeeded = executions.Single(
                execution => execution.Id == succeededExecutionId);

            Assert.AreEqual(JobExecutionStatuses.Failed, failed.Status);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failed.ErrorMessage));
            Assert.AreEqual(JobExecutionStatuses.Succeeded, succeeded.Status);
            Assert.IsNull(succeeded.ErrorMessage);
            Assert.IsTrue(
                executions.All(execution =>
                    execution.AttemptCount == 1
                    && execution.LeaseId is null
                    && execution.LeaseExpiresAtUtc is null
                    && execution.FinishedAtUtc is not null),
                "失败与成功任务都必须终结本次租约，并且只领取一次。");
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

        public string? ErrorMessage { get; set; }

        public int AttemptCount { get; set; }

        public Guid? LeaseId { get; set; }

        public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

        public DateTimeOffset? FinishedAtUtc { get; set; }
    }
}
