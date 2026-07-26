using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>Host 工作台汇总纵向切片验收夹具。</summary>
internal static class PlatformHostDashboardAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyRequiresDashboardPermissionAsync(factory, client, cancellationToken);
        await VerifySummaryReturnsLiveMetricsAsync(client, cancellationToken);
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

    private static async Task VerifySummaryReturnsLiveMetricsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);

        using var probeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        probeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var probeResponse = await client.SendAsync(probeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, probeResponse.StatusCode);

        using var activityRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/roles/{Guid.CreateVersion7():D}/disable");
        activityRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var activityResponse = await client.SendAsync(activityRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, activityResponse.StatusCode);

        using var summaryRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/platform/host-dashboard-summary");
        summaryRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var summaryResponse = await client.SendAsync(summaryRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, summaryResponse.StatusCode);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<HostDashboardSummaryResponse>(
            cancellationToken);
        Assert.IsNotNull(summary);
        Assert.IsTrue(summary.ActiveTenantCount >= 0);
        Assert.IsTrue(summary.OnlineSessionCount >= 1);
        Assert.IsTrue(summary.TodayRequestCount >= 1);
        Assert.IsTrue(summary.TodayErrorRate >= 0m && summary.TodayErrorRate <= 1m);
        Assert.IsNotNull(summary.RecentActivities);
        Assert.IsTrue(summary.RecentActivities.Length > 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(summary.RecentActivities[0].RequestPath));
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
