using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>Organization CDC E2E 复用的 API 登录与写路径辅助。</summary>
internal static class FullNetApiFactoryLogin
{
    internal static async Task<string> LoginAsHostAdminAsync(
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
        var loginToken = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(loginToken);
        return loginToken.AccessToken;
    }

    internal static async Task<TenantSession> EnterAcmeTenantAsync(
        HttpClient client,
        string hostAccessToken,
        CancellationToken cancellationToken)
    {
        using var availableRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            hostAccessToken);
        using var availableResponse = await client.SendAsync(availableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        var available = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        var acme = available.Single(tenant => tenant.Identifier == "acme");

        using var enterRequest = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(acme.Id)),
        };
        enterRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            hostAccessToken);
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);
        var entered = await enterResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(entered);
        return new TenantSession(acme.Id, entered.AccessToken);
    }

    internal sealed record TenantSession(Guid TenantId, string AccessToken);
}

internal static class FullNetApiFactoryOrganization
{
    internal static async Task<OrganizationUnitResponse> CreateUnitAsync(
        HttpClient client,
        string tenantToken,
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/organization/units")
        {
            Content = JsonContent.Create(
                new CreateOrganizationUnitRequest(null, code, name, 10)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<OrganizationUnitResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }
}
