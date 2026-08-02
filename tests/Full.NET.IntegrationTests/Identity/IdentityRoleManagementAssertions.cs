using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// Host 角色管理纵向切片验收夹具。
/// </summary>
internal static class IdentityRoleManagementAssertions
{
    public static async Task VerifyHostRoleManagementContractAsync(
        Api.FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyAuthorizationTreeRequiresRolesReadAsync(factory, client, cancellationToken);
        await VerifyAuthorizationTreeReturnsUsersActionsAsync(client, cancellationToken);
        await VerifyPageActionGrantHierarchyAsync(client, cancellationToken);
        await VerifyRolePermissionRevocationDeniesActionAsync(client, cancellationToken);
        await VerifyCreateRejectsDuplicateCodeAsync(client, cancellationToken);
        await VerifySystemRoleUpdateRejectedAsync(client, cancellationToken);
        await VerifyCustomRoleLifecycleAsync(client, cancellationToken);
        await VerifyExactRoleActionPermissionBoundariesAsync(
            factory,
            client,
            cancellationToken);
        await OpenApiHostRolesContractAssertions.VerifyAsync(
            client,
            cancellationToken);
        await OpenApiAuthorizationTreeContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyAuthorizationTreeRequiresRolesReadAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/authorization-tree");
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

    private static async Task VerifyAuthorizationTreeReturnsUsersActionsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/authorization-tree");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var usersPage = document.RootElement.EnumerateArray()
            .Single(element => element.GetProperty("id").GetString() == "users");
        Assert.AreEqual(
            "identity.users.read",
            usersPage.GetProperty("permissionCode").GetString());
        var actionCodes = usersPage.GetProperty("actions")
            .EnumerateArray()
            .Select(element => element.GetProperty("permissionCode").GetString())
            .ToArray();
        CollectionAssert.Contains(actionCodes, "identity.users.reset_password");
        Assert.IsFalse(document.RootElement.EnumerateArray().Any(
            element => element.GetProperty("id").GetString() == "super-administrators"));
    }

    private static async Task VerifyPageActionGrantHierarchyAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var code = $"action-hierarchy-{Guid.NewGuid():N}".ToLowerInvariant();

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            adminToken,
            new CreateHostRoleRequest(code, "操作层级角色"));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostRoleResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var orphanRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{created.Id:D}/permissions",
            adminToken,
            new ReplaceHostRolePermissionsRequest(
                [IdentityUserManagementPermissions.ResetPassword],
                created.Version));
        using var orphanResponse = await client.SendAsync(orphanRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, orphanResponse.StatusCode);
        using (var problem = JsonDocument.Parse(
                   await orphanResponse.Content.ReadAsStringAsync(cancellationToken)))
        {
            Assert.AreEqual(
                IdentityErrorCodes.ActionRequiresPage,
                problem.RootElement.GetProperty("code").GetString());
        }

        using var validRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{created.Id:D}/permissions",
            adminToken,
            new ReplaceHostRolePermissionsRequest(
                [
                    IdentityUserManagementPermissions.Read,
                    IdentityUserManagementPermissions.ResetPassword,
                ],
                created.Version));
        using var validResponse = await client.SendAsync(validRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, validResponse.StatusCode);
        var withPermissions = await validResponse.Content
            .ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(withPermissions);
        CollectionAssert.AreEqual(
            new[]
            {
                IdentityUserManagementPermissions.Read,
                IdentityUserManagementPermissions.ResetPassword,
            },
            withPermissions.PermissionCodes.ToArray());
    }

    private static async Task VerifyRolePermissionRevocationDeniesActionAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var roleCode = $"revoke-disable-{Guid.NewGuid():N}".ToLowerInvariant();
        var operatorUsername = $"revoke-operator-{Guid.NewGuid():N}".ToLowerInvariant();
        var targetUsername = $"revoke-target-{Guid.NewGuid():N}".ToLowerInvariant();
        var password = Api.FullNetApiFactory.TestPassword;

        using var createRoleRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            adminToken,
            new CreateHostRoleRequest(roleCode, "撤销禁用权限角色"));
        using var createRoleResponse = await client.SendAsync(createRoleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createRoleResponse.StatusCode);
        var role = await createRoleResponse.Content.ReadFromJsonAsync<HostRoleResponse>(
            cancellationToken);
        Assert.IsNotNull(role);

        using var grantRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{role.Id:D}/permissions",
            adminToken,
            new ReplaceHostRolePermissionsRequest(
                [
                    IdentityUserManagementPermissions.Read,
                    IdentityUserManagementPermissions.Disable,
                ],
                role.Version));
        using var grantResponse = await client.SendAsync(grantRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, grantResponse.StatusCode);
        var grantedRole = await grantResponse.Content.ReadFromJsonAsync<HostRoleResponse>(
            cancellationToken);
        Assert.IsNotNull(grantedRole);

        var operatorUser = await CreateHostUserForRoleTestAsync(
            client,
            adminToken,
            operatorUsername,
            password,
            cancellationToken);
        var targetUser = await CreateHostUserForRoleTestAsync(
            client,
            adminToken,
            targetUsername,
            password,
            cancellationToken);
        await AssignRoleToUserAsync(
            client,
            adminToken,
            operatorUser.Id,
            role.Id,
            cancellationToken);

        var operatorToken = await LoginAsUserAsync(
            client,
            operatorUsername,
            password,
            cancellationToken);
        using var allowedDisableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{targetUser.Id:D}/disable",
            operatorToken,
            new { });
        using var allowedDisableResponse = await client.SendAsync(
            allowedDisableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, allowedDisableResponse.StatusCode);

        using var revokeRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{role.Id:D}/permissions",
            adminToken,
            new ReplaceHostRolePermissionsRequest(
                [IdentityUserManagementPermissions.Read],
                grantedRole.Version));
        using var revokeResponse = await client.SendAsync(revokeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revokeResponse.StatusCode);

        var refreshedTarget = await CreateHostUserForRoleTestAsync(
            client,
            adminToken,
            $"revoke-target-2-{Guid.NewGuid():N}".ToLowerInvariant(),
            password,
            cancellationToken);
        using var deniedDisableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{refreshedTarget.Id:D}/disable",
            operatorToken,
            new { });
        using var deniedDisableResponse = await client.SendAsync(
            deniedDisableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, deniedDisableResponse.StatusCode);

        var refreshedToken = await LoginAsUserAsync(
            client,
            operatorUsername,
            password,
            cancellationToken);
        var postRevocationTarget = await CreateHostUserForRoleTestAsync(
            client,
            adminToken,
            $"revoke-target-3-{Guid.NewGuid():N}".ToLowerInvariant(),
            password,
            cancellationToken);
        using var forbiddenDisableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/users/{postRevocationTarget.Id:D}/disable",
            refreshedToken,
            new { });
        using var forbiddenDisableResponse = await client.SendAsync(
            forbiddenDisableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenDisableResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await forbiddenDisableResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<HostUserResponse> CreateHostUserForRoleTestAsync(
        HttpClient client,
        string adminToken,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "角色撤销测试用户", password));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<HostUserResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task AssignRoleToUserAsync(
        HttpClient client,
        string adminToken,
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        using var getRolesRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/users/{userId:D}/roles");
        getRolesRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var getRolesResponse = await client.SendAsync(getRolesRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getRolesResponse.StatusCode);
        var currentRoles = await getRolesResponse.Content
            .ReadFromJsonAsync<HostUserRolesResponse>(cancellationToken);
        Assert.IsNotNull(currentRoles);

        using var assignRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/users/{userId:D}/roles",
            adminToken,
            new ReplaceHostUserRolesRequest([roleId], currentRoles.Version));
        using var assignResponse = await client.SendAsync(assignRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, assignResponse.StatusCode);
    }

    private static async Task<string> LoginAsUserAsync(
        HttpClient client,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, password)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/roles?page=1&pageSize=20");
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

    private static async Task VerifyCreateRejectsDuplicateCodeAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var code = $"role-{Guid.NewGuid():N}".ToLowerInvariant();
        var body = new CreateHostRoleRequest(code, "集成测试角色");

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            adminToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        var created = await firstResponse.Content.ReadFromJsonAsync<HostRoleResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(code, created.Code);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            adminToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.RoleCodeExists,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifySystemRoleUpdateRejectedAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/roles?page=1&pageSize=50");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        using var listDocument = JsonDocument.Parse(
            await listResponse.Content.ReadAsStringAsync(cancellationToken));
        var items = listDocument.RootElement.GetProperty("items");
        Guid? hostAdministratorId = null;
        int? hostAdministratorVersion = null;
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("code").GetString() == "host-administrator")
            {
                hostAdministratorId = item.GetProperty("id").GetGuid();
                hostAdministratorVersion = item.GetProperty("version").GetInt32();
                break;
            }
        }

        Assert.IsNotNull(hostAdministratorId);
        Assert.IsNotNull(hostAdministratorVersion);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{hostAdministratorId:D}",
            adminToken,
            new UpdateHostRoleRequest("禁止修改", hostAdministratorVersion.Value));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, updateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await updateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.RoleSystemLocked,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCustomRoleLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var code = $"custom-{Guid.NewGuid():N}".ToLowerInvariant();

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            adminToken,
            new CreateHostRoleRequest(code, "生命周期角色"));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<HostRoleResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{created.Id:D}",
            adminToken,
            new UpdateHostRoleRequest("已更新角色", created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<HostRoleResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("已更新角色", updated.Name);

        using var permissionsRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{created.Id:D}/permissions",
            adminToken,
            new ReplaceHostRolePermissionsRequest(
                [IdentityUserManagementPermissions.Read],
                updated.Version));
        using var permissionsResponse = await client.SendAsync(
            permissionsRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, permissionsResponse.StatusCode);
        var withPermissions = await permissionsResponse.Content
            .ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(withPermissions);
        CollectionAssert.Contains(
            withPermissions.PermissionCodes.ToList(),
            IdentityUserManagementPermissions.Read);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/roles/{created.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(
            disableRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<HostRoleResponse>(
            cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);
    }

    private static async Task VerifyExactRoleActionPermissionBoundariesAsync(
        Api.FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var targetCode = $"boundary-target-{Guid.NewGuid():N}".ToLowerInvariant();

        using var createTargetRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            adminToken,
            new CreateHostRoleRequest(targetCode, "动作边界目标角色"));
        using var createTargetResponse = await client.SendAsync(createTargetRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createTargetResponse.StatusCode);
        var targetRole = await createTargetResponse.Content.ReadFromJsonAsync<HostRoleResponse>(
            cancellationToken);
        Assert.IsNotNull(targetRole);

        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [IdentityRoleManagementPermissions.Read],
            cancellationToken);
        await AssertRolesListAllowedAsync(client, readOnlyToken, cancellationToken);
        await AssertRolePermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            "/api/v1/identity/roles",
            new CreateHostRoleRequest($"denied-{Guid.NewGuid():N}", "拒绝创建"),
            cancellationToken);
        await AssertRolePermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            $"/api/v1/identity/roles/{targetRole.Id:D}",
            new UpdateHostRoleRequest("拒绝更新", targetRole.Version),
            cancellationToken);
        await AssertRolePermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            $"/api/v1/identity/roles/{targetRole.Id:D}/permissions",
            new ReplaceHostRolePermissionsRequest(
                [IdentityUserManagementPermissions.Read],
                targetRole.Version),
            cancellationToken);
        await AssertRolePermissionDeniedAsync<object?>(
            client,
            readOnlyToken,
            HttpMethod.Post,
            $"/api/v1/identity/roles/{targetRole.Id:D}/disable",
            null,
            cancellationToken);
        await AssertRolePermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            $"/api/v1/identity/roles/{targetRole.Id:D}/data-scope",
            new UpdateHostRoleDataScopeRequest(
                RoleDataScopeKinds.Self,
                null,
                targetRole.Version),
            cancellationToken);

        var createToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityRoleManagementPermissions.Read,
                IdentityRoleManagementPermissions.Create,
            ],
            cancellationToken);
        var createdByLimited = await CreateHostRoleWithTokenAsync(
            client,
            createToken,
            $"limited-create-{Guid.NewGuid():N}",
            cancellationToken);
        await AssertRolePermissionDeniedAsync(
            client,
            createToken,
            HttpMethod.Put,
            $"/api/v1/identity/roles/{targetRole.Id:D}",
            new UpdateHostRoleRequest("拒绝更新", targetRole.Version),
            cancellationToken);

        var updateToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityRoleManagementPermissions.Read,
                IdentityRoleManagementPermissions.Update,
            ],
            cancellationToken);
        await AssertRoleOkAsync(
            client,
            updateToken,
            HttpMethod.Put,
            $"/api/v1/identity/roles/{targetRole.Id:D}",
            new UpdateHostRoleRequest("受限更新名称", targetRole.Version),
            cancellationToken);
        await AssertRolePermissionDeniedAsync(
            client,
            updateToken,
            HttpMethod.Post,
            "/api/v1/identity/roles",
            new CreateHostRoleRequest($"denied-update-{Guid.NewGuid():N}", "拒绝创建"),
            cancellationToken);

        var assignPermissionsToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityRoleManagementPermissions.Read,
                IdentityRoleManagementPermissions.AssignPermissions,
            ],
            cancellationToken);
        await AssertRoleOkAsync(
            client,
            assignPermissionsToken,
            HttpMethod.Put,
            $"/api/v1/identity/roles/{createdByLimited.Id:D}/permissions",
            new ReplaceHostRolePermissionsRequest(
                [IdentityUserManagementPermissions.Read],
                createdByLimited.Version),
            cancellationToken);
        await AssertRolePermissionDeniedAsync<object?>(
            client,
            assignPermissionsToken,
            HttpMethod.Post,
            $"/api/v1/identity/roles/{createdByLimited.Id:D}/disable",
            null,
            cancellationToken);

        var disableTarget = await CreateHostRoleWithTokenAsync(
            client,
            adminToken,
            $"disable-target-{Guid.NewGuid():N}",
            cancellationToken);
        var disableToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityRoleManagementPermissions.Read,
                IdentityRoleManagementPermissions.Disable,
            ],
            cancellationToken);
        await AssertRoleOkAsync<object?>(
            client,
            disableToken,
            HttpMethod.Post,
            $"/api/v1/identity/roles/{disableTarget.Id:D}/disable",
            null,
            cancellationToken);
        await AssertRolePermissionDeniedAsync(
            client,
            disableToken,
            HttpMethod.Put,
            $"/api/v1/identity/roles/{disableTarget.Id:D}",
            new UpdateHostRoleRequest("拒绝更新", disableTarget.Version),
            cancellationToken);

        var dataScopeTarget = await CreateHostRoleWithTokenAsync(
            client,
            adminToken,
            $"scope-target-{Guid.NewGuid():N}",
            cancellationToken);
        var dataScopeToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentityRoleManagementPermissions.Read,
                IdentityRoleManagementPermissions.AssignDataScope,
            ],
            cancellationToken);
        await AssertRoleOkAsync(
            client,
            dataScopeToken,
            HttpMethod.Put,
            $"/api/v1/identity/roles/{dataScopeTarget.Id:D}/data-scope",
            new UpdateHostRoleDataScopeRequest(
                RoleDataScopeKinds.Self,
                null,
                dataScopeTarget.Version),
            cancellationToken);
        await AssertRolePermissionDeniedAsync(
            client,
            dataScopeToken,
            HttpMethod.Put,
            $"/api/v1/identity/roles/{dataScopeTarget.Id:D}/permissions",
            new ReplaceHostRolePermissionsRequest(
                [IdentityUserManagementPermissions.Read],
                dataScopeTarget.Version),
            cancellationToken);
    }

    private static async Task<HostRoleResponse> CreateHostRoleWithTokenAsync(
        HttpClient client,
        string accessToken,
        string code,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            accessToken,
            new CreateHostRoleRequest(code.ToLowerInvariant(), "动作边界角色"));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task AssertRolesListAllowedAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/roles?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertRolePermissionDeniedAsync<TRequest>(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        using var request = body is null
            ? new HttpRequestMessage(method, path)
            : CreateBearerJsonRequest(method, path, accessToken, body);
        if (body is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task AssertRoleOkAsync<TRequest>(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        TRequest? body,
        CancellationToken cancellationToken)
    {
        using var request = body is null
            ? new HttpRequestMessage(method, path)
            : CreateBearerJsonRequest(method, path, accessToken, body);
        if (body is null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
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
                new LoginRequest("admin", Api.FullNetApiFactory.TestPassword)),
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
