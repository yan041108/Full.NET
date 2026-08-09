using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.IntegrationTests.Settings;

/// <summary>
/// Host 系统配置纵向切片验收夹具。
/// </summary>
internal static class SettingsConfigEntryManagementAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyCreateRejectsDuplicateKeyAndInvalidValueAsync(client, cancellationToken);
        await VerifyUpdateWithOptimisticVersionAsync(client, cancellationToken);
        await VerifyGetByKeyAndDisableAsync(client, cancellationToken);
        await VerifyExactConfigEntryActionPermissionBoundariesAsync(
            factory,
            client,
            cancellationToken);
        await OpenApiSettingsConfigEntriesContractAssertions.VerifyAsync(client, cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/config-entries?page=1&pageSize=20");
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

    private static async Task VerifyCreateRejectsDuplicateKeyAndInvalidValueAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var configKey = $"cfg.{Guid.NewGuid():N}"[..20];
        var body = new CreateConfigEntryRequest(
            configKey,
            "集成测试配置",
            "描述",
            null,
            ConfigValueKinds.String,
            "hello",
            10);

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/config-entries",
            adminToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        var created = await firstResponse.Content.ReadFromJsonAsync<ConfigEntryResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(configKey, created.ConfigKey);
        Assert.AreEqual("hello", created.Value);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/config-entries",
            adminToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            SettingsErrorCodes.ConfigEntryKeyExists,
            problem.RootElement.GetProperty("code").GetString());

        using var invalidValueRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/config-entries",
            adminToken,
            new CreateConfigEntryRequest(
                $"num.{Guid.NewGuid():N}"[..20],
                "整数配置",
                null,
                null,
                ConfigValueKinds.Integer,
                "not-an-integer",
                1));
        using var invalidValueResponse = await client.SendAsync(
            invalidValueRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidValueResponse.StatusCode);
    }

    private static async Task VerifyUpdateWithOptimisticVersionAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var configKey = $"upd.{Guid.NewGuid():N}"[..20];

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/config-entries",
            adminToken,
            new CreateConfigEntryRequest(
                configKey,
                "更新前名称",
                null,
                null,
                ConfigValueKinds.Boolean,
                "true",
                1));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ConfigEntryResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual("true", created.Value);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/settings/config-entries/{created.Id:D}",
            adminToken,
            new UpdateConfigEntryRequest("更新后名称", "新描述", null, "false", 2, created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ConfigEntryResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("更新后名称", updated.DisplayName);
        Assert.AreEqual("false", updated.Value);
        Assert.AreEqual(2, updated.DisplayOrder);
        Assert.AreEqual(created.Version + 1, updated.Version);

        using var staleRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/settings/config-entries/{created.Id:D}",
            adminToken,
            new UpdateConfigEntryRequest("陈旧版本", null, null, "true", 3, created.Version));
        using var staleResponse = await client.SendAsync(staleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, staleResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await staleResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            SettingsErrorCodes.ConfigEntryVersionConflict,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyGetByKeyAndDisableAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var configKey = $"dis.{Guid.NewGuid():N}"[..20];

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/config-entries",
            adminToken,
            new CreateConfigEntryRequest(
                configKey,
                "待禁用配置",
                null,
                null,
                ConfigValueKinds.Json,
                """{"mode":"test"}""",
                1));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ConfigEntryResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var byKeyRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/settings/config-entries/by-key/{configKey}");
        byKeyRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var byKeyResponse = await client.SendAsync(byKeyRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, byKeyResponse.StatusCode);
        var byKey = await byKeyResponse.Content.ReadFromJsonAsync<ConfigEntryResponse>(
            cancellationToken);
        Assert.IsNotNull(byKey);
        Assert.AreEqual(created.Id, byKey.Id);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/config-entries?page=1&pageSize=100");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedConfigEntryResponses>(
            cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsTrue(page.Items.Any(item => item.Id == created.Id));

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/settings/config-entries/{created.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<ConfigEntryResponse>(
            cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsFalse(disabled.IsActive);
    }

    private static async Task VerifyExactConfigEntryActionPermissionBoundariesAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var configKey = $"bound.{Guid.NewGuid():N}"[..20];

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/config-entries",
            adminToken,
            new CreateConfigEntryRequest(
                configKey,
                "边界测试配置",
                null,
                null,
                ConfigValueKinds.String,
                "hello",
                1));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ConfigEntryResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        var disableKey = $"dis.{Guid.NewGuid():N}"[..20];
        using var disableSeedRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/config-entries",
            adminToken,
            new CreateConfigEntryRequest(
                disableKey,
                "禁用边界配置",
                null,
                null,
                ConfigValueKinds.String,
                "seed",
                1));
        using var disableSeedResponse = await client.SendAsync(disableSeedRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, disableSeedResponse.StatusCode);
        var disableTarget = await disableSeedResponse.Content.ReadFromJsonAsync<ConfigEntryResponse>(
            cancellationToken);
        Assert.IsNotNull(disableTarget);

        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [ConfigEntryManagementPermissions.Read],
            cancellationToken);
        await AssertConfigEntryPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Post,
            "/api/v1/settings/config-entries",
            cancellationToken,
            new CreateConfigEntryRequest(
                $"deny.{Guid.NewGuid():N}"[..20],
                "拒绝",
                null,
                null,
                ConfigValueKinds.String,
                "x",
                1));
        await AssertConfigEntryPermissionDeniedAsync(
            client,
            readOnlyToken,
            HttpMethod.Put,
            $"/api/v1/settings/config-entries/{created.Id:D}",
            cancellationToken,
            new UpdateConfigEntryRequest("拒绝", null, null, "x", 1, created.Version));
        await AssertConfigEntryPermissionDeniedAsync<object?>(
            client,
            readOnlyToken,
            HttpMethod.Post,
            $"/api/v1/settings/config-entries/{created.Id:D}/disable",
            cancellationToken,
            null);

        var createToken = await factory.CreateHostAccessTokenAsync(
            [
                ConfigEntryManagementPermissions.Read,
                ConfigEntryManagementPermissions.Create,
            ],
            cancellationToken);
        await AssertConfigEntryPermissionDeniedAsync(
            client,
            createToken,
            HttpMethod.Put,
            $"/api/v1/settings/config-entries/{created.Id:D}",
            cancellationToken,
            new UpdateConfigEntryRequest("拒绝", null, null, "x", 1, created.Version));
        await AssertConfigEntryPermissionDeniedAsync<object?>(
            client,
            createToken,
            HttpMethod.Post,
            $"/api/v1/settings/config-entries/{created.Id:D}/disable",
            cancellationToken,
            null);

        var disableToken = await factory.CreateHostAccessTokenAsync(
            [
                ConfigEntryManagementPermissions.Read,
                ConfigEntryManagementPermissions.Disable,
            ],
            cancellationToken);
        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/settings/config-entries/{disableTarget.Id:D}/disable",
            disableToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
    }

    private static async Task AssertConfigEntryPermissionDeniedAsync<TRequest>(
        HttpClient client,
        string accessToken,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        TRequest? body)
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

    private sealed record PagedConfigEntryResponses(
        ConfigEntryResponse[] Items,
        int Page,
        int PageSize,
        long Total);

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
