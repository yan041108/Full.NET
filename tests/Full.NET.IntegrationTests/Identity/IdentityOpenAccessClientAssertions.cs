using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// OpenAccess 接入方应用管理端点与 ApiKey 认证纵向切片验收夹具。
/// </summary>
internal static class IdentityOpenAccessClientAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyCreateAuthenticateRotateAndDisableAsync(client, cancellationToken);
        await OpenApiIdentityOpenAccessClientsContractAssertions.VerifyAsync(
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
            "/api/v1/identity/open-access-clients?page=1&pageSize=20");
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

    private static async Task VerifyCreateAuthenticateRotateAndDisableAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var adminUserId = await ResolveAdminUserIdAsync(client, adminToken, cancellationToken);

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/open-access-clients",
            adminToken,
            new CreateOpenAccessClientRequest(
                adminUserId,
                "集成测试接入方",
                "用于 OpenAccess 集成测试",
                "备注",
                [IdentityUserManagementPermissions.Read],
                null));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content
            .ReadFromJsonAsync<CreateOpenAccessClientResponse>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.IsFalse(string.IsNullOrWhiteSpace(created.Secret));
        Assert.AreEqual("集成测试接入方", created.Client.Name);
        Assert.IsTrue(created.Client.IsActive);
        Assert.StartsWith("fnoa_", created.Client.AccessKeyId, StringComparison.Ordinal);

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

        using var rotateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/open-access-clients/{created.Client.Id:D}/rotate",
            adminToken,
            new { });
        using var rotateResponse = await client.SendAsync(rotateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, rotateResponse.StatusCode);
        var rotated = await rotateResponse.Content
            .ReadFromJsonAsync<CreateOpenAccessClientResponse>(cancellationToken);
        Assert.IsNotNull(rotated);
        Assert.AreNotEqual(created.Secret, rotated.Secret);
        Assert.AreNotEqual(created.Client.ApiKeyId, rotated.Client.ApiKeyId);
        Assert.AreEqual(created.Client.Id, rotated.Client.Id);

        using var oldKeyRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1");
        oldKeyRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "ApiKey",
            created.Secret);
        using var oldKeyResponse = await client.SendAsync(oldKeyRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldKeyResponse.StatusCode);

        using var newKeyRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1");
        newKeyRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "ApiKey",
            rotated.Secret);
        using var newKeyResponse = await client.SendAsync(newKeyRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, newKeyResponse.StatusCode);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/identity/open-access-clients/{rotated.Client.Id:D}",
            adminToken,
            new UpdateOpenAccessClientRequest(
                "更新后的接入方",
                "更新描述",
                "更新备注",
                [IdentityUserManagementPermissions.Read],
                null,
                rotated.Client.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content
            .ReadFromJsonAsync<OpenAccessClientResponse>(cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("更新后的接入方", updated.Name);
        Assert.AreEqual(rotated.Client.Version + 1, updated.Version);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/open-access-clients/{updated.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content
            .ReadFromJsonAsync<OpenAccessClientResponse>(cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);

        using var revokedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1");
        revokedRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "ApiKey",
            rotated.Secret);
        using var revokedResponse = await client.SendAsync(revokedRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, revokedResponse.StatusCode);
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

    private static HttpRequestMessage CreateBearerJsonRequest<T>(
        HttpMethod method,
        string path,
        string accessToken,
        T body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }
}
