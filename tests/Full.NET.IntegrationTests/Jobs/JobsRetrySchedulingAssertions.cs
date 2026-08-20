using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Features.ManageHostJobExecutions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Jobs;

/// <summary>验证可重试失败在真实数据库中遵守到期领取和尝试上限。</summary>
internal static class JobsRetrySchedulingAssertions
{
    public static void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IJobHandlerExecutor, RetryableFailureJobHandler>();

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>()
            .SetHost();
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var retryScheduledAtUtc = clock.UtcNow.AddSeconds(30);
        var runner = CreateRunner(scope.ServiceProvider, clock);
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var backlogReader = scope.ServiceProvider
            .GetRequiredService<JobsBacklogReader>();
        var baseline = await backlogReader.ReadAsync(
            clock.UtcNow,
            cancellationToken);
        Assert.AreEqual(0, baseline.PendingCount);
        Assert.IsNull(baseline.OldestClaimableCreatedAtUtc);
        Assert.AreEqual(0, baseline.DueRetryCount);
        Assert.IsNull(baseline.OldestDueRetryAtUtc);
        var seeded = await SeedAsync(factory, cancellationToken);
        var initialBacklog = await backlogReader.ReadAsync(
            clock.UtcNow,
            cancellationToken);
        Assert.AreEqual(1, initialBacklog.PendingCount);
        Assert.IsNotNull(initialBacklog.OldestClaimableCreatedAtUtc);
        Assert.AreEqual(0, initialBacklog.DueRetryCount);
        Assert.IsNull(initialBacklog.OldestDueRetryAtUtc);

        Assert.AreEqual(
            1,
            await runner.ProcessPendingAsync(1, cancellationToken));
        var scheduled = await ReadStateAsync(
            query,
            seeded.ExecutionId,
            cancellationToken);
        Assert.AreEqual(JobExecutionStatuses.Pending, scheduled.Status);
        Assert.AreEqual(1, scheduled.AttemptCount);
        Assert.IsNotNull(scheduled.NextAttemptAtUtc);
        Assert.IsTrue(
            Math.Abs(
                (scheduled.NextAttemptAtUtc.Value - retryScheduledAtUtc).Ticks)
            <= TimeSpan.TicksPerMicrosecond,
            "重试到期时间必须在双数据库共同支持的微秒精度内保持一致。");
        Assert.IsNull(scheduled.LeaseId);
        Assert.IsNull(scheduled.LeaseExpiresAtUtc);
        Assert.IsNull(scheduled.FinishedAtUtc);
        Assert.IsFalse(string.IsNullOrWhiteSpace(scheduled.ErrorMessage));
        var queryResult = await scope.ServiceProvider
            .GetRequiredService<HostJobExecutionQueryService>()
            .GetByIdAsync(seeded.ExecutionId, cancellationToken);
        Assert.IsTrue(queryResult.IsSuccess);
        Assert.IsNotNull(queryResult.Value);
        Assert.AreEqual(
            scheduled.NextAttemptAtUtc,
            queryResult.Value.NextAttemptAtUtc);
        var scheduledBacklog = await backlogReader.ReadAsync(
            clock.UtcNow,
            cancellationToken);
        Assert.AreEqual(1, scheduledBacklog.PendingCount);
        Assert.IsNull(scheduledBacklog.OldestClaimableCreatedAtUtc);
        Assert.AreEqual(0, scheduledBacklog.DueRetryCount);
        Assert.IsNull(scheduledBacklog.OldestDueRetryAtUtc);

        Assert.AreEqual(
            0,
            await runner.ProcessPendingAsync(1, cancellationToken));

        clock.UtcNow = retryScheduledAtUtc.AddSeconds(1);
        var dueBacklog = await backlogReader.ReadAsync(
            clock.UtcNow,
            cancellationToken);
        Assert.AreEqual(1, dueBacklog.PendingCount);
        Assert.IsNotNull(dueBacklog.OldestClaimableCreatedAtUtc);
        Assert.AreEqual(1, dueBacklog.DueRetryCount);
        Assert.IsNotNull(dueBacklog.OldestDueRetryAtUtc);
        Assert.IsTrue(
            Math.Abs(
                (dueBacklog.OldestDueRetryAtUtc.Value
                    - retryScheduledAtUtc).Ticks)
            <= TimeSpan.TicksPerMicrosecond,
            "最老到期重试时间必须在双数据库共同支持的微秒精度内保持一致。");

        Assert.AreEqual(
            1,
            await runner.ProcessPendingAsync(1, cancellationToken));
        var secondFailureTime = clock.UtcNow;
        var secondRetryScheduledAtUtc = secondFailureTime.AddSeconds(60);
        var secondScheduled = await ReadStateAsync(
            query,
            seeded.ExecutionId,
            cancellationToken);
        Assert.AreEqual(
            JobExecutionStatuses.Pending,
            secondScheduled.Status);
        Assert.AreEqual(2, secondScheduled.AttemptCount);
        Assert.IsNotNull(secondScheduled.NextAttemptAtUtc);
        Assert.IsTrue(
            Math.Abs(
                (secondScheduled.NextAttemptAtUtc.Value
                    - secondRetryScheduledAtUtc).Ticks)
            <= TimeSpan.TicksPerMicrosecond,
            "第二次失败必须按指数退避延迟六十秒。");
        Assert.IsNull(secondScheduled.LeaseId);
        Assert.IsNull(secondScheduled.LeaseExpiresAtUtc);
        Assert.IsNull(secondScheduled.FinishedAtUtc);
        Assert.IsFalse(
            string.IsNullOrWhiteSpace(secondScheduled.ErrorMessage));

        clock.UtcNow = secondRetryScheduledAtUtc.AddSeconds(1);
        Assert.AreEqual(
            1,
            await runner.ProcessPendingAsync(1, cancellationToken));
        var exhausted = await ReadStateAsync(
            query,
            seeded.ExecutionId,
            cancellationToken);
        Assert.AreEqual(JobExecutionStatuses.Failed, exhausted.Status);
        Assert.AreEqual(3, exhausted.AttemptCount);
        Assert.IsNull(exhausted.NextAttemptAtUtc);
        Assert.IsNull(exhausted.LeaseId);
        Assert.IsNull(exhausted.LeaseExpiresAtUtc);
        Assert.IsNotNull(exhausted.FinishedAtUtc);
        Assert.IsFalse(string.IsNullOrWhiteSpace(exhausted.ErrorMessage));
        var emptyBacklog = await backlogReader.ReadAsync(
            clock.UtcNow,
            cancellationToken);
        Assert.AreEqual(0, emptyBacklog.PendingCount);
        Assert.IsNull(emptyBacklog.OldestClaimableCreatedAtUtc);
        Assert.AreEqual(0, emptyBacklog.DueRetryCount);
        Assert.IsNull(emptyBacklog.OldestDueRetryAtUtc);
    }

    private static JobExecutionRunner CreateRunner(
        IServiceProvider services,
        IClock clock) =>
        new(
            services.GetRequiredService<IQueryExecutor>(),
            services.GetRequiredService<ICommandExecutor>(),
            services.GetRequiredService<ICommandTransaction>(),
            services.GetRequiredService<JobHandlerKindRegistry>(),
            clock,
            services.GetRequiredService<IIdGenerator>(),
            services.GetRequiredService<IOptions<DatabaseOptions>>(),
            Options.Create(
                new JobsWorkerOptions
                {
                    MaxAttempts = 3,
                    RetryDelaySeconds = 30,
                    RetryBackoffMode = "exponential",
                    RetryMaxDelaySeconds = 86400,
                    RetryJitterPercent = 0,
                }),
            NullLogger<JobExecutionRunner>.Instance);

    private static async Task<SeededExecution> SeedAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>()
            .SetHost();
        var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var definitionId = Guid.CreateVersion7();
        var executionId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await command.ExecuteAsync(
            new SqlStatement(
                "test.jobs.insert_retry_definition",
                """
                INSERT INTO fn_jobs_definition
                    (Id, TenantId, JobKey, HandlerKind, ArgsJson, DisplayName, Description, IsEnabled,
                     CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId,
                     Version)
                VALUES
                    (@Id, NULL, @JobKey, @HandlerKind, NULL, @DisplayName, NULL, @IsEnabled,
                     @CreatedAtUtc, NULL, @CreatedByUserId, NULL, 1)
                """,
                SqlDataScope.HostOnly),
            new
            {
                Id = definitionId,
                JobKey = RetryableFailureJobHandler.Key,
                HandlerKind = RetryableFailureJobHandler.Key,
                DisplayName = "集成测试可重试任务",
                IsEnabled = true,
                CreatedAtUtc = now,
                CreatedByUserId = Guid.CreateVersion7(),
            },
            cancellationToken);
        await command.ExecuteAsync(
            new SqlStatement(
                "test.jobs.insert_retry_execution",
                """
                INSERT INTO fn_jobs_execution
                    (Id, TenantId, JobDefinitionId, Status, TriggerKind,
                     ErrorMessage, StartedAtUtc, FinishedAtUtc,
                     LeaseId, LeaseExpiresAtUtc, NextAttemptAtUtc,
                     AttemptCount, CreatedAtUtc)
                VALUES
                    (@Id, NULL, @JobDefinitionId, @Status, @TriggerKind,
                     NULL, NULL, NULL, NULL, NULL, NULL, 0, @CreatedAtUtc)
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
        return new SeededExecution(executionId);
    }

    private static async Task<RetryExecutionState> ReadStateAsync(
        IQueryExecutor query,
        Guid executionId,
        CancellationToken cancellationToken) =>
        await query.QuerySingleOrDefaultAsync<RetryExecutionState>(
            new SqlStatement(
                "test.jobs.find_retry_execution_state",
                """
                SELECT Status, ErrorMessage, AttemptCount, NextAttemptAtUtc,
                       LeaseId, LeaseExpiresAtUtc, FinishedAtUtc
                FROM fn_jobs_execution
                WHERE Id = @Id AND TenantId IS NULL
                """,
                SqlDataScope.HostOnly),
            new { Id = executionId },
            cancellationToken)
        ?? throw new InvalidOperationException(
            "Retry execution state was not found.");

    private sealed class RetryableFailureJobHandler : IJobHandlerExecutor
    {
        public const string Key = "jobs.test-retry-scheduling";

        public string HandlerKind => Key;

        public Task ExecuteAsync(
            JobExecutionContext context,
            CancellationToken cancellationToken) =>
            throw new RetryableJobException("transient integration failure");
    }

    private sealed record SeededExecution(Guid ExecutionId);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class RetryExecutionState
    {
        public string Status { get; set; } = string.Empty;

        public string? ErrorMessage { get; set; }

        public int AttemptCount { get; set; }

        public DateTimeOffset? NextAttemptAtUtc { get; set; }

        public Guid? LeaseId { get; set; }

        public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

        public DateTimeOffset? FinishedAtUtc { get; set; }
    }
}
