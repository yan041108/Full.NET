using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Organization;

/// <summary>
/// 租户用户-机构隶属纵向切片验收夹具。
/// </summary>
internal static class OrganizationUserUnitManagementAssertions
{
    public static async Task VerifyTenantUserUnitManagementContractAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionInTenantContextAsync(
            factory,
            client,
            cancellationToken);
        await VerifyCreateRejectsDuplicateAssignmentAsync(client, cancellationToken);
        await VerifyCustomAssignmentLifecycleAsync(client, cancellationToken);
        await VerifyExactOrganizationUserUnitActionPermissionBoundariesAsync(
            client,
            cancellationToken);
        await OpenApiOrganizationTenantUserUnitsContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionInTenantContextAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var tenantToken = await EnterAcmeTenantAsync(
            client,
            await factory.CreateHostAccessTokenAsync(
                [
                    "platform.dashboard.read",
                    "tenancy.tenants.read",
                    "tenancy.tenants.switch",
                ],
                cancellationToken),
            cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/user-units?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tenantToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());

        var readOnlyTenantToken = await EnterAcmeTenantAsync(
            client,
            await factory.CreateHostAccessTokenAsync(
                [
                    "platform.dashboard.read",
                    "tenancy.tenants.read",
                    "tenancy.tenants.switch",
                    OrganizationUserUnitManagementPermissions.Read,
                ],
                cancellationToken),
            cancellationToken);
        using var candidatesRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/user-units/assignable-users?page=1&pageSize=100");
        candidatesRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            readOnlyTenantToken);
        using var candidatesResponse = await client.SendAsync(
            candidatesRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, candidatesResponse.StatusCode);

        var createTenantToken = await EnterAcmeTenantWithRolePermissionsAsync(
            client,
            [
                OrganizationUserUnitManagementPermissions.Read,
                OrganizationUserUnitManagementPermissions.Create,
            ],
            cancellationToken);
        using var allowedCandidatesRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/user-units/assignable-users?page=1&pageSize=100");
        allowedCandidatesRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            createTenantToken);
        using var allowedCandidatesResponse = await client.SendAsync(
            allowedCandidatesRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, allowedCandidatesResponse.StatusCode);
    }

    private static async Task VerifyCreateRejectsDuplicateAssignmentAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var (userId, unitId) = await CreateFixtureUnitAndResolveAdminUserAsync(
            client,
            adminTenantToken,
            cancellationToken);
        var body = new CreateOrganizationUserUnitRequest(userId, unitId, true);

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-units",
            adminTenantToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-units",
            adminTenantToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            OrganizationErrorCodes.UserUnitAlreadyAssigned,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCustomAssignmentLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var (userId, unitId) = await CreateFixtureUnitAndResolveAdminUserAsync(
            client,
            adminTenantToken,
            cancellationToken);

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-units",
            adminTenantToken,
            new CreateOrganizationUserUnitRequest(userId, unitId, false));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content
            .ReadFromJsonAsync<OrganizationUserUnitResponse>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual("admin", created.Username);
        Assert.AreEqual("系统管理员", created.DisplayName);
        Assert.IsFalse(created.IsPrimary);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/user-units/{created.Id:D}",
            adminTenantToken,
            new UpdateOrganizationUserUnitRequest(true, created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content
            .ReadFromJsonAsync<OrganizationUserUnitResponse>(cancellationToken);
        Assert.IsNotNull(updated);
        Assert.IsTrue(updated.IsPrimary);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/organization/user-units/{created.Id:D}/disable",
            adminTenantToken,
            new { });
        using var disableResponse = await client.SendAsync(
            disableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content
            .ReadFromJsonAsync<OrganizationUserUnitResponse>(cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);
        Assert.IsFalse(disabled.IsPrimary);

        using var recreateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-units",
            adminTenantToken,
            new CreateOrganizationUserUnitRequest(userId, unitId, true));
        using var recreateResponse = await client.SendAsync(recreateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, recreateResponse.StatusCode);
        var recreated = await recreateResponse.Content
            .ReadFromJsonAsync<OrganizationUserUnitResponse>(cancellationToken);
        Assert.IsNotNull(recreated);
        Assert.AreEqual(created.Id, recreated.Id);
        Assert.IsTrue(recreated.IsActive);
        Assert.IsTrue(recreated.IsPrimary);
    }

    private static async Task<(Guid UserId, Guid UnitId)> CreateFixtureUnitAndResolveAdminUserAsync(
        HttpClient client,
        string adminTenantToken,
        CancellationToken cancellationToken)
    {
        using var currentUserRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/user-units/assignable-users?page=1&pageSize=100");
        currentUserRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminTenantToken);
        using var currentUserResponse = await client.SendAsync(
            currentUserRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, currentUserResponse.StatusCode);
        var candidates = await currentUserResponse.Content
            .ReadFromJsonAsync<PagedResult<OrganizationAssignableUserResponse>>(
                cancellationToken);
        Assert.IsNotNull(candidates);
        var admin = candidates.Items.Single(user => user.Username == "admin");
        Assert.IsNotNull(admin);
        Assert.AreEqual("admin", admin.Username);

        var code = $"unit-{Guid.NewGuid():N}".ToLowerInvariant();
        using var createUnitRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            adminTenantToken,
            new CreateOrganizationUnitRequest(null, code, "隶属测试机构", 10));
        using var createUnitResponse = await client.SendAsync(
            createUnitRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createUnitResponse.StatusCode);
        var unit = await createUnitResponse.Content.ReadFromJsonAsync<OrganizationUnitResponse>(
            cancellationToken);
        Assert.IsNotNull(unit);
        return (admin.Id, unit.Id);
    }

    private static async Task VerifyExactOrganizationUserUnitActionPermissionBoundariesAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var (userId, unitId) = await CreateFixtureUnitAndResolveAdminUserAsync(
            client,
            adminTenantToken,
            cancellationToken);

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-units",
            adminTenantToken,
            new CreateOrganizationUserUnitRequest(userId, unitId, false));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content
            .ReadFromJsonAsync<OrganizationUserUnitResponse>(cancellationToken);
        Assert.IsNotNull(created);

        var disableUnitCode = $"unit-{Guid.NewGuid():N}".ToLowerInvariant();
        using var disableUnitRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            adminTenantToken,
            new CreateOrganizationUnitRequest(null, disableUnitCode, "禁用边界机构", 10));
        using var disableUnitResponse = await client.SendAsync(
            disableUnitRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, disableUnitResponse.StatusCode);
        var disableUnit = await disableUnitResponse.Content
            .ReadFromJsonAsync<OrganizationUnitResponse>(cancellationToken);
        Assert.IsNotNull(disableUnit);

        using var disableTargetRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-units",
            adminTenantToken,
            new CreateOrganizationUserUnitRequest(userId, disableUnit.Id, false));
        using var disableTargetResponse = await client.SendAsync(
            disableTargetRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, disableTargetResponse.StatusCode);
        var disableTarget = await disableTargetResponse.Content
            .ReadFromJsonAsync<OrganizationUserUnitResponse>(cancellationToken);
        Assert.IsNotNull(disableTarget);

        var readOnlyToken = await EnterAcmeTenantWithRolePermissionsAsync(
            client,
            [OrganizationUserUnitManagementPermissions.Read],
            cancellationToken);
        await AssertOrganizationUserUnitPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Get,
            "/api/v1/organization/user-units/assignable-users?page=1&pageSize=100",
            cancellationToken);
        await AssertOrganizationUserUnitPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            "/api/v1/organization/user-units",
            cancellationToken,
            new CreateOrganizationUserUnitRequest(userId, unitId, false));
        await AssertOrganizationUserUnitPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            $"/api/v1/organization/user-units/{created.Id:D}",
            cancellationToken,
            new UpdateOrganizationUserUnitRequest(true, created.Version));
        await AssertOrganizationUserUnitPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            $"/api/v1/organization/user-units/{created.Id:D}/disable",
            cancellationToken);

        var createToken = await EnterAcmeTenantWithRolePermissionsAsync(
            client,
            [
                OrganizationUserUnitManagementPermissions.Read,
                OrganizationUserUnitManagementPermissions.Create,
            ],
            cancellationToken);
        using var assignableUsersRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/user-units/assignable-users?page=1&pageSize=100");
        assignableUsersRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            createToken);
        using var assignableUsersResponse = await client.SendAsync(
            assignableUsersRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, assignableUsersResponse.StatusCode);
        await AssertOrganizationUserUnitPermissionDeniedAsync(
            client,
            createToken,
            HttpMethod.Put,
            $"/api/v1/organization/user-units/{created.Id:D}",
            cancellationToken,
            new UpdateOrganizationUserUnitRequest(true, created.Version));
        await AssertOrganizationUserUnitPermissionDeniedAsync(
            client,
            createToken,
            HttpMethod.Post,
            $"/api/v1/organization/user-units/{created.Id:D}/disable",
            cancellationToken);

        var disableToken = await EnterAcmeTenantWithRolePermissionsAsync(
            client,
            [
                OrganizationUserUnitManagementPermissions.Read,
                OrganizationUserUnitManagementPermissions.Disable,
            ],
            cancellationToken);
        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/organization/user-units/{disableTarget.Id:D}/disable",
            disableToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
    }

    private static async Task AssertOrganizationUserUnitPermissionDeniedAsync(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            CommonErrorCodes.PermissionDenied,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<string> EnterAcmeTenantWithRolePermissionsAsync(
        HttpClient client,
        IReadOnlyCollection<string> organizationPermissions,
        CancellationToken cancellationToken)
    {
        var hostAdminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var roleCode = $"uunit-bound-{Guid.NewGuid():N}".ToLowerInvariant();
        var username = $"uunit-bound-{Guid.NewGuid():N}".ToLowerInvariant();
        var rolePermissions = new[]
            {
                "platform.dashboard.read",
                "tenancy.tenants.read",
                "tenancy.tenants.switch",
            }
            .Concat(organizationPermissions)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        using var createRoleRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            hostAdminToken,
            new CreateHostRoleRequest(roleCode, "用户机构隶属动作边界角色"));
        using var createRoleResponse = await client.SendAsync(createRoleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createRoleResponse.StatusCode);
        var createdRole = await createRoleResponse.Content
            .ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(createdRole);

        using var updatePermissionsRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{createdRole.Id:D}/permissions",
            hostAdminToken,
            new ReplaceHostRolePermissionsRequest(rolePermissions, createdRole.Version));
        using var updatePermissionsResponse = await client.SendAsync(
            updatePermissionsRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updatePermissionsResponse.StatusCode);

        using var createUserRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            hostAdminToken,
            new CreateHostUserRequest(
                username,
                "用户机构隶属动作边界用户",
                FullNetApiFactory.TestPassword));
        using var createUserResponse = await client.SendAsync(createUserRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createUserResponse.StatusCode);
        var createdUser = await createUserResponse.Content
            .ReadFromJsonAsync<HostUserResponse>(cancellationToken);
        Assert.IsNotNull(createdUser);

        using var getRolesRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/users/{createdUser.Id:D}/roles");
        getRolesRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            hostAdminToken);
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
            Content = JsonContent.Create(
                new LoginRequest(username, FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginToken = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(loginToken);

        return await EnterAcmeTenantAsync(client, loginToken.AccessToken, cancellationToken);
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

    private static async Task<string> LoginAndEnterAcmeTenantAsync(
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
        return await EnterAcmeTenantAsync(client, loginToken.AccessToken, cancellationToken);
    }

    private static async Task<string> EnterAcmeTenantAsync(
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
        return entered.AccessToken;
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
}
