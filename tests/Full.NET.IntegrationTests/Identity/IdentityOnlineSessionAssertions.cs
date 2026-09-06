using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// Host 在线会话查询与强制下线纵向切片验收夹具。
/// </summary>
internal static class IdentityOnlineSessionAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyRevokeInvalidatesAccessTokenAsync(client, cancellationToken);
        await VerifyRevokeAllOnlyTargetsRequestedUserAsync(client, cancellationToken);
        await VerifyExactSessionRevokePermissionBoundariesAsync(factory, client, cancellationToken);
        await VerifySessionPolicyRequiresReadPermissionAsync(factory, client, cancellationToken);
        await OpenApiIdentityOnlineSessionsContractAssertions.VerifyAsync(
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
            "/api/v1/identity/online-sessions?page=1&pageSize=20");
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

    private static async Task VerifyRevokeInvalidatesAccessTokenAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"online-{Guid.NewGuid():N}";
        var password = Api.FullNetApiFactory.TestPassword;

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "在线测试", password));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);

        var victimToken = await LoginAsync(client, username, password, cancellationToken);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/online-sessions?page=1&pageSize=50&usernameContains={username}");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<HostOnlineSessionResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        Assert.AreEqual(1, page.Total);
        var targetSession = page.Items.Single(item => item.Username == username);

        using var protectedBeforeRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/me");
        protectedBeforeRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            victimToken);
        using var protectedBeforeResponse = await client.SendAsync(
            protectedBeforeRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, protectedBeforeResponse.StatusCode);

        using var revokeRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/online-sessions/{targetSession.Id:D}/revoke",
            adminToken,
            new { });
        using var revokeResponse = await client.SendAsync(revokeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revokeResponse.StatusCode);

        using var protectedAfterRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/me");
        protectedAfterRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            victimToken);
        using var protectedAfterResponse = await client.SendAsync(
            protectedAfterRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, protectedAfterResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await protectedAfterResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.SessionNotActive,
            problem.RootElement.GetProperty("code").GetString());
    }

    public static async Task VerifySingleSessionPolicyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"single-{Guid.NewGuid():N}";
        var password = Api.FullNetApiFactory.TestPassword;

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "单端测试", password));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);

        var firstToken = await LoginAsync(client, username, password, cancellationToken);
        var secondToken = await LoginAsync(client, username, password, cancellationToken);

        using var firstMeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        firstMeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", firstToken);
        using var firstMeResponse = await client.SendAsync(firstMeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, firstMeResponse.StatusCode);

        using var secondMeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        secondMeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secondToken);
        using var secondMeResponse = await client.SendAsync(secondMeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondMeResponse.StatusCode);
    }

    private static async Task VerifyRevokeAllOnlyTargetsRequestedUserAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var firstUsername = $"revoke-all-a-{Guid.NewGuid():N}";
        var secondUsername = $"revoke-all-b-{Guid.NewGuid():N}";
        var password = Api.FullNetApiFactory.TestPassword;

        foreach (var username in new[] { firstUsername, secondUsername })
        {
            using var createRequest = CreateBearerJsonRequest(
                HttpMethod.Post,
                "/api/v1/identity/users",
                adminToken,
                new CreateHostUserRequest(username, "批量下线", password));
            using var createResponse = await client.SendAsync(createRequest, cancellationToken);
            Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
            await LoginAsync(client, username, password, cancellationToken);
        }

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/online-sessions?page=1&pageSize=50&usernameContains={firstUsername}");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<HostOnlineSessionResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        var targetUserId = page.Items.Single(item => item.Username == firstUsername).UserId;

        using var revokeAllRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/online-sessions/users/{targetUserId:D}/revoke-all",
            adminToken,
            new { });
        using var revokeAllResponse = await client.SendAsync(revokeAllRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revokeAllResponse.StatusCode);
        var revokeAllResult = await revokeAllResponse.Content
            .ReadFromJsonAsync<RevokeAllHostUserSessionsResponse>(cancellationToken);
        Assert.IsNotNull(revokeAllResult);
        Assert.AreEqual(1, revokeAllResult.RevokedSessionCount);

        using var secondListRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/online-sessions?page=1&pageSize=50&usernameContains={secondUsername}");
        secondListRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var secondListResponse = await client.SendAsync(secondListRequest, cancellationToken);
        var secondPage = await secondListResponse.Content
            .ReadFromJsonAsync<PagedResult<HostOnlineSessionResponse>>(cancellationToken);
        Assert.IsNotNull(secondPage);
        Assert.AreEqual(1, secondPage.Total);
    }

    private static async Task VerifySessionPolicyRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/session-policy");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await factory.CreateHostAccessTokenAsync(
                [IdentitySessionManagementPermissions.Read],
                cancellationToken));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var policy = await response.Content.ReadFromJsonAsync<IdentitySessionPolicyResponse>(
            cancellationToken);
        Assert.IsNotNull(policy);
        Assert.AreEqual(IdentitySessionLoginPolicy.AllowMultiple, policy.LoginPolicy);
    }

    private static async Task VerifyExactSessionRevokePermissionBoundariesAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var username = $"boundary-{Guid.NewGuid():N}";
        var password = Api.FullNetApiFactory.TestPassword;

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/users",
            adminToken,
            new CreateHostUserRequest(username, "边界测试", password));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        await LoginAsync(client, username, password, cancellationToken);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/online-sessions?page=1&pageSize=50&usernameContains={username}");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        var page = await listResponse.Content
            .ReadFromJsonAsync<PagedResult<HostOnlineSessionResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        var targetSession = page.Items.Single(item => item.Username == username);

        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [IdentitySessionManagementPermissions.Read],
            cancellationToken);
        using var deniedRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/online-sessions/{targetSession.Id:D}/revoke",
            readOnlyToken,
            new { });
        using var deniedResponse = await client.SendAsync(deniedRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var revokeToken = await factory.CreateHostAccessTokenAsync(
            [
                IdentitySessionManagementPermissions.Read,
                IdentitySessionManagementPermissions.Revoke,
            ],
            cancellationToken);
        using var allowedRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/online-sessions/{targetSession.Id:D}/revoke",
            revokeToken,
            new { });
        using var allowedResponse = await client.SendAsync(allowedRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, allowedResponse.StatusCode);
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken) =>
        await LoginAsync(
            client,
            "admin",
            Api.FullNetApiFactory.TestPassword,
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
