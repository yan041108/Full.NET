using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试在 Host 与租户域之间切换上下文的共用辅助。</summary>
internal static class IntegrationTestTenantContextHelper
{
    public static async Task<TenantSummary> GetCurrentTenantAsync(
        HttpClient tenantClient,
        CancellationToken cancellationToken = default)
    {
        var tenant = await tenantClient.GetFromJsonAsync<TenantSummary>(
            "/api/v1/tenancy/current",
            cancellationToken);
        Assert.IsNotNull(tenant);
        return tenant!;
    }

    public static async Task<string> SwitchToTenantAsync(
        HttpClient hostClient,
        string hostAccessToken,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(tenantId)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostAccessToken);
        using var response = await hostClient.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var switched = await response.Content.ReadFromJsonAsync<TenantContextTokenResponse>(
            cancellationToken);
        Assert.IsNotNull(switched);
        Assert.IsFalse(string.IsNullOrWhiteSpace(switched!.AccessToken));
        return switched.AccessToken;
    }
}