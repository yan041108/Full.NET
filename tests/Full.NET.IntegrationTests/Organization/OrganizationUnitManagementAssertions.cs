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
/// 租户机构管理纵向切片验收夹具。
/// </summary>
internal static class OrganizationUnitManagementAssertions
{
    public static async Task VerifyTenantUnitManagementContractAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionInTenantContextAsync(
            factory,
            client,
            cancellationToken);
        await VerifyCreateRejectsDuplicateCodeAsync(client, cancellationToken);
        await VerifyCustomUnitLifecycleAsync(client, cancellationToken);
        await VerifyParentCycleIsRejectedAsync(client, cancellationToken);
        await VerifyExactOrganizationUnitActionPermissionBoundariesAsync(
            client,
            cancellationToken);
        await OpenApiOrganizationTenantUnitsContractAssertions.VerifyAsync(
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
            "/api/v1/organization/units?page=1&pageSize=20");
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
    }

    private static async Task VerifyCreateRejectsDuplicateCodeAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var code = $"unit-{Guid.NewGuid():N}".ToLowerInvariant();
        var body = new CreateOrganizationUnitRequest(null, code, "集成测试机构", 10);

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            adminTenantToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            adminTenantToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            OrganizationErrorCodes.UnitCodeExists,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCustomUnitLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var code = $"unit-{Guid.NewGuid():N}".ToLowerInvariant();

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            adminTenantToken,
            new CreateOrganizationUnitRequest(null, code, "生命周期机构", 10));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<OrganizationUnitResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/units/{created.Id:D}",
            adminTenantToken,
            new UpdateOrganizationUnitRequest(
                null,
                "已更新机构",
                created.DisplayOrder,
                created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<OrganizationUnitResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("已更新机构", updated.Name);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/organization/units/{created.Id:D}/disable",
            adminTenantToken,
            new { });
        using var disableResponse = await client.SendAsync(
            disableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<OrganizationUnitResponse>(
            cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);
    }

    private static async Task VerifyParentCycleIsRejectedAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var rootCode = $"root-{Guid.NewGuid():N}"[..14].ToLowerInvariant();
        var childCode = $"child-{Guid.NewGuid():N}"[..14].ToLowerInvariant();

        using var rootRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            adminTenantToken,
            new CreateOrganizationUnitRequest(null, rootCode, "环检测根", 10));
        using var rootResponse = await client.SendAsync(rootRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, rootResponse.StatusCode);
        var root = await rootResponse.Content.ReadFromJsonAsync<OrganizationUnitResponse>(
            cancellationToken);
        Assert.IsNotNull(root);

        using var childRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            adminTenantToken,
            new CreateOrganizationUnitRequest(root.Id.ToString("D"), childCode, "环检测子", 20));
        using var childResponse = await client.SendAsync(childRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, childResponse.StatusCode);
        var child = await childResponse.Content.ReadFromJsonAsync<OrganizationUnitResponse>(
            cancellationToken);
        Assert.IsNotNull(child);
        Assert.AreEqual(root.Id, child.ParentId);

        using var cycleRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/organization/units/{root.Id:D}",
            adminTenantToken,
            new UpdateOrganizationUnitRequest(
                child.Id.ToString("D"),
                root.Name,
                root.DisplayOrder,
                root.Version));
        using var cycleResponse = await client.SendAsync(cycleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, cycleResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await cycleResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            OrganizationErrorCodes.UnitParentCycle,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyExactOrganizationUnitActionPermissionBoundariesAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var code = $"bound-{Guid.NewGuid():N}"[..14].ToLowerInvariant();
        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            adminTenantToken,
            new CreateOrganizationUnitRequest(null, code, "边界测试机构", 10));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<OrganizationUnitResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        var disableCode = $"dis-{Guid.NewGuid():N}"[..14].ToLowerInvariant();
        using var disableTargetRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/organization/units",
            adminTenantToken,
            new CreateOrganizationUnitRequest(null, disableCode, "禁用边界机构", 10));
        using var disableTargetResponse = await client.SendAsync(
            disableTargetRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, disableTargetResponse.StatusCode);
        var disableTarget = await disableTargetResponse.Content.ReadFromJsonAsync<OrganizationUnitResponse>(
            cancellationToken);
        Assert.IsNotNull(disableTarget);

        var readOnlyToken = await EnterAcmeTenantWithRolePermissionsAsync(
            client,
            [OrganizationUnitManagementPermissions.Read],
            cancellationToken);
        await AssertOrganizationUnitPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            "/api/v1/organization/units",
            cancellationToken,
            new CreateOrganizationUnitRequest(null, "denied", "拒绝", 10));
        await AssertOrganizationUnitPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            $"/api/v1/organization/units/{created.Id:D}",
            cancellationToken,
            new UpdateOrganizationUnitRequest(
                null,
                "拒绝更新",
                created.DisplayOrder,
                created.Version));
        await AssertOrganizationUnitPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            $"/api/v1/organization/units/{created.Id:D}/disable",
            cancellationToken);

        var createToken = await EnterAcmeTenantWithRolePermissionsAsync(
            client,
            [
                OrganizationUnitManagementPermissions.Read,
                OrganizationUnitManagementPermissions.Create,
            ],
            cancellationToken);
        await AssertOrganizationUnitPermissionDeniedAsync(
            client,
            createToken,
            HttpMethod.Put,
            $"/api/v1/organization/units/{created.Id:D}",
            cancellationToken,
            new UpdateOrganizationUnitRequest(
                null,
                "拒绝更新",
                created.DisplayOrder,
                created.Version));
        await AssertOrganizationUnitPermissionDeniedAsync(
            client,
            createToken,
            HttpMethod.Post,
            $"/api/v1/organization/units/{created.Id:D}/disable",
            cancellationToken);

        var disableToken = await EnterAcmeTenantWithRolePermissionsAsync(
            client,
            [
                OrganizationUnitManagementPermissions.Read,
                OrganizationUnitManagementPermissions.Disable,
            ],
            cancellationToken);
        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/organization/units/{disableTarget.Id:D}/disable",
            disableToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
    }

    private static async Task AssertOrganizationUnitPermissionDeniedAsync(
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
        var roleCode = $"unit-bound-{Guid.NewGuid():N}".ToLowerInvariant();
        var username = $"unit-bound-{Guid.NewGuid():N}".ToLowerInvariant();
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
            new CreateHostRoleRequest(roleCode, "机构动作边界角色"));
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
                "机构动作边界用户",
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
