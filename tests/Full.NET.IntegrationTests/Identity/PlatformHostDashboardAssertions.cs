using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Auditing.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>Host 工作台汇总纵向切片验收夹具。</summary>
internal static class PlatformHostDashboardAssertions
{
    /// <summary>
    /// 验证 Host 工作台的权限、实时汇总与 OpenAPI 契约。
    /// </summary>
    /// <param name="factory">待验证的真实测试宿主。</param>
    /// <param name="cancellationToken">用于取消整个验收流程的令牌。</param>
    /// <returns>表示异步验收执行的任务。</returns>
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyRequiresDashboardPermissionAsync(factory, client, cancellationToken);
        await VerifySummaryReturnsLiveMetricsAsync(factory, client, cancellationToken);
        await OpenApiPlatformHostDashboardContractAssertions.VerifyAsync(client, cancellationToken);
    }

    private static async Task VerifyRequiresDashboardPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/platform/host-dashboard-summary");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await factory.CreateHostAccessTokenAsync(
                ["identity.users.read"],
                cancellationToken));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// 验证工作台能够从审计表返回实时汇总指标与最近活动。
    /// </summary>
    /// <param name="factory">已初始化的真实测试宿主。</param>
    /// <param name="client">已连接真实测试宿主的 HTTP 客户端。</param>
    /// <param name="cancellationToken">用于取消 HTTP 调用与等待的令牌。</param>
    /// <returns>表示异步验证执行的任务。</returns>
    private static async Task VerifySummaryReturnsLiveMetricsAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);

        await SeedAccessMetricAsync(factory, cancellationToken);

        using var probeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        probeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var probeResponse = await client.SendAsync(probeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, probeResponse.StatusCode);

        for (var index = 0; index < 7; index++)
        {
            using var activityRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/v1/identity/roles/{Guid.CreateVersion7():D}/disable");
            activityRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                adminToken);
            using var activityResponse = await client.SendAsync(
                activityRequest,
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.NotFound, activityResponse.StatusCode);
        }

        HostDashboardSummaryResponse? summary = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var summaryRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/v1/platform/host-dashboard-summary");
            summaryRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                adminToken);
            using var summaryResponse = await client.SendAsync(summaryRequest, cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, summaryResponse.StatusCode);
            summary = await summaryResponse.Content.ReadFromJsonAsync<HostDashboardSummaryResponse>(
                cancellationToken);
            Assert.IsNotNull(summary);
            if (summary.TodayRequestCount >= 1)
            {
                break;
            }

            // OperationLog 仍通过微批落库，轮询权威查询避免依赖固定机器时序。
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        Assert.IsNotNull(summary);
        Assert.IsTrue(summary.ActiveTenantCount >= 0);
        Assert.IsTrue(summary.OnlineSessionCount >= 1);
        Assert.IsTrue(summary.TodayRequestCount >= 1);
        Assert.IsTrue(summary.TodayErrorRate >= 0m && summary.TodayErrorRate <= 1m);
        Assert.IsNotNull(summary.RecentActivities);
        Assert.HasCount(5, summary.RecentActivities);
        Assert.IsFalse(string.IsNullOrWhiteSpace(summary.RecentActivities[0].RequestPath));
        for (var index = 1; index < summary.RecentActivities.Length; index++)
        {
            Assert.IsGreaterThanOrEqualTo(
                summary.RecentActivities[index].OccurredAtUtc,
                summary.RecentActivities[index - 1].OccurredAtUtc);
        }
    }

    /// <summary>
    /// 写入一条可预期的访问指标，避免把 B2 结构化 HTTP 日志误当成审计表写入链。
    /// </summary>
    /// <param name="factory">已初始化的真实测试宿主。</param>
    /// <param name="cancellationToken">用于取消数据库写入的令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    private static async Task SeedAccessMetricAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var commandExecutor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        await commandExecutor.ExecuteAsync(
            AccessLogSql.Insert,
            AuditingSqlParameters.Create(
                ("Id", Guid.CreateVersion7()),
                ("OccurredAtUtc", DateTimeOffset.UtcNow),
                ("HttpMethod", "GET"),
                ("RequestPath", "/api/v1/me"),
                ("StatusCode", (int)HttpStatusCode.OK),
                ("DurationMs", 1),
                ("UserId", null),
                ("TenantId", null),
                ("TraceId", "dashboard-integration-probe"),
                ("ClientIpFingerprint", null),
                ("IsAuthenticated", true)),
            cancellationToken);
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
}
