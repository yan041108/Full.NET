using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Jobs;

/// <summary>验证 Host 计划管理、Worker 物化和执行历史关联形成同一纵向切片。</summary>
internal static class JobsScheduleAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        HttpClient client,
        HostJobDefinitionResponse definition,
        CancellationToken cancellationToken)
    {
        var token = await LoginAsHostAdminAsync(client, cancellationToken);
        var schedule = await CreateCronScheduleAsync(
            client,
            token,
            definition.Id,
            cancellationToken);
        await VerifyListUpdatePauseResumeAsync(
            client,
            token,
            schedule,
            definition,
            cancellationToken);
        await VerifySelfContainedPermissionBoundaryAsync(
            factory,
            client,
            definition,
            cancellationToken);
        await VerifyCronMaterializationAsync(
            factory,
            schedule.Id,
            cancellationToken);
        await VerifyOneTimeCompletionAsync(
            factory,
            client,
            token,
            definition.Id,
            cancellationToken);
        await CompleteMaterializedExecutionsAsync(
            factory,
            cancellationToken);
    }

    private static async Task<HostJobScheduleResponse> CreateCronScheduleAsync(
        HttpClient client,
        string token,
        Guid definitionId,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/jobs/host-schedules",
            token,
            new CreateHostJobScheduleRequest(
                definitionId,
                JobTriggerKinds.Cron,
                "*/5 * * * *",
                "Eastern Standard Time",
                null,
                JobMisfirePolicies.FireOnce));
        using var response = await client.SendAsync(request, cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Created,
            response.StatusCode,
            responseBody);
        var created = await response.Content
            .ReadFromJsonAsync<HostJobScheduleResponse>(
                cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual("America/New_York", created.TimeZoneId);
        Assert.IsTrue(created.IsEnabled);
        Assert.IsNotNull(created.NextExecutionAtUtc);
        return created;
    }

    private static async Task VerifyListUpdatePauseResumeAsync(
        HttpClient client,
        string token,
        HostJobScheduleResponse schedule,
        HostJobDefinitionResponse definition,
        CancellationToken cancellationToken)
    {
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/jobs/host-schedules?page=1&pageSize=20"
            + $"&jobDefinitionId={schedule.JobDefinitionId:D}");
        listRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        using var listResponse = await client.SendAsync(
            listRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<HostJobScheduleResponse>>(
                cancellationToken);
        Assert.IsNotNull(page);
        Assert.AreEqual(1, page.Page);
        Assert.AreEqual(20, page.PageSize);
        Assert.IsTrue(page.Total >= 1);
        var listed = page.Items.FirstOrDefault(item => item.Id == schedule.Id);
        Assert.IsNotNull(listed);
        Assert.AreEqual(definition.DisplayName, listed.JobDefinitionDisplayName);
        Assert.AreEqual(definition.JobKey, listed.JobDefinitionJobKey);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/jobs/host-schedules/{schedule.Id:D}",
            token,
            new UpdateHostJobScheduleRequest(
                JobTriggerKinds.Cron,
                "* * * * *",
                "UTC",
                null,
                JobMisfirePolicies.Skip,
                schedule.Version));
        using var updateResponse = await client.SendAsync(
            updateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content
            .ReadFromJsonAsync<HostJobScheduleResponse>(
                cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("UTC", updated.TimeZoneId);

        using var pauseRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/jobs/host-schedules/{schedule.Id:D}/pause",
            token,
            new ChangeHostJobScheduleStateRequest(updated.Version));
        using var pauseResponse = await client.SendAsync(
            pauseRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, pauseResponse.StatusCode);
        var paused = await pauseResponse.Content
            .ReadFromJsonAsync<HostJobScheduleResponse>(
                cancellationToken);
        Assert.IsNotNull(paused);
        Assert.IsFalse(paused.IsEnabled);

        using var resumeRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/jobs/host-schedules/{schedule.Id:D}/resume",
            token,
            new ChangeHostJobScheduleStateRequest(paused.Version));
        using var resumeResponse = await client.SendAsync(
            resumeRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, resumeResponse.StatusCode);
        var resumed = await resumeResponse.Content
            .ReadFromJsonAsync<HostJobScheduleResponse>(
                cancellationToken);
        Assert.IsNotNull(resumed);
        Assert.IsTrue(resumed.IsEnabled);
        Assert.IsTrue(resumed.NextExecutionAtUtc > paused.UpdatedAtUtc);
    }

    private static async Task VerifySelfContainedPermissionBoundaryAsync(
        FullNetApiFactory factory,
        HttpClient client,
        HostJobDefinitionResponse definition,
        CancellationToken cancellationToken)
    {
        var schedulesOnly = await factory.CreateHostIdentityAsync(
            $"jobs-schedules-only-{Guid.NewGuid():N}",
            [
                HostJobPermissions.SchedulesRead,
                HostJobPermissions.SchedulesCreate,
            ],
            cancellationToken);

        using (var listResponse = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       "/api/v1/jobs/host-schedules?page=1&pageSize=20",
                       schedulesOnly.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
            var page = await listResponse.Content
                .ReadFromJsonAsync<PagedResult<HostJobScheduleResponse>>(
                    cancellationToken);
            Assert.IsNotNull(page);
            Assert.IsTrue(page.Total >= 1);
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(page.Items[0].JobDefinitionDisplayName));
        }

        using (var optionsResponse = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       "/api/v1/jobs/host-schedules/definition-options",
                       schedulesOnly.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, optionsResponse.StatusCode);
            var options = await optionsResponse.Content
                .ReadFromJsonAsync<IReadOnlyList<HostJobScheduleDefinitionOptionResponse>>(
                    cancellationToken);
            Assert.IsNotNull(options);
            Assert.IsTrue(options.Any(item => item.Id == definition.Id));
        }

        using (var cronPreviewResponse = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       "/api/v1/jobs/host-schedules/cron-preview"
                       + "?cronExpression=0%209%20*%20*%20*&timeZoneId=UTC",
                       schedulesOnly.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, cronPreviewResponse.StatusCode);
        }

        using (var definitionsForbidden = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       "/api/v1/jobs/host-definitions?page=1&pageSize=20",
                       schedulesOnly.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, definitionsForbidden.StatusCode);
        }

        var readOnly = await factory.CreateHostIdentityAsync(
            $"jobs-schedules-read-only-{Guid.NewGuid():N}",
            [HostJobPermissions.SchedulesRead],
            cancellationToken);
        using (var optionsForbidden = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       "/api/v1/jobs/host-schedules/definition-options",
                       readOnly.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, optionsForbidden.StatusCode);
        }
    }

    private static HttpRequestMessage Authorized(
        HttpMethod method,
        string path,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static async Task VerifyCronMaterializationAsync(
        FullNetApiFactory factory,
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>()
            .SetHost();
        var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var dueAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await command.ExecuteAsync(
            new SqlStatement(
                "test.make_job_schedule_due",
                """
                UPDATE fn_jobs_schedule
                SET MisfirePolicy = @MisfirePolicy,
                    NextExecutionAtUtc = @NextExecutionAtUtc
                WHERE Id = @Id AND TenantId IS NULL
                """,
                SqlDataScope.HostOnly),
            new
            {
                Id = scheduleId,
                MisfirePolicy = JobMisfirePolicies.FireOnce,
                NextExecutionAtUtc = dueAtUtc,
            },
            cancellationToken);

        var created = await scope.ServiceProvider
            .GetRequiredService<JobScheduleDispatcher>()
            .ProcessDueAsync(10, cancellationToken);
        var execution = await query.QuerySingleOrDefaultAsync<ScheduledExecution>(
            new SqlStatement(
                "test.find_scheduled_job_execution",
                """
                SELECT Id, JobScheduleId, TriggerKind, ScheduledForUtc, Status
                FROM fn_jobs_execution
                WHERE JobScheduleId = @JobScheduleId
                  AND TenantId IS NULL
                ORDER BY CreatedAtUtc DESC
                """,
                SqlDataScope.HostOnly),
            new { JobScheduleId = scheduleId },
            cancellationToken);

        Assert.AreEqual(1, created);
        Assert.IsNotNull(execution);
        Assert.AreEqual(scheduleId, execution.JobScheduleId);
        Assert.AreEqual(JobTriggerKinds.Cron, execution.TriggerKind);
        Assert.IsNotNull(execution.ScheduledForUtc);
        Assert.AreEqual(JobExecutionStatuses.Pending, execution.Status);
    }

    private static async Task VerifyOneTimeCompletionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string token,
        Guid definitionId,
        CancellationToken cancellationToken)
    {
        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/jobs/host-schedules",
            token,
            new CreateHostJobScheduleRequest(
                definitionId,
                JobTriggerKinds.OneTime,
                null,
                "UTC",
                DateTimeOffset.UtcNow.AddHours(1),
                JobMisfirePolicies.FireOnce));
        using var createResponse = await client.SendAsync(
            createRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var oneTime = await createResponse.Content
            .ReadFromJsonAsync<HostJobScheduleResponse>(
                cancellationToken);
        Assert.IsNotNull(oneTime);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>()
            .SetHost();
        var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var dueAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await command.ExecuteAsync(
            new SqlStatement(
                "test.make_one_time_job_schedule_due",
                """
                UPDATE fn_jobs_schedule
                SET OneTimeAtUtc = @DueAtUtc,
                    NextExecutionAtUtc = @DueAtUtc
                WHERE Id = @Id AND TenantId IS NULL
                """,
                SqlDataScope.HostOnly),
            new { oneTime.Id, DueAtUtc = dueAtUtc },
            cancellationToken);

        var created = await scope.ServiceProvider
            .GetRequiredService<JobScheduleDispatcher>()
            .ProcessDueAsync(10, cancellationToken);
        var completed = await query
            .QuerySingleOrDefaultAsync<CompletedSchedule>(
                new SqlStatement(
                    "test.find_completed_one_time_job_schedule",
                    """
                    SELECT IsEnabled, NextExecutionAtUtc, CompletedAtUtc
                    FROM fn_jobs_schedule
                    WHERE Id = @Id AND TenantId IS NULL
                    """,
                    SqlDataScope.HostOnly),
                new { oneTime.Id },
                cancellationToken);

        Assert.IsTrue(created >= 1);
        Assert.IsNotNull(completed);
        Assert.IsFalse(completed.IsEnabled);
        Assert.IsNull(completed.NextExecutionAtUtc);
        Assert.IsNotNull(completed.CompletedAtUtc);
    }

    private static async Task CompleteMaterializedExecutionsAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>()
            .SetHost();
        await scope.ServiceProvider.GetRequiredService<ICommandExecutor>()
            .ExecuteAsync(
                new SqlStatement(
                    "test.complete_materialized_job_executions",
                    """
                    UPDATE fn_jobs_execution
                    SET Status = @Status,
                        FinishedAtUtc = @FinishedAtUtc
                    WHERE TenantId IS NULL
                      AND JobScheduleId IS NOT NULL
                      AND Status = @PendingStatus
                    """,
                    SqlDataScope.HostOnly),
                new
                {
                    Status = JobExecutionStatuses.Succeeded,
                    FinishedAtUtc = DateTimeOffset.UtcNow,
                    PendingStatus = JobExecutionStatuses.Pending,
                },
                cancellationToken);
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        request.Headers.Add("Origin", "http://localhost");
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(
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
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private sealed class ScheduledExecution
    {
        public Guid Id { get; set; }

        public Guid? JobScheduleId { get; set; }

        public string TriggerKind { get; set; } = string.Empty;

        public DateTimeOffset? ScheduledForUtc { get; set; }

        public string Status { get; set; } = string.Empty;
    }

    private sealed class CompletedSchedule
    {
        public bool IsEnabled { get; set; }

        public DateTimeOffset? NextExecutionAtUtc { get; set; }

        public DateTimeOffset? CompletedAtUtc { get; set; }
    }
}
