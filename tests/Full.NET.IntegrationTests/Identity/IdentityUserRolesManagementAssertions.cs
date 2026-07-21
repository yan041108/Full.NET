using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// Host 用户角色分配纵向切片验收夹具。
/// </summary>
internal static class IdentityUserRolesManagementAssertions
{
    public static async Task VerifyHostUserRolesContractAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifySystemRoleAssignmentRejectedAsync(client, cancellationToken);
        await VerifyCustomRoleAssignmentLifecycleAsync(client, cancellationToken);
        await OpenApiHostUserRolesContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifySystemRoleAssignmentRejectedAsync(
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

        using var usersRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=20");
        usersRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var usersResponse = await client.SendAsync(usersRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, usersResponse.StatusCode);
        var usersPage = await usersResponse.Content
            .ReadFromJsonAsync<PagedResult<HostUserResponse>>(cancellationToken);
        Assert.IsNotNull(usersPage);
        var adminUser = usersPage.Items.First(user => user.Username == "admin");

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/users/{adminUser.Id:D}/roles",
            adminToken,
            new ReplaceHostUserRolesRequest([systemRole.Id], adminUser.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, updateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await updateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.UserRolesRoleNotAssignable,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCustomRoleAssignmentLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"user-{Guid.NewGuid():N}".ToLowerInvariant();
        var roleCode = $"role-{Guid.NewGuid():N}".ToLowerInvariant();

        using var createUserRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(
                username,
                "角色分配测试用户",
                FullNetApiFactory.TestPassword));
        using var createUserResponse = await client.SendAsync(createUserRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createUserResponse.StatusCode);
        var createdUser = await createUserResponse.Content
            .ReadFromJsonAsync<HostUserResponse>(cancellationToken);
        Assert.IsNotNull(createdUser);

        using var getDefaultRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/users/{createdUser.Id:D}/roles");
        getDefaultRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var getDefaultResponse = await client.SendAsync(getDefaultRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getDefaultResponse.StatusCode);
        var defaultRoles = await getDefaultResponse.Content
            .ReadFromJsonAsync<HostUserRolesResponse>(cancellationToken);
        Assert.IsNotNull(defaultRoles);
        Assert.AreEqual(0, defaultRoles.RoleIds.Count);

        using var createRoleRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            adminToken,
            new CreateHostRoleRequest(roleCode, "角色分配测试角色"));
        using var createRoleResponse = await client.SendAsync(createRoleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createRoleResponse.StatusCode);
        var createdRole = await createRoleResponse.Content
            .ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(createdRole);

        using var updateRolesRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/users/{createdUser.Id:D}/roles",
            adminToken,
            new ReplaceHostUserRolesRequest([createdRole.Id], defaultRoles.Version));
        using var updateRolesResponse = await client.SendAsync(updateRolesRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateRolesResponse.StatusCode);
        var updatedRoles = await updateRolesResponse.Content
            .ReadFromJsonAsync<HostUserRolesResponse>(cancellationToken);
        Assert.IsNotNull(updatedRoles);
        Assert.AreEqual(1, updatedRoles.RoleIds.Count);
        Assert.AreEqual(createdRole.Id, updatedRoles.RoleIds[0]);
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
