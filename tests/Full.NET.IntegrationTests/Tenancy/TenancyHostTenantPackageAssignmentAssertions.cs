using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Tenancy;

/// <summary>
/// Host 租户-套餐绑定纵向切片验收夹具。
/// </summary>
internal static class TenancyHostTenantPackageAssignmentAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);

        var packageId = await CreateActivePackageAsync(client, adminToken, cancellationToken);
        var tenant = await CreateTenantAsync(client, adminToken, cancellationToken);
        await VerifyAssignActivePackageAsync(client, adminToken, tenant, packageId, cancellationToken);
        await VerifyRejectInactivePackageAsync(
            client,
            adminToken,
            tenant,
            cancellationToken);
        await VerifyUnassignPackageAsync(client, adminToken, tenant, cancellationToken);
        await VerifyAssignVersionConflictAsync(
            client,
            adminToken,
            tenant,
            packageId,
            cancellationToken);
    }

    private static async Task VerifyAssignActivePackageAsync(
        HttpClient client,
        string adminToken,
        TenantSummary tenant,
        Guid packageId,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/package",
            adminToken,
            new AssignHostTenantPackageRequest(packageId, tenant.Version));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var assigned = await response.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(assigned);
        Assert.AreEqual(packageId, assigned.TenantPackageId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(assigned.TenantPackageCode));
        Assert.IsFalse(string.IsNullOrWhiteSpace(assigned.TenantPackageName));
        Assert.AreEqual(tenant.Version + 1, assigned.Version);
    }

    private static async Task VerifyRejectInactivePackageAsync(
        HttpClient client,
        string adminToken,
        TenantSummary tenant,
        CancellationToken cancellationToken)
    {
        var inactivePackageId = await CreateInactivePackageAsync(
            client,
            adminToken,
            cancellationToken);
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        var current = await listResponse.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(current);

        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/package",
            adminToken,
            new AssignHostTenantPackageRequest(inactivePackageId, current.Version));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            TenancyErrorCodes.PackageInactive,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyUnassignPackageAsync(
        HttpClient client,
        string adminToken,
        TenantSummary tenant,
        CancellationToken cancellationToken)
    {
        using var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken);
        var current = await getResponse.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(current);

        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/package",
            adminToken,
            new AssignHostTenantPackageRequest(null, current.Version));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var unassigned = await response.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(unassigned);
        Assert.IsNull(unassigned.TenantPackageId);
        Assert.IsNull(unassigned.TenantPackageCode);
        Assert.IsNull(unassigned.TenantPackageName);
    }

    private static async Task VerifyAssignVersionConflictAsync(
        HttpClient client,
        string adminToken,
        TenantSummary tenant,
        Guid packageId,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{tenant.Id:D}/package",
            adminToken,
            new AssignHostTenantPackageRequest(packageId, tenant.Version));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            TenancyErrorCodes.VersionConflict,
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
            new CreateHostTenantPackageRequest(code, "绑定测试套餐", null));
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

    private static async Task<TenantSummary> CreateTenantAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        var identifier = $"bind-{Guid.NewGuid():N}"[..14];
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenants",
            adminToken,
            new ProvisionTenantRequest(identifier, "绑定测试租户", $"{identifier}.localhost"));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TenantSummary>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
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
