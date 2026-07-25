using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Tenancy;

/// <summary>
/// 开通租户时可选分配套餐的验收夹具。
/// </summary>
internal static class TenancyProvisionTenantWithPackageAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);

        var packageId = await CreateActivePackageAsync(client, adminToken, cancellationToken);
        await VerifyProvisionWithActivePackageAsync(
            client,
            adminToken,
            packageId,
            cancellationToken);
        await VerifyProvisionRejectsInactivePackageAsync(
            client,
            adminToken,
            cancellationToken);
    }

    private static async Task VerifyProvisionWithActivePackageAsync(
        HttpClient client,
        string adminToken,
        Guid packageId,
        CancellationToken cancellationToken)
    {
        var identifier = $"prov-{Guid.NewGuid():N}"[..14];
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenants",
            adminToken,
            new ProvisionTenantRequest(
                identifier,
                "开通绑定套餐",
                $"{identifier}.localhost",
                packageId));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(packageId, created.TenantPackageId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(created.TenantPackageCode));
        Assert.IsFalse(string.IsNullOrWhiteSpace(created.TenantPackageName));
    }

    private static async Task VerifyProvisionRejectsInactivePackageAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        var inactivePackageId = await CreateInactivePackageAsync(
            client,
            adminToken,
            cancellationToken);
        var identifier = $"bad-{Guid.NewGuid():N}"[..14];
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenants",
            adminToken,
            new ProvisionTenantRequest(
                identifier,
                "禁用套餐开通",
                $"{identifier}.localhost",
                inactivePackageId));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            TenancyErrorCodes.PackageInactive,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<Guid> CreateActivePackageAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        var code = $"pkg-{Guid.NewGuid():N}"[..12];
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenant-packages",
            adminToken,
            new CreateHostTenantPackageRequest(code, "开通测试套餐", null));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TenantPackageSummary>(cancellationToken);
        Assert.IsNotNull(created);
        return created.Id;
    }

    private static async Task<Guid> CreateInactivePackageAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        var packageId = await CreateActivePackageAsync(client, adminToken, cancellationToken);
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenant-packages/{packageId:D}/disable",
            adminToken,
            new { });
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return packageId;
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
