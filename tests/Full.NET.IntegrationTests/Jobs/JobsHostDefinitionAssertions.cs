using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Jobs;

/// <summary>Host 任务定义与手动触发纵向切片验收夹具。</summary>
internal static class JobsHostDefinitionAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        JobsBoundedConcurrencyProbe? concurrencyProbe = null,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        var lifecycle = await VerifyCreateTriggerAndExecutionLifecycleAsync(
            client,
            cancellationToken);
        await JobsExecutionHistoryAssertions.VerifyAsync(
            factory,
            client,
            lifecycle.Definition,
            lifecycle.Execution,
            cancellationToken);
        var definition = await VerifyExactJobDefinitionActionPermissionBoundariesAsync(
            factory,
            client,
            lifecycle.Definition,
            cancellationToken);
        await JobsMultiWorkerClaimAssertions.VerifyAsync(
            factory,
            definition.Id,
            cancellationToken);
        await JobsOverlapControlAssertions.VerifyAsync(
            factory,
            definition.Id,
            cancellationToken);
        await JobsBatchFailureIsolationAssertions.VerifyAsync(
            factory,
            definition.Id,
            cancellationToken);
        await JobsRetrySchedulingAssertions.VerifyAsync(
            factory,
            cancellationToken);
        await JobsActiveLeaseRenewalAssertions.VerifyAsync(
            factory,
            cancellationToken);
        await JobsScheduleAssertions.VerifyAsync(
            factory,
            client,
            definition,
            cancellationToken);
        if (concurrencyProbe is not null)
        {
            await JobsBoundedConcurrencyAssertions.VerifyAsync(
                factory,
                concurrencyProbe,
                cancellationToken);
        }

        await VerifyDisableAsync(
            client,
            definition.Id,
            definition.Version,
            cancellationToken);
        await VerifyExpiredRunningExecutionIsReclaimedAsync(
            factory,
            definition.Id,
            cancellationToken);
        await OpenApiJobsHostDefinitionsContractAssertions.VerifyAsync(client, cancellationToken);
        await JobsHealthReadonlyAssertions.VerifyAsync(factory, client, cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/jobs/host-definitions?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await factory.CreateHostAccessTokenAsync(
                ["platform.dashboard.read"],
                cancellationToken));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<(HostJobDefinitionResponse Definition, HostJobExecutionResponse Execution)> VerifyCreateTriggerAndExecutionLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var displayName = $"集成任务-{Guid.NewGuid():N}"[..20];

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/jobs/host-definitions",
            adminToken,
            new CreateHostJobDefinitionRequest(
                JobsWellKnownKeys.Ping,
                displayName,
                "集成测试任务",
                null));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostJobDefinitionResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(JobsWellKnownKeys.Ping, created.JobKey);
        Assert.IsTrue(created.IsEnabled);
        Assert.IsFalse(created.AllowConcurrentExecutions);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/jobs/host-definitions/{created.Id:D}",
            adminToken,
            new UpdateHostJobDefinitionRequest(
                "更新后名称",
                "更新后描述",
                null,
                false,
                created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<HostJobDefinitionResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("更新后名称", updated.DisplayName);
        Assert.AreEqual(created.Version + 1, updated.Version);

        using var triggerRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{created.Id:D}/trigger",
            adminToken,
            new { });
        using var triggerResponse = await client.SendAsync(triggerRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, triggerResponse.StatusCode);
        var execution = await triggerResponse.Content.ReadFromJsonAsync<HostJobExecutionResponse>(
            cancellationToken);
        Assert.IsNotNull(execution);
        Assert.AreEqual(JobExecutionStatuses.Succeeded, execution.Status);
        Assert.AreEqual(JobTriggerKinds.Manual, execution.TriggerKind);
        Assert.IsNotNull(execution.FinishedAtUtc);

        using var executionsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/jobs/host-executions?page=1&pageSize=20&jobDefinitionId={created.Id:D}");
        executionsRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var executionsResponse = await client.SendAsync(executionsRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, executionsResponse.StatusCode);
        var page = await executionsResponse.Content.ReadFromJsonAsync<PagedHostJobExecutionResponses>(
            cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == execution.Id));

        using var invalidKeyRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/jobs/host-definitions",
            adminToken,
            new CreateHostJobDefinitionRequest(
                "jobs.invalid-key",
                "无效键",
                null,
                null));
        using var invalidKeyResponse = await client.SendAsync(invalidKeyRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidKeyResponse.StatusCode);
        using var invalidKeyProblem = JsonDocument.Parse(
            await invalidKeyResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            JobsErrorCodes.DefinitionValidationFailed,
            invalidKeyProblem.RootElement.GetProperty("code").GetString());

        return (updated, execution);
    }

    private static async Task<HostJobDefinitionResponse> VerifyExactJobDefinitionActionPermissionBoundariesAsync(
        FullNetApiFactory factory,
        HttpClient client,
        HostJobDefinitionResponse definition,
        CancellationToken cancellationToken)
    {
        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [HostJobPermissions.DefinitionsRead],
            cancellationToken);
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/jobs/host-definitions?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            readOnlyToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            "/api/v1/jobs/host-definitions",
            cancellationToken,
            new CreateHostJobDefinitionRequest(JobsWellKnownKeys.Ping, "拒绝", null, null));
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}",
            cancellationToken,
            new UpdateHostJobDefinitionRequest("拒绝", null, null, false, definition.Version));
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}/disable",
            cancellationToken,
            new DisableHostJobDefinitionRequest(definition.Version));
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}/trigger",
            cancellationToken,
            new { });

        var createOnlyToken = await factory.CreateHostAccessTokenAsync(
            [
                HostJobPermissions.DefinitionsRead,
                HostJobPermissions.DefinitionsCreate,
            ],
            cancellationToken);
        using var createOnlyRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/jobs/host-definitions",
            createOnlyToken,
            new CreateHostJobDefinitionRequest(JobsWellKnownKeys.Ping, "重复键", null, null));
        using var createOnlyResponse = await client.SendAsync(createOnlyRequest, cancellationToken);
        Assert.AreNotEqual(HttpStatusCode.Forbidden, createOnlyResponse.StatusCode);
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            createOnlyToken,
            HttpMethod.Put,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}",
            cancellationToken,
            new UpdateHostJobDefinitionRequest("拒绝", null, null, false, definition.Version));
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            createOnlyToken,
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}/trigger",
            cancellationToken,
            new { });
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            createOnlyToken,
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}/disable",
            cancellationToken,
            new DisableHostJobDefinitionRequest(definition.Version));

        var updateOnlyToken = await factory.CreateHostAccessTokenAsync(
            [
                HostJobPermissions.DefinitionsRead,
                HostJobPermissions.DefinitionsUpdate,
            ],
            cancellationToken);
        using var updateOnlyRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}",
            updateOnlyToken,
            new UpdateHostJobDefinitionRequest("边界更新", "正文", null, false, definition.Version));
        using var updateOnlyResponse = await client.SendAsync(updateOnlyRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateOnlyResponse.StatusCode);
        var updated = await updateOnlyResponse.Content.ReadFromJsonAsync<HostJobDefinitionResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            updateOnlyToken,
            HttpMethod.Post,
            "/api/v1/jobs/host-definitions",
            cancellationToken,
            new CreateHostJobDefinitionRequest(JobsWellKnownKeys.Ping, "拒绝", null, null));
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            updateOnlyToken,
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}/trigger",
            cancellationToken,
            new { });
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            updateOnlyToken,
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}/disable",
            cancellationToken,
            new DisableHostJobDefinitionRequest(updated.Version));

        var triggerOnlyToken = await factory.CreateHostAccessTokenAsync(
            [
                HostJobPermissions.DefinitionsRead,
                HostJobPermissions.DefinitionsTrigger,
            ],
            cancellationToken);
        using var triggerOnlyRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}/trigger",
            triggerOnlyToken,
            new { });
        using var triggerOnlyResponse = await client.SendAsync(triggerOnlyRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, triggerOnlyResponse.StatusCode);
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            triggerOnlyToken,
            HttpMethod.Post,
            "/api/v1/jobs/host-definitions",
            cancellationToken,
            new CreateHostJobDefinitionRequest(JobsWellKnownKeys.Ping, "拒绝", null, null));
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            triggerOnlyToken,
            HttpMethod.Put,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}",
            cancellationToken,
            new UpdateHostJobDefinitionRequest("拒绝", null, null, false, updated.Version));
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            triggerOnlyToken,
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}/disable",
            cancellationToken,
            new DisableHostJobDefinitionRequest(updated.Version));

        var disableOnlyToken = await factory.CreateHostAccessTokenAsync(
            [
                HostJobPermissions.DefinitionsRead,
                HostJobPermissions.DefinitionsDisable,
            ],
            cancellationToken);
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            disableOnlyToken,
            HttpMethod.Post,
            "/api/v1/jobs/host-definitions",
            cancellationToken,
            new CreateHostJobDefinitionRequest(JobsWellKnownKeys.Ping, "拒绝", null, null));
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            disableOnlyToken,
            HttpMethod.Put,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}",
            cancellationToken,
            new UpdateHostJobDefinitionRequest("拒绝", null, null, false, updated.Version));
        await AssertJobDefinitionPermissionDeniedAsync(
            client,
            disableOnlyToken,
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{definition.Id:D}/trigger",
            cancellationToken,
            new { });

        return updated;
    }

    private static async Task AssertJobDefinitionPermissionDeniedAsync<TRequest>(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        TRequest body)
    {
        using var request = CreateBearerJsonRequest(method, path, accessToken, body);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyDisableAsync(
        HttpClient client,
        Guid definitionId,
        int version,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{definitionId:D}/disable",
            adminToken,
            new DisableHostJobDefinitionRequest(version));
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<HostJobDefinitionResponse>(
            cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsEnabled);

        using var disabledTriggerRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/jobs/host-definitions/{definitionId:D}/trigger",
            adminToken,
            new { });
        using var disabledTriggerResponse = await client.SendAsync(
            disabledTriggerRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, disabledTriggerResponse.StatusCode);
    }

    private sealed record PagedHostJobExecutionResponses(
        HostJobExecutionResponse[] Items,
        int Page,
        int PageSize,
        long Total);

    private static async Task VerifyExpiredRunningExecutionIsReclaimedAsync(
        FullNetApiFactory factory,
        Guid definitionId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var executionId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await command.ExecuteAsync(
            new SqlStatement(
                "test.insert_expired_running_job_execution",
                """
                INSERT INTO fn_jobs_execution
                    (Id, TenantId, JobDefinitionId, Status, TriggerKind,
                     ErrorMessage, StartedAtUtc, FinishedAtUtc,
                     LeaseId, LeaseExpiresAtUtc, AttemptCount, CreatedAtUtc)
                VALUES
                    (@Id, NULL, @JobDefinitionId, @Status, @TriggerKind,
                     NULL, @StartedAtUtc, NULL,
                     @LeaseId, @LeaseExpiresAtUtc, 1, @CreatedAtUtc)
                """,
                SqlDataScope.HostOnly),
            new
            {
                Id = executionId,
                JobDefinitionId = definitionId,
                Status = JobExecutionStatuses.Running,
                TriggerKind = JobTriggerKinds.Manual,
                StartedAtUtc = now.AddMinutes(-10),
                LeaseId = Guid.CreateVersion7(),
                LeaseExpiresAtUtc = now.AddMinutes(-5),
                CreatedAtUtc = now.AddMinutes(-10),
            },
            cancellationToken);

        var processed = await scope.ServiceProvider
            .GetRequiredService<JobExecutionRunner>()
            .ProcessPendingAsync(1, cancellationToken);
        var recovered = await query.QuerySingleOrDefaultAsync<RecoveredExecution>(
            new SqlStatement(
                "test.find_recovered_job_execution",
                """
                SELECT Status, AttemptCount, FinishedAtUtc
                FROM fn_jobs_execution
                WHERE Id = @Id AND TenantId IS NULL
                """,
                SqlDataScope.HostOnly),
            new { Id = executionId },
            cancellationToken);

        Assert.AreEqual(1, processed);
        Assert.IsNotNull(recovered);
        Assert.AreEqual(JobExecutionStatuses.Succeeded, recovered.Status);
        Assert.AreEqual(2, recovered.AttemptCount);
        Assert.IsNotNull(recovered.FinishedAtUtc);

        var activeLeaseExecutionId = Guid.CreateVersion7();
        await command.ExecuteAsync(
            new SqlStatement(
                "test.insert_active_running_job_execution",
                """
                INSERT INTO fn_jobs_execution
                    (Id, TenantId, JobDefinitionId, Status, TriggerKind,
                     ErrorMessage, StartedAtUtc, FinishedAtUtc,
                     LeaseId, LeaseExpiresAtUtc, AttemptCount, CreatedAtUtc)
                VALUES
                    (@Id, NULL, @JobDefinitionId, @Status, @TriggerKind,
                     NULL, @StartedAtUtc, NULL,
                     @LeaseId, @LeaseExpiresAtUtc, 1, @CreatedAtUtc)
                """,
                SqlDataScope.HostOnly),
            new
            {
                Id = activeLeaseExecutionId,
                JobDefinitionId = definitionId,
                Status = JobExecutionStatuses.Running,
                TriggerKind = JobTriggerKinds.Manual,
                StartedAtUtc = now,
                LeaseId = Guid.CreateVersion7(),
                LeaseExpiresAtUtc = now.AddMinutes(5),
                CreatedAtUtc = now,
            },
            cancellationToken);

        var activeLeaseProcessed = await scope.ServiceProvider
            .GetRequiredService<JobExecutionRunner>()
            .ProcessPendingAsync(1, cancellationToken);
        var activeLease = await query.QuerySingleOrDefaultAsync<RecoveredExecution>(
            new SqlStatement(
                "test.find_active_job_execution",
                """
                SELECT Status, AttemptCount, FinishedAtUtc
                FROM fn_jobs_execution
                WHERE Id = @Id AND TenantId IS NULL
                """,
                SqlDataScope.HostOnly),
            new { Id = activeLeaseExecutionId },
            cancellationToken);

        Assert.AreEqual(0, activeLeaseProcessed);
        Assert.IsNotNull(activeLease);
        Assert.AreEqual(JobExecutionStatuses.Running, activeLease.Status);
        Assert.AreEqual(1, activeLease.AttemptCount);
        Assert.IsNull(activeLease.FinishedAtUtc);
    }

    private sealed class RecoveredExecution
    {
        public string Status { get; set; } = string.Empty;

        public int AttemptCount { get; set; }

        public DateTimeOffset? FinishedAtUtc { get; set; }
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }
}
