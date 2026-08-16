using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Jobs.Contracts;

namespace Full.NET.IntegrationTests.Jobs;

/// <summary>验证执行历史列表过滤与按 Id 查询 API。</summary>
internal static class JobsExecutionHistoryAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        HttpClient client,
        HostJobDefinitionResponse definition,
        HostJobExecutionResponse triggeredExecution,
        CancellationToken cancellationToken = default)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);

        using var byIdRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/jobs/host-executions/{triggeredExecution.Id:D}");
        byIdRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var byIdResponse = await client.SendAsync(byIdRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, byIdResponse.StatusCode);
        var byId = await byIdResponse.Content.ReadFromJsonAsync<HostJobExecutionResponse>(
            cancellationToken);
        Assert.IsNotNull(byId);
        Assert.AreEqual(triggeredExecution.Id, byId.Id);

        using var filteredRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/jobs/host-executions?page=1&pageSize=20&jobDefinitionId={definition.Id:D}&status={JobExecutionStatuses.Succeeded}");
        filteredRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var filteredResponse = await client.SendAsync(filteredRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, filteredResponse.StatusCode);
        var page = await filteredResponse.Content.ReadFromJsonAsync<PagedExecutions>(cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == triggeredExecution.Id));
        Assert.IsTrue(page.Total >= 1);

        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [HostJobPermissions.DefinitionsRead],
            cancellationToken);
        using var forbiddenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/jobs/host-executions/{triggeredExecution.Id:D}");
        forbiddenRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            readOnlyToken);
        using var forbiddenResponse = await client.SendAsync(forbiddenRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    private sealed record PagedExecutions(
        HostJobExecutionResponse[] Items,
        int Page,
        int PageSize,
        long Total);

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        return token!.AccessToken;
    }
}
