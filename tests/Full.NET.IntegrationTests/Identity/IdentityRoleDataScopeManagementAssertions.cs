using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// Host 角色数据范围纵向切片验收夹具。
/// </summary>
internal static class IdentityRoleDataScopeManagementAssertions
{
    public static async Task VerifyHostRoleDataScopeContractAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifySystemRoleDataScopeUpdateRejectedAsync(client, cancellationToken);
        await VerifyCustomRoleDataScopeLifecycleAsync(factory, client, cancellationToken);
        await OpenApiHostRoleDataScopeContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifySystemRoleDataScopeUpdateRejectedAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/roles?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<HostRoleResponse>>(
            cancellationToken);
        Assert.IsNotNull(page);
        var systemRole = page.Items.First(role => role.IsSystem);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{systemRole.Id:D}/data-scope",
            adminToken,
            new UpdateHostRoleDataScopeRequest(
                RoleDataScopeKinds.Self,
                null,
                systemRole.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, updateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await updateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.RoleSystemLocked,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCustomRoleDataScopeLifecycleAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var hostToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var code = $"role-{Guid.NewGuid():N}".ToLowerInvariant();

        using var createRoleRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            hostToken,
            new CreateHostRoleRequest(code, "数据范围测试角色"));
        using var createRoleResponse = await client.SendAsync(createRoleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createRoleResponse.StatusCode);
        var createdRole = await createRoleResponse.Content.ReadFromJsonAsync<HostRoleResponse>(
            cancellationToken);
        Assert.IsNotNull(createdRole);

        using var getDefaultRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/roles/{createdRole.Id:D}/data-scope");
        getDefaultRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostToken);
        using var getDefaultResponse = await client.SendAsync(getDefaultRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getDefaultResponse.StatusCode);
        var defaultScope = await getDefaultResponse.Content
            .ReadFromJsonAsync<HostRoleDataScopeResponse>(cancellationToken);
        Assert.IsNotNull(defaultScope);
        Assert.AreEqual(RoleDataScopeKinds.All, defaultScope.DataScopeKind);

        // 使用独立会话进入租户，避免推进 Host 管理会话版本。
        var tenantContext = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var unitCode = $"unit-{Guid.NewGuid():N}".ToLowerInvariant();
        using var createUnitRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            tenantContext.AccessToken,
            new CreateOrganizationUnitRequest(null, unitCode, "数据范围机构", 10));
        using var createUnitResponse = await client.SendAsync(createUnitRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createUnitResponse.StatusCode);
        var unit = await createUnitResponse.Content.ReadFromJsonAsync<OrganizationUnitResponse>(
            cancellationToken);
        Assert.IsNotNull(unit);

        await IdentityOrganizationUnitProjectionTestHelper.BackfillTenantAsync(
            factory,
            tenantContext.TenantId,
            cancellationToken);

        hostToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var updateScopeRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{createdRole.Id:D}/data-scope",
            hostToken,
            new UpdateHostRoleDataScopeRequest(
                RoleDataScopeKinds.Custom,
                [unit.Id],
                defaultScope.Version,
                tenantContext.TenantId));
        using var updateScopeResponse = await client.SendAsync(updateScopeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateScopeResponse.StatusCode);
        var updatedScope = await updateScopeResponse.Content
            .ReadFromJsonAsync<HostRoleDataScopeResponse>(cancellationToken);
        Assert.IsNotNull(updatedScope);
        Assert.AreEqual(RoleDataScopeKinds.Custom, updatedScope.DataScopeKind);
        Assert.AreEqual(1, updatedScope.UnitIds.Count);
        Assert.AreEqual(unit.Id, updatedScope.UnitIds[0]);
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
        var loginToken = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(loginToken);
        return loginToken.AccessToken;
    }

    private static async Task<TenantSession> LoginAndEnterAcmeTenantAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var hostAccessToken = await LoginAsHostAdminAsync(client, cancellationToken);
        return await EnterAcmeTenantAsync(client, hostAccessToken, cancellationToken);
    }

    private static async Task<TenantSession> EnterAcmeTenantAsync(
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

    private static HttpRequestMessage CreateBearerJsonRequest<TRequest>(
        HttpMethod method,
        string path,
        string accessToken,
        TRequest body)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null && method != HttpMethod.Get)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }

    private sealed record TenantSession(Guid TenantId, string AccessToken);
}
