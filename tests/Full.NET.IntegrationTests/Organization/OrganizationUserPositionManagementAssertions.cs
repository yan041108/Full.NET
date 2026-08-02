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
/// 租户用户-职位隶属纵向切片验收夹具。
/// </summary>
internal static class OrganizationUserPositionManagementAssertions
{
    public static async Task VerifyTenantUserPositionManagementContractAsync(
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
        await VerifyExactOrganizationUserPositionActionPermissionBoundariesAsync(
            client,
            cancellationToken);
        await OpenApiOrganizationTenantUserPositionsContractAssertions.VerifyAsync(
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
            "/api/v1/organization/user-positions?page=1&pageSize=20");
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
                    OrganizationUserPositionManagementPermissions.Read,
                ],
                cancellationToken),
            cancellationToken);
        using var candidatesRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/user-positions/assignable-users?page=1&pageSize=100");
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
                OrganizationUserPositionManagementPermissions.Read,
                OrganizationUserPositionManagementPermissions.Create,
            ],
            cancellationToken);
        using var allowedCandidatesRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/user-positions/assignable-users?page=1&pageSize=100");
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
        var (userId, positionId) = await CreateFixturePositionAndResolveAdminUserAsync(
            client,
            adminTenantToken,
            cancellationToken);
        var body = new CreateOrganizationUserPositionRequest(userId, positionId, true);

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-positions",
            adminTenantToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-positions",
            adminTenantToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            OrganizationErrorCodes.UserPositionAlreadyAssigned,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCustomAssignmentLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var (userId, positionId) = await CreateFixturePositionAndResolveAdminUserAsync(
            client,
            adminTenantToken,
            cancellationToken);

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-positions",
            adminTenantToken,
            new CreateOrganizationUserPositionRequest(userId, positionId, false));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content
            .ReadFromJsonAsync<OrganizationUserPositionResponse>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual("admin", created.Username);
        Assert.AreEqual("系统管理员", created.DisplayName);
        Assert.IsFalse(created.IsPrimary);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/user-positions/{created.Id:D}",
            adminTenantToken,
            new UpdateOrganizationUserPositionRequest(true, created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content
            .ReadFromJsonAsync<OrganizationUserPositionResponse>(cancellationToken);
        Assert.IsNotNull(updated);
        Assert.IsTrue(updated.IsPrimary);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/organization/user-positions/{created.Id:D}/disable",
            adminTenantToken,
            new { });
        using var disableResponse = await client.SendAsync(
            disableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content
            .ReadFromJsonAsync<OrganizationUserPositionResponse>(cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);
        Assert.IsFalse(disabled.IsPrimary);
    }

    private static async Task<(Guid UserId, Guid PositionId)> CreateFixturePositionAndResolveAdminUserAsync(
        HttpClient client,
        string adminTenantToken,
        CancellationToken cancellationToken)
    {
        using var currentUserRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/user-positions/assignable-users?page=1&pageSize=100");
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

        var code = $"pos-{Guid.NewGuid():N}"[..20].ToLowerInvariant();
        using var createPositionRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/positions",
            adminTenantToken,
            new CreateOrganizationPositionRequest(code, "隶属测试职位", 10));
        using var createPositionResponse = await client.SendAsync(
            createPositionRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createPositionResponse.StatusCode);
        var position = await createPositionResponse.Content
            .ReadFromJsonAsync<OrganizationPositionResponse>(cancellationToken);
        Assert.IsNotNull(position);
        return (admin.Id, position.Id);
    }

    private static async Task VerifyExactOrganizationUserPositionActionPermissionBoundariesAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var (userId, positionId) = await CreateFixturePositionAndResolveAdminUserAsync(
            client,
            adminTenantToken,
            cancellationToken);

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-positions",
            adminTenantToken,
            new CreateOrganizationUserPositionRequest(userId, positionId, false));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content
            .ReadFromJsonAsync<OrganizationUserPositionResponse>(cancellationToken);
        Assert.IsNotNull(created);

        var disablePositionCode = $"pos-{Guid.NewGuid():N}"[..20].ToLowerInvariant();
        using var disablePositionRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/positions",
            adminTenantToken,
            new CreateOrganizationPositionRequest(disablePositionCode, "禁用边界职位", 10));
        using var disablePositionResponse = await client.SendAsync(
            disablePositionRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, disablePositionResponse.StatusCode);
        var disablePosition = await disablePositionResponse.Content
            .ReadFromJsonAsync<OrganizationPositionResponse>(cancellationToken);
        Assert.IsNotNull(disablePosition);

        using var disableTargetRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/user-positions",
            adminTenantToken,
            new CreateOrganizationUserPositionRequest(userId, disablePosition.Id, false));
        using var disableTargetResponse = await client.SendAsync(
            disableTargetRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, disableTargetResponse.StatusCode);
        var disableTarget = await disableTargetResponse.Content
            .ReadFromJsonAsync<OrganizationUserPositionResponse>(cancellationToken);
        Assert.IsNotNull(disableTarget);

        var readOnlyToken = await EnterAcmeTenantWithRolePermissionsAsync(
            client,
            [OrganizationUserPositionManagementPermissions.Read],
            cancellationToken);
        await AssertOrganizationUserPositionPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Get,
            "/api/v1/organization/user-positions/assignable-users?page=1&pageSize=100",
            cancellationToken);
        await AssertOrganizationUserPositionPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            "/api/v1/organization/user-positions",
            cancellationToken,
            new CreateOrganizationUserPositionRequest(userId, positionId, false));
        await AssertOrganizationUserPositionPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            $"/api/v1/organization/user-positions/{created.Id:D}",
            cancellationToken,
            new UpdateOrganizationUserPositionRequest(true, created.Version));
        await AssertOrganizationUserPositionPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            $"/api/v1/organization/user-positions/{created.Id:D}/disable",
            cancellationToken);

        var createToken = await EnterAcmeTenantWithRolePermissionsAsync(
            client,
            [
                OrganizationUserPositionManagementPermissions.Read,
                OrganizationUserPositionManagementPermissions.Create,
            ],
            cancellationToken);
        using var assignableUsersRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/user-positions/assignable-users?page=1&pageSize=100");
        assignableUsersRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            createToken);
        using var assignableUsersResponse = await client.SendAsync(
            assignableUsersRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, assignableUsersResponse.StatusCode);
        await AssertOrganizationUserPositionPermissionDeniedAsync(
            client,
            createToken,
            HttpMethod.Put,
            $"/api/v1/organization/user-positions/{created.Id:D}",
            cancellationToken,
            new UpdateOrganizationUserPositionRequest(true, created.Version));
        await AssertOrganizationUserPositionPermissionDeniedAsync(
            client,
            createToken,
            HttpMethod.Post,
            $"/api/v1/organization/user-positions/{created.Id:D}/disable",
            cancellationToken);

        var disableToken = await EnterAcmeTenantWithRolePermissionsAsync(
            client,
            [
                OrganizationUserPositionManagementPermissions.Read,
                OrganizationUserPositionManagementPermissions.Disable,
            ],
            cancellationToken);
        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/organization/user-positions/{disableTarget.Id:D}/disable",
            disableToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
    }

    private static async Task AssertOrganizationUserPositionPermissionDeniedAsync(
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
        var roleCode = $"upos-bound-{Guid.NewGuid():N}".ToLowerInvariant();
        var username = $"upos-bound-{Guid.NewGuid():N}".ToLowerInvariant();
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
            new CreateHostRoleRequest(roleCode, "用户职位隶属动作边界角色"));
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
        var roleWithPermissions = await updatePermissionsResponse.Content
            .ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(roleWithPermissions);

        using var createUserRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            hostAdminToken,
            new CreateHostUserRequest(
                username,
                "用户职位隶属动作边界用户",
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
