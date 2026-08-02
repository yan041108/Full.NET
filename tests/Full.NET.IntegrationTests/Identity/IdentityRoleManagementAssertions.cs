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
        await VerifyCreateRejectsDuplicateCodeAsync(client, cancellationToken);
        await VerifySystemRoleUpdateRejectedAsync(client, cancellationToken);
        await VerifyCustomRoleLifecycleAsync(client, cancellationToken);
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
