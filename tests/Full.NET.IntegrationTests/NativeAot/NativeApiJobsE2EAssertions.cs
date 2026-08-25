using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Jobs.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// Native 原生产物上的 Jobs HTTP/JSON 链路断言。覆盖定义、手动触发、执行列表、计划列表与健康查询。
/// </summary>
internal static class NativeApiJobsE2EAssertions
{
    public static async Task VerifyJobsFlowAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);

        await using var host = await NativeApiProcessHost.StartAsync(
            artifact,
            provider,
            connectionString,
            new Dictionary<string, string?>(),
            TimeSpan.FromMinutes(3),
            cancellationToken).ConfigureAwait(false);

        using var client = host.CreateClient();
        var token = await NativeApiE2EAssertions.LoginAsync(
                client,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);

        var jobKey = $"aot.ping.{Guid.NewGuid():N}"[..20];
        using var createRequest = AuthorizedJson(
            HttpMethod.Post,
            "/api/v1/jobs/host-definitions",
            token,
            new CreateHostJobDefinitionRequest(
                jobKey,
                JobHandlerKinds.Ping,
                null,
                "Native AOT Ping",
                "native-aot",
                "aot"));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                createResponse,
                HttpStatusCode.Created,
                "Create host job definition",
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var created = await createResponse.Content
            .ReadFromJsonAsync<HostJobDefinitionResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(created);
        Assert.AreEqual(jobKey, created.JobKey);

        using var getRequest = Authorized(
            HttpMethod.Get,
            $"/api/v1/jobs/host-definitions/{created.Id:D}",
            token);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                getResponse,
                HttpStatusCode.OK,
                "Get host job definition",
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);

        using var listRequest = Authorized(
            HttpMethod.Get,
            "/api/v1/jobs/host-definitions?page=1&pageSize=20",
            token);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                listResponse,
                HttpStatusCode.OK,
                "List host job definitions",
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);

        using var triggerRequest = Authorized(HttpMethod.Post, $"/api/v1/jobs/host-definitions/{created.Id:D}/trigger", token);
        using var triggerResponse = await client.SendAsync(triggerRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                triggerResponse,
                HttpStatusCode.Created,
                "Trigger host job",
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var execution = await triggerResponse.Content
            .ReadFromJsonAsync<HostJobExecutionResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(execution);
        Assert.AreEqual(JobExecutionStatuses.Succeeded, execution.Status);

        using var executionsRequest = Authorized(
            HttpMethod.Get,
            $"/api/v1/jobs/host-executions?page=1&pageSize=20&jobDefinitionId={created.Id:D}",
            token);
        using var executionsResponse = await client.SendAsync(executionsRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                executionsResponse,
                HttpStatusCode.OK,
                "List host job executions",
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var executions = await executionsResponse.Content
            .ReadFromJsonAsync<PagedResult<HostJobExecutionResponse>>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(executions);
        Assert.IsTrue(executions.Items.Any(item => item.Id == execution.Id));

        using var schedulesRequest = Authorized(
            HttpMethod.Get,
            "/api/v1/jobs/host-schedules?page=1&pageSize=20",
            token);
        using var schedulesResponse = await client.SendAsync(schedulesRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                schedulesResponse,
                HttpStatusCode.OK,
                "List host job schedules",
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);

        using var optionsRequest = Authorized(
            HttpMethod.Get,
            "/api/v1/jobs/host-schedules/definition-options",
            token);
        using var optionsResponse = await client.SendAsync(optionsRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                optionsResponse,
                HttpStatusCode.OK,
                "List job schedule definition options",
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);

        using var healthRequest = Authorized(HttpMethod.Get, "/api/v1/jobs/host-health", token);
        using var healthResponse = await client.SendAsync(healthRequest, cancellationToken)
            .ConfigureAwait(false);
        await AssertOkAsync(
                healthResponse,
                HttpStatusCode.OK,
                "Get host job health",
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var health = await healthResponse.Content
            .ReadFromJsonAsync<HostJobHealthResponse>(cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(health);
        Assert.IsTrue(health.RegisteredHandlers.Contains(JobHandlerKinds.Ping));

        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage AuthorizedJson<T>(
        HttpMethod method,
        string url,
        string token,
        T body)
    {
        var request = Authorized(method, url, token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task AssertOkAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string operation,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        var logTail = File.Exists(logFilePath)
            ? File.ReadAllText(logFilePath)
            : string.Empty;
        if (logTail.Length > 4000)
        {
            logTail = logTail[^4000..];
        }

        Assert.Fail(
            $"{operation} failed. Expected {expected}, actual {response.StatusCode}. "
            + $"Response body: {body}"
            + (string.IsNullOrEmpty(logTail) ? string.Empty : $"\nNative log tail:\n{logTail}"));
    }
}
