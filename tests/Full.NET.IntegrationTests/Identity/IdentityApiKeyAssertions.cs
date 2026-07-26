using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// Host API Key 管理端点与 ApiKey 认证纵向切片验收夹具。
/// </summary>
internal static class IdentityApiKeyAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyCreateAuthenticateAndDisableAsync(
            factory,
            client,
            cancellationToken);
        await VerifyDelegatedManagerCannotExceedEffectivePermissionsAsync(
            client,
            cancellationToken);
        await OpenApiIdentityApiKeysContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/api-keys?page=1&pageSize=20");
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

    private static async Task VerifyCreateAuthenticateAndDisableAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var adminUserId = await ResolveAdminUserIdAsync(client, adminToken, cancellationToken);

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/api-keys",
            adminToken,
            new CreateHostApiKeyRequest(
                adminUserId,
                "集成测试密钥",
                [IdentityUserManagementPermissions.Read],
                null));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content
            .ReadFromJsonAsync<CreateHostApiKeyResponse>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.IsFalse(string.IsNullOrWhiteSpace(created.Secret));
        Assert.AreEqual("集成测试密钥", created.Key.DisplayName);
        Assert.IsTrue(created.Key.IsActive);

        using var authorizedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1");
        authorizedRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "ApiKey",
            created.Secret);
        using var authorizedResponse = await client.SendAsync(
            authorizedRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, authorizedResponse.StatusCode);
        var firstLastUsedAtUtc = await ReadLastUsedAtUtcAsync(
            factory,
            created.Key.Id,
            cancellationToken);
        Assert.IsNotNull(firstLastUsedAtUtc);

        using var secondAuthorizedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1");
        secondAuthorizedRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "ApiKey",
            created.Secret);
        using var secondAuthorizedResponse = await client.SendAsync(
            secondAuthorizedRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondAuthorizedResponse.StatusCode);
        Assert.AreEqual(
            firstLastUsedAtUtc,
            await ReadLastUsedAtUtcAsync(
                factory,
                created.Key.Id,
                cancellationToken));

        using var forbiddenRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/identity/users")
        {
            Content = JsonContent.Create(new CreateHostUserRequest(
                $"apikey-{Guid.NewGuid():N}",
                "应被拒绝",
                FullNetApiFactory.TestPassword)),
        };
        forbiddenRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "ApiKey",
            created.Secret);
        using var forbiddenResponse = await client.SendAsync(
            forbiddenRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/api-keys?page=1&pageSize=20&userId={adminUserId:D}");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<HostApiKeyResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == created.Key.Id));

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/api-keys/{created.Key.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);

        using var revokedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1");
        revokedRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "ApiKey",
            created.Secret);
        using var revokedResponse = await client.SendAsync(revokedRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, revokedResponse.StatusCode);
    }

    private static async Task VerifyDelegatedManagerCannotExceedEffectivePermissionsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var adminUserId = await ResolveAdminUserIdAsync(
            client,
            adminToken,
            cancellationToken);
        var username = $"api-key-manager-{Guid.NewGuid():N}".ToLowerInvariant();

        using var createUserRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(
                username,
                "API Key 委派管理员",
                FullNetApiFactory.TestPassword));
        using var createUserResponse = await client.SendAsync(
            createUserRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createUserResponse.StatusCode);
        var user = await createUserResponse.Content.ReadFromJsonAsync<HostUserResponse>(
            cancellationToken);
        Assert.IsNotNull(user);

        using var createRoleRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/roles",
            adminToken,
            new CreateHostRoleRequest(
                $"api-key-manager-{Guid.NewGuid():N}".ToLowerInvariant(),
                "API Key 委派角色"));
        using var createRoleResponse = await client.SendAsync(
            createRoleRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createRoleResponse.StatusCode);
        var role = await createRoleResponse.Content.ReadFromJsonAsync<HostRoleResponse>(
            cancellationToken);
        Assert.IsNotNull(role);

        using var grantPermissionRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{role.Id:D}/permissions",
            adminToken,
            new ReplaceHostRolePermissionsRequest(
                [
                    IdentityApiKeyManagementPermissions.Write,
                    IdentityUserManagementPermissions.Write,
                ],
                role.Version));
        using var grantPermissionResponse = await client.SendAsync(
            grantPermissionRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, grantPermissionResponse.StatusCode);
        var grantedRole = await grantPermissionResponse.Content
            .ReadFromJsonAsync<HostRoleResponse>(cancellationToken);
        Assert.IsNotNull(grantedRole);

        using var currentRolesRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/users/{user.Id:D}/roles");
        currentRolesRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var currentRolesResponse = await client.SendAsync(
            currentRolesRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, currentRolesResponse.StatusCode);
        var currentRoles = await currentRolesResponse.Content
            .ReadFromJsonAsync<HostUserRolesResponse>(cancellationToken);
        Assert.IsNotNull(currentRoles);

        using var assignRoleRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/users/{user.Id:D}/roles",
            adminToken,
            new ReplaceHostUserRolesRequest([role.Id], currentRoles.Version));
        using var assignRoleResponse = await client.SendAsync(
            assignRoleRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, assignRoleResponse.StatusCode);

        var delegatedToken = await LoginAsync(
            client,
            username,
            FullNetApiFactory.TestPassword,
            cancellationToken);
        using var revokePermissionRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/roles/{role.Id:D}/permissions",
            adminToken,
            new ReplaceHostRolePermissionsRequest(
                [IdentityApiKeyManagementPermissions.Write],
                grantedRole.Version));
        using var revokePermissionResponse = await client.SendAsync(
            revokePermissionRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revokePermissionResponse.StatusCode);

        using var escalationRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/api-keys",
            delegatedToken,
            new CreateHostApiKeyRequest(
                adminUserId,
                "越权密钥",
                [IdentityUserManagementPermissions.Write],
                null));
        using var escalationResponse = await client.SendAsync(
            escalationRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, escalationResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await escalationResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            CommonErrorCodes.PermissionDenied,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<Guid> ResolveAdminUserIdAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=50");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content
            .ReadFromJsonAsync<PagedResult<HostUserResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        var admin = page.Items.SingleOrDefault(item => item.Username == "admin");
        Assert.IsNotNull(admin);
        return admin.Id;
    }

    private static async Task<DateTimeOffset?> ReadLastUsedAtUtcAsync(
        FullNetApiFactory factory,
        Guid apiKeyId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<DateTimeOffset?>(
                new SqlStatement(
                    "integration.identity.read_api_key_last_used",
                    """
                    SELECT LastUsedAtUtc
                    FROM fn_identity_api_key
                    WHERE Id = @ApiKeyId
                    """,
                    SqlDataScope.Global),
                new { ApiKeyId = apiKeyId },
                cancellationToken);
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken) =>
        await LoginAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);

    private static async Task<string> LoginAsync(
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
