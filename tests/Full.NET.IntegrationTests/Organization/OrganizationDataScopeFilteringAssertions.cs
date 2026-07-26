using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Organization;

/// <summary>
/// 运行时多角色数据范围并集在租户机构查询上的验收夹具。
/// </summary>
internal static class OrganizationDataScopeFilteringAssertions
{
    public static async Task VerifyTenantUnitDataScopeFilteringAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyCustomRoleScopeLimitsVisibleUnitsAsync(client, cancellationToken);
        await VerifyTenantUserUnitListDataScopeFilteringAsync(client, cancellationToken);
    }

    private static async Task VerifyCustomRoleScopeLimitsVisibleUnitsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var hostAdminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var adminTenant = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var visibleCode = $"scope-visible-{Guid.NewGuid():N}".ToLowerInvariant();
        var hiddenCode = $"scope-hidden-{Guid.NewGuid():N}".ToLowerInvariant();

        var visibleUnit = await CreateUnitAsync(
            client,
            adminTenant.AccessToken,
            visibleCode,
            "可见机构",
            cancellationToken);
        var hiddenUnit = await CreateUnitAsync(
            client,
            adminTenant.AccessToken,
            hiddenCode,
            "隐藏机构",
            cancellationToken);
        hostAdminToken = await LoginAsHostAdminAsync(client, cancellationToken);

        var roleCode = $"scope-role-{Guid.NewGuid():N}".ToLowerInvariant();
        using var createRoleRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            hostAdminToken,
            new CreateHostRoleRequest(roleCode, "数据范围过滤角色"));
        using var createRoleResponse = await client.SendAsync(createRoleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createRoleResponse.StatusCode);
        var createdRole = await createRoleResponse.Content
            .ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(createdRole);

        using var updatePermissionsRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{createdRole.Id:D}/permissions",
            hostAdminToken,
            new ReplaceHostRolePermissionsRequest(
                [
                    OrganizationUnitManagementPermissions.Read,
                    TenancyTenantManagementPermissions.Read,
                    "tenancy.tenants.switch",
                    "platform.dashboard.read",
                ],
                createdRole.Version));
        using var updatePermissionsResponse = await client.SendAsync(
            updatePermissionsRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updatePermissionsResponse.StatusCode);
        var roleWithPermissions = await updatePermissionsResponse.Content
            .ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(roleWithPermissions);

        using var updateScopeRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{createdRole.Id:D}/data-scope",
            hostAdminToken,
            new UpdateHostRoleDataScopeRequest(
                RoleDataScopeKinds.Custom,
                [visibleUnit.Id],
                roleWithPermissions.Version,
                adminTenant.TenantId));
        using var updateScopeResponse = await client.SendAsync(updateScopeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateScopeResponse.StatusCode);
        var customScope = await updateScopeResponse.Content
            .ReadFromJsonAsync<HostRoleDataScopeResponse>(cancellationToken);
        Assert.IsNotNull(customScope);

        var username = $"scope-user-{Guid.NewGuid():N}".ToLowerInvariant();
        using var createUserRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            hostAdminToken,
            new CreateHostUserRequest(
                username,
                "数据范围受限用户",
                FullNetApiFactory.TestPassword));
        using var createUserResponse = await client.SendAsync(createUserRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createUserResponse.StatusCode);
        var createdUser = await createUserResponse.Content
            .ReadFromJsonAsync<HostUserResponse>(cancellationToken);
        Assert.IsNotNull(createdUser);

        using var getRolesRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/users/{createdUser.Id:D}/roles");
        getRolesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostAdminToken);
        using var getRolesResponse = await client.SendAsync(getRolesRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getRolesResponse.StatusCode);
        var userRoles = await getRolesResponse.Content
            .ReadFromJsonAsync<HostUserRolesResponse>(cancellationToken);
        Assert.IsNotNull(userRoles);

        using var assignRoleRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/users/{createdUser.Id:D}/roles",
            hostAdminToken,
            new ReplaceHostUserRolesRequest([createdRole.Id], userRoles.Version));
        using var assignRoleResponse = await client.SendAsync(assignRoleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, assignRoleResponse.StatusCode);

        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginToken = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        Assert.IsNotNull(loginToken);

        var scopedTenant = await EnterAcmeTenantAsync(
            client,
            loginToken.AccessToken,
            cancellationToken);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/units?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            scopedTenant.AccessToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<OrganizationUnitResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        Assert.AreEqual(1, page.Total);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual(visibleUnit.Id, page.Items[0].Id);

        using var hiddenRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/organization/units/{hiddenUnit.Id:D}");
        hiddenRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            scopedTenant.AccessToken);
        using var hiddenResponse = await client.SendAsync(hiddenRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, hiddenResponse.StatusCode);

        await CreateUserUnitAssignmentAsync(
            client,
            adminTenant.AccessToken,
            createdUser.Id,
            visibleUnit.Id,
            cancellationToken);
        using var updateSelfScopeRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{createdRole.Id:D}/data-scope",
            hostAdminToken,
            new UpdateHostRoleDataScopeRequest(
                RoleDataScopeKinds.Self,
                [],
                customScope.Version));
        using var updateSelfScopeResponse = await client.SendAsync(
            updateSelfScopeRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateSelfScopeResponse.StatusCode);
        var selfScope = await updateSelfScopeResponse.Content
            .ReadFromJsonAsync<HostRoleDataScopeResponse>(cancellationToken);
        Assert.IsNotNull(selfScope);
        Assert.AreEqual(RoleDataScopeKinds.Self, selfScope.DataScopeKind);

        using var selfListRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/units?page=1&pageSize=20");
        selfListRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            scopedTenant.AccessToken);
        using var selfListResponse = await client.SendAsync(
            selfListRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, selfListResponse.StatusCode);
        var selfPage = await selfListResponse.Content
            .ReadFromJsonAsync<PagedResult<OrganizationUnitResponse>>(cancellationToken);
        Assert.IsNotNull(selfPage);
        Assert.AreEqual(1, selfPage.Total);
        Assert.AreEqual(visibleUnit.Id, selfPage.Items.Single().Id);
    }

    private static async Task VerifyTenantUserUnitListDataScopeFilteringAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var hostAdminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var adminTenant = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var visibleCode = $"scope-u-visible-{Guid.NewGuid():N}".ToLowerInvariant();
        var hiddenCode = $"scope-u-hidden-{Guid.NewGuid():N}".ToLowerInvariant();
        var visibleUnit = await CreateUnitAsync(
            client,
            adminTenant.AccessToken,
            visibleCode,
            "隶属可见机构",
            cancellationToken);
        var hiddenUnit = await CreateUnitAsync(
            client,
            adminTenant.AccessToken,
            hiddenCode,
            "隶属隐藏机构",
            cancellationToken);
        hostAdminToken = await LoginAsHostAdminAsync(client, cancellationToken);

        using var usersRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=20");
        usersRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostAdminToken);
        using var usersResponse = await client.SendAsync(usersRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, usersResponse.StatusCode);
        var usersPage = await usersResponse.Content
            .ReadFromJsonAsync<PagedResult<HostUserResponse>>(cancellationToken);
        Assert.IsNotNull(usersPage);
        var adminUser = usersPage.Items.Single(user => user.Username == "admin");

        await CreateUserUnitAssignmentAsync(
            client,
            adminTenant.AccessToken,
            adminUser.Id,
            visibleUnit.Id,
            cancellationToken);
        await CreateUserUnitAssignmentAsync(
            client,
            adminTenant.AccessToken,
            adminUser.Id,
            hiddenUnit.Id,
            cancellationToken);

        var roleCode = $"scope-u-role-{Guid.NewGuid():N}".ToLowerInvariant();
        using var createRoleRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            hostAdminToken,
            new CreateHostRoleRequest(roleCode, "隶属数据范围角色"));
        using var createRoleResponse = await client.SendAsync(createRoleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createRoleResponse.StatusCode);
        var createdRole = await createRoleResponse.Content
            .ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(createdRole);

        using var updatePermissionsRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{createdRole.Id:D}/permissions",
            hostAdminToken,
            new ReplaceHostRolePermissionsRequest(
                [
                    OrganizationUserUnitManagementPermissions.Read,
                    TenancyTenantManagementPermissions.Read,
                    "tenancy.tenants.switch",
                    "platform.dashboard.read",
                ],
                createdRole.Version));
        using var updatePermissionsResponse = await client.SendAsync(
            updatePermissionsRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updatePermissionsResponse.StatusCode);
        var roleWithPermissions = await updatePermissionsResponse.Content
            .ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(roleWithPermissions);

        using var updateScopeRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{createdRole.Id:D}/data-scope",
            hostAdminToken,
            new UpdateHostRoleDataScopeRequest(
                RoleDataScopeKinds.Custom,
                [visibleUnit.Id],
                roleWithPermissions.Version,
                adminTenant.TenantId));
        using var updateScopeResponse = await client.SendAsync(updateScopeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateScopeResponse.StatusCode);

        var username = $"scope-u-user-{Guid.NewGuid():N}".ToLowerInvariant();
        using var createUserRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            hostAdminToken,
            new CreateHostUserRequest(
                username,
                "隶属范围受限用户",
                FullNetApiFactory.TestPassword));
        using var createUserResponse = await client.SendAsync(createUserRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createUserResponse.StatusCode);
        var createdUser = await createUserResponse.Content
            .ReadFromJsonAsync<HostUserResponse>(cancellationToken);
        Assert.IsNotNull(createdUser);

        using var getRolesRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/users/{createdUser.Id:D}/roles");
        getRolesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostAdminToken);
        using var getRolesResponse = await client.SendAsync(getRolesRequest, cancellationToken);
        var userRoles = await getRolesResponse.Content
            .ReadFromJsonAsync<HostUserRolesResponse>(cancellationToken);
        Assert.IsNotNull(userRoles);

        using var assignRoleRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/users/{createdUser.Id:D}/roles",
            hostAdminToken,
            new ReplaceHostUserRolesRequest([createdRole.Id], userRoles.Version));
        using var assignRoleResponse = await client.SendAsync(assignRoleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, assignRoleResponse.StatusCode);

        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        var loginToken = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
        Assert.IsNotNull(loginToken);

        var scopedTenant = await EnterAcmeTenantAsync(
            client,
            loginToken.AccessToken,
            cancellationToken);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/user-units?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            scopedTenant.AccessToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<OrganizationUserUnitResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        Assert.AreEqual(1, page.Total);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual(visibleUnit.Id, page.Items[0].UnitId);
    }

    private static async Task CreateUserUnitAssignmentAsync(
        HttpClient client,
        string tenantToken,
        Guid userId,
        Guid unitId,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-units",
            tenantToken,
            new CreateOrganizationUserUnitRequest(userId, unitId, false));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<OrganizationUnitResponse> CreateUnitAsync(
        HttpClient client,
        string tenantToken,
        string code,
        string name,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            tenantToken,
            new CreateOrganizationUnitRequest(null, code, name, 10));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<OrganizationUnitResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task<TenantSession> LoginAndEnterAcmeTenantAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var hostAccessToken = await LoginAsHostAdminAsync(client, cancellationToken);
        return await EnterAcmeTenantAsync(client, hostAccessToken, cancellationToken);
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
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }

    private sealed record TenantSession(Guid TenantId, string AccessToken);
}
