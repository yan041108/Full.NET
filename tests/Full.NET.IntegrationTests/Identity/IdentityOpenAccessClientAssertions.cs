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
        await VerifyObservabilityAndQuotaAsync(client, cancellationToken);
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
                null,
                2));
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
                2,
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

    private static async Task VerifyObservabilityAndQuotaAsync(
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
                "可观测性测试接入方",
                null,
                null,
                [IdentityUserManagementPermissions.Read],
                null,
                1));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content
            .ReadFromJsonAsync<CreateOpenAccessClientResponse>(cancellationToken);
        Assert.IsNotNull(created);

        using var firstAuthRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1");
        firstAuthRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "ApiKey",
            created.Secret);
        using var firstAuthResponse = await client.SendAsync(firstAuthRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, firstAuthResponse.StatusCode);

        using var usageRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/open-access-clients/{created.Client.Id:D}/usage");
        usageRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var usageResponse = await client.SendAsync(usageRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, usageResponse.StatusCode);
        var usage = await usageResponse.Content
            .ReadFromJsonAsync<OpenAccessClientUsageResponse>(cancellationToken);
        Assert.IsNotNull(usage);
        Assert.AreEqual(1, usage.DailyRequestQuota);
        Assert.IsTrue(usage.TodaySuccessCount >= 1);

        using var logsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/open-access-clients/{created.Client.Id:D}/access-logs?page=1&pageSize=20");
        logsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var logsResponse = await client.SendAsync(logsRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, logsResponse.StatusCode);
        var logs = await logsResponse.Content
            .ReadFromJsonAsync<PagedResult<OpenAccessClientAccessLogEntry>>(cancellationToken);
        Assert.IsNotNull(logs);
        Assert.IsTrue(logs.Total >= 1);
        Assert.IsTrue(logs.Items.Any(item =>
            item.EventType == IdentityOpenAccessClientAuditEventTypes.ApiKeyAuthentication
            && item.Succeeded));

        using var secondAuthRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=1");
        secondAuthRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "ApiKey",
            created.Secret);
        using var secondAuthResponse = await client.SendAsync(secondAuthRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, secondAuthResponse.StatusCode);

        using var debugRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/open-access-clients/{created.Client.Id:D}/signature-debug",
            adminToken,
            new OpenAccessClientSignatureDebugRequest(
                created.Secret,
                "GET",
                "/api/v1/identity/users",
                "page=1&pageSize=1",
                null,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                "nonceabcdefghijklm",
                new string('a', 64),
                "1"));
        using var debugResponse = await client.SendAsync(debugRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, debugResponse.StatusCode);
        var debug = await debugResponse.Content
            .ReadFromJsonAsync<OpenAccessClientSignatureDebugResponse>(cancellationToken);
        Assert.IsNotNull(debug);
        Assert.IsFalse(string.IsNullOrWhiteSpace(debug.CanonicalString));
        Assert.IsFalse(debug.Diagnostics.Any(item => item.Contains(created.Secret, StringComparison.Ordinal)));

        using var otherClientLogsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/identity/open-access-clients/{Guid.NewGuid():D}/access-logs?page=1&pageSize=20");
        otherClientLogsRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var otherClientLogsResponse = await client.SendAsync(
            otherClientLogsRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, otherClientLogsResponse.StatusCode);
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

    private static Task<string> LoginAsync(
        HttpClient client,
        string username,
        string password,
        CancellationToken cancellationToken) =>
        IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            username,
            password,
            cancellationToken);

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
