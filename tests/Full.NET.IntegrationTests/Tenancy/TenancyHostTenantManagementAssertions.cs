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
/// Host 租户管理纵向切片验收夹具。
/// </summary>
internal static class TenancyHostTenantManagementAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresManageReadPermissionAsync(factory, client, cancellationToken);
        await VerifyContextReadDoesNotGrantHostDirectoryAsync(factory, client, cancellationToken);
        await VerifyCreateRejectsDuplicateIdentifierAsync(client, cancellationToken);
        await VerifyUpdateNameWithOptimisticVersionAsync(client, cancellationToken);
        await VerifyDisableRemovesTenantFromAvailableListAsync(client, cancellationToken);
        await VerifyCannotDisableLastActiveTenantAsync(client, cancellationToken);
        await Api.OpenApiHostTenantsContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyListRequiresManageReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenants?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await factory.CreateHostAccessTokenAsync(
                ["platform.dashboard.read"],
                cancellationToken));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyContextReadDoesNotGrantHostDirectoryAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var token = await factory.CreateHostAccessTokenAsync(
            ["tenancy.tenants.read", "tenancy.tenants.switch"],
            cancellationToken);

        using var directoryRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenants?page=1&pageSize=20");
        directoryRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);
        using var directoryResponse = await client.SendAsync(
            directoryRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, directoryResponse.StatusCode);

        using var availableRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);
        using var availableResponse = await client.SendAsync(
            availableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
    }

    private static async Task VerifyCreateRejectsDuplicateIdentifierAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var identifier = $"tenant-{Guid.NewGuid():N}"[..12];
        var domain = $"{identifier}.localhost";
        var body = new ProvisionTenantRequest(identifier, "集成测试租户", domain);

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenants",
            adminToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        var created = await firstResponse.Content.ReadFromJsonAsync<TenantSummary>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(identifier, created.Identifier);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenants",
            adminToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            TenancyErrorCodes.IdentifierExists,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyUpdateNameWithOptimisticVersionAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var identifier = $"update-{Guid.NewGuid():N}"[..14];
        var domain = $"{identifier}.localhost";

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenants",
            adminToken,
            new ProvisionTenantRequest(identifier, "更新前名称", domain));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantSummary>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/tenancy/tenants/{created.Id:D}",
            adminToken,
            new UpdateHostTenantRequest("更新后名称", created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<TenantSummary>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("更新后名称", updated.Name);
        Assert.AreEqual(created.Version + 1, updated.Version);

        using var staleRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/tenancy/tenants/{created.Id:D}",
            adminToken,
            new UpdateHostTenantRequest("冲突名称", created.Version));
        using var staleResponse = await client.SendAsync(staleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, staleResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await staleResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            TenancyErrorCodes.VersionConflict,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyDisableRemovesTenantFromAvailableListAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var identifier = $"disable-{Guid.NewGuid():N}"[..14];
        var domain = $"{identifier}.localhost";

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/tenancy/tenants",
            adminToken,
            new ProvisionTenantRequest(identifier, "待禁用租户", domain));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<TenantSummary>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{created.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<TenantSummary>(
            cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);

        using var availableRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var availableResponse = await client.SendAsync(
            availableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        var available = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        Assert.IsFalse(available.Any(tenant => tenant.Id == created.Id));
    }

    private static async Task VerifyCannotDisableLastActiveTenantAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenants?page=1&pageSize=50");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<TenantSummary>>(
            cancellationToken);
        Assert.IsNotNull(page);
        var activeTenants = page.Items.Where(tenant => tenant.IsActive).ToArray();
        foreach (var tenant in activeTenants.Skip(1))
        {
            using var disableRequest = CreateBearerJsonRequest(
                HttpMethod.Post,
                $"/api/v1/tenancy/tenants/{tenant.Id:D}/disable",
                adminToken,
                new { });
            using var disableResponse = await client.SendAsync(
                disableRequest,
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        }

        var lastActive = activeTenants[0];
        using var lastDisableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/tenancy/tenants/{lastActive.Id:D}/disable",
            adminToken,
            new { });
        using var lastDisableResponse = await client.SendAsync(
            lastDisableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, lastDisableResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await lastDisableResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            TenancyErrorCodes.LastActiveTenant,
            problem.RootElement.GetProperty("code").GetString());
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
