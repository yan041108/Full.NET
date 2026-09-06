using System.Net;
using System.Net.Http.Json;
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

/// <summary>验证运行中/待处理任务取消请求、Worker 协作取消与终态一致性。</summary>
internal static class JobsExecutionCancelAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await VerifyCancelPendingExecutionAsync(factory, cancellationToken);
        await VerifyCancelRunningExecutionAsync(factory, cancellationToken);
        await VerifyCancelTerminalExecutionIsRejectedAsync(factory, cancellationToken);
    }

    private static async Task VerifyCancelPendingExecutionAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await JobsHostDefinitionAssertions.LoginAsHostAdminAsync(
            client,
            cancellationToken);
        var executionId = Guid.CreateVersion7();
        var definitionId = Guid.CreateVersion7();
        await SeedDefinitionAndPendingExecutionAsync(
            factory,
            definitionId,
            executionId,
            cancellationToken);

        using var cancelRequest = JobsHostDefinitionAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/jobs/host-executions/{executionId:D}/cancel",
            adminToken,
            new { });
        using var cancelResponse = await client.SendAsync(cancelRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<HostJobExecutionResponse>(
            cancellationToken);
        Assert.IsNotNull(cancelled);
        Assert.AreEqual(JobExecutionStatuses.Cancelled, cancelled.Status);
        Assert.IsNotNull(cancelled.FinishedAtUtc);
    }

    private static async Task VerifyCancelRunningExecutionAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(20));
        var testToken = timeoutSource.Token;
        var definitionId = Guid.CreateVersion7();
        var executionId = Guid.CreateVersion7();
        await SeedDefinitionAndPendingExecutionAsync(
            factory,
            definitionId,
            executionId,
            testToken,
            CancelBlockingJobHandler.Key);

        await using var workerScope = factory.Services.CreateAsyncScope();
        workerScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var handler = new CancelBlockingJobHandler();
        var worker = CreateRunner(
            workerScope.ServiceProvider,
            new JobHandlerKindRegistry([handler]));
        var workerTask = worker.ProcessPendingAsync(1, testToken);
        await handler.Started.WaitAsync(testToken);

        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await JobsHostDefinitionAssertions.LoginAsHostAdminAsync(
            client,
            testToken);
        using var cancelRequest = JobsHostDefinitionAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/jobs/host-executions/{executionId:D}/cancel",
            adminToken,
            new { });
        using var cancelResponse = await client.SendAsync(cancelRequest, testToken);
        Assert.AreEqual(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelling = await cancelResponse.Content.ReadFromJsonAsync<HostJobExecutionResponse>(
            testToken);
        Assert.IsNotNull(cancelling);
        Assert.AreEqual(JobExecutionStatuses.Cancelling, cancelling.Status);

        Assert.AreEqual(1, await workerTask.WaitAsync(testToken));
        var final = await WaitForStateAsync(
            factory.Services,
            executionId,
            state => state.Status == JobExecutionStatuses.Cancelled,
            testToken);
        Assert.IsNotNull(final.FinishedAtUtc);
    }

    private static async Task VerifyCancelTerminalExecutionIsRejectedAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await JobsHostDefinitionAssertions.LoginAsHostAdminAsync(
            client,
            cancellationToken);
        var executionId = Guid.CreateVersion7();
        var definitionId = Guid.CreateVersion7();
        await SeedDefinitionAndPendingExecutionAsync(
            factory,
            definitionId,
            executionId,
            cancellationToken,
            status: JobExecutionStatuses.Succeeded,
            finishedAtUtc: DateTimeOffset.UtcNow);

        using var cancelRequest = JobsHostDefinitionAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/jobs/host-executions/{executionId:D}/cancel",
            adminToken,
            new { });
        using var cancelResponse = await client.SendAsync(cancelRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, cancelResponse.StatusCode);
    }

    private static async Task SeedDefinitionAndPendingExecutionAsync(
        FullNetApiFactory factory,
        Guid definitionId,
        Guid executionId,
        CancellationToken cancellationToken,
        string handlerKind = JobHandlerKinds.Ping,
        string status = JobExecutionStatuses.Pending,
        DateTimeOffset? finishedAtUtc = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var now = DateTimeOffset.UtcNow;
            await command.ExecuteAsync(
                new SqlStatement(
                    "test.jobs.insert_cancel_definition",
                    """
                    INSERT INTO fn_jobs_definition
                        (Id, TenantId, JobKey, HandlerKind, ArgsJson, DisplayName, Description,
                         GroupName, IsEnabled, AllowConcurrentExecutions,
                         CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UpdatedByUserId, Version)
                    VALUES
                        (@Id, NULL, @JobKey, @HandlerKind, NULL, @DisplayName, NULL,
                         NULL, 1, 1,
                         @CreatedAtUtc, NULL, @CreatedByUserId, NULL, 1)
                    """,
                    SqlDataScope.HostOnly),
                new
                {
                    Id = definitionId,
                    JobKey = $"jobs.cancel.{definitionId:D}",
                    HandlerKind = handlerKind,
                    DisplayName = "Cancel test job",
                    CreatedAtUtc = now,
                    CreatedByUserId = Guid.CreateVersion7(),
                },
                cancellationToken);
            await command.ExecuteAsync(
                new SqlStatement(
                    "test.jobs.insert_cancel_execution",
                    """
                    INSERT INTO fn_jobs_execution
                        (Id, TenantId, JobDefinitionId, Status, TriggerKind,
                         ErrorMessage, StartedAtUtc, FinishedAtUtc,
                         LeaseId, LeaseExpiresAtUtc, AttemptCount, CreatedAtUtc)
                    VALUES
                        (@Id, NULL, @JobDefinitionId, @Status, @TriggerKind,
                         NULL, @StartedAtUtc, @FinishedAtUtc,
                         NULL, NULL, @AttemptCount, @CreatedAtUtc)
                    """,
                    SqlDataScope.HostOnly),
                new
                {
                    Id = executionId,
                    JobDefinitionId = definitionId,
                    Status = status,
                    TriggerKind = JobTriggerKinds.Manual,
                    StartedAtUtc = status == JobExecutionStatuses.Succeeded
                        ? (DateTimeOffset?)now
                        : null,
                    FinishedAtUtc = finishedAtUtc,
                    AttemptCount = status == JobExecutionStatuses.Succeeded ? 1 : 0,
                    CreatedAtUtc = now,
                },
                cancellationToken);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static JobExecutionRunner CreateRunner(
        IServiceProvider services,
        JobHandlerKindRegistry registry) =>
        new(
            services.GetRequiredService<IQueryExecutor>(),
            services.GetRequiredService<ICommandExecutor>(),
            services.GetRequiredService<ICommandTransaction>(),
            registry,
            services.GetRequiredService<Full.NET.Abstractions.Time.IClock>(),
            services.GetRequiredService<Full.NET.Abstractions.Ids.IIdGenerator>(),
            services.GetRequiredService<IOptions<DatabaseOptions>>(),
            Options.Create(new JobsWorkerOptions
            {
                LeaseSeconds = 4,
                LeaseRenewalSeconds = 1,
                MaxConcurrency = 1,
            }),
            NullLogger<JobExecutionRunner>.Instance,
            services.GetService<IServiceScopeFactory>());

    private static async Task<ExecutionState> WaitForStateAsync(
        IServiceProvider services,
        Guid executionId,
        Func<ExecutionState, bool> predicate,
        CancellationToken cancellationToken)
    {
        var query = services.GetRequiredService<IQueryExecutor>();
        var statement = new SqlStatement(
            "test.jobs.find_cancel_execution_state",
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

    private sealed class CancelBlockingJobHandler : IJobHandlerExecutor
    {
        public const string Key = "jobs.test-cancel-blocking";

        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string HandlerKind => Key;

        public Task Started => _started.Task;

        public async Task ExecuteAsync(
            JobExecutionContext context,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
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
