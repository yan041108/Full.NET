using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Settings;

/// <summary>
/// 租户数据字典纵向切片验收夹具。
/// </summary>
internal static class SettingsTenantDictTypeManagementAssertions
{
    public static async Task VerifyAsync(
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
        await VerifyUpdateWithOptimisticVersionAsync(client, cancellationToken);
        await VerifyDisableRejectsActiveItemsAsync(client, cancellationToken);
        await VerifyDictItemLifecycleAsync(client, cancellationToken);
        await OpenApiSettingsTenantDictTypesContractAssertions.VerifyAsync(client, cancellationToken);
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
            "/api/v1/settings/tenant-dict-types?page=1&pageSize=20");
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
        var code = $"tdt-{Guid.NewGuid():N}"[..12];
        var body = new CreateDictTypeRequest(code, "集成测试租户字典", "描述", 10);

        using var firstRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/tenant-dict-types",
            adminTenantToken,
            body);
        using var firstResponse = await client.SendAsync(firstRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        var created = await firstResponse.Content.ReadFromJsonAsync<DictTypeResponse>(
            cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(code, created.Code);

        using var duplicateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/tenant-dict-types",
            adminTenantToken,
            body);
        using var duplicateResponse = await client.SendAsync(
            duplicateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await duplicateResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            SettingsErrorCodes.DictTypeCodeExists,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyUpdateWithOptimisticVersionAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var code = $"tup-{Guid.NewGuid():N}"[..12];

        using var createRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/tenant-dict-types",
            adminTenantToken,
            new CreateDictTypeRequest(code, "更新前名称", null, 1));
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<DictTypeResponse>(
            cancellationToken);
        Assert.IsNotNull(created);

        using var updateRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/settings/tenant-dict-types/{created.Id:D}",
            adminTenantToken,
            new UpdateDictTypeRequest("更新后名称", "新描述", 2, created.Version));
        using var updateResponse = await client.SendAsync(updateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<DictTypeResponse>(
            cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual("更新后名称", updated.Name);
        Assert.AreEqual(2, updated.DisplayOrder);
        Assert.AreEqual(created.Version + 1, updated.Version);

        using var staleRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/settings/tenant-dict-types/{created.Id:D}",
            adminTenantToken,
            new UpdateDictTypeRequest("陈旧版本", null, 3, created.Version));
        using var staleResponse = await client.SendAsync(staleRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, staleResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await staleResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            SettingsErrorCodes.DictTypeVersionConflict,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyDisableRejectsActiveItemsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var code = $"tds-{Guid.NewGuid():N}"[..12];

        using var createTypeRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/tenant-dict-types",
            adminTenantToken,
            new CreateDictTypeRequest(code, "待禁用租户类型", null, 1));
        using var createTypeResponse = await client.SendAsync(createTypeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createTypeResponse.StatusCode);
        var dictType = await createTypeResponse.Content.ReadFromJsonAsync<DictTypeResponse>(
            cancellationToken);
        Assert.IsNotNull(dictType);

        var itemValue = $"tv-{Guid.NewGuid():N}"[..10];
        using var createItemRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/settings/tenant-dict-types/{dictType.Id:D}/items",
            adminTenantToken,
            new CreateDictItemRequest("启用项", itemValue, null, 1));
        using var createItemResponse = await client.SendAsync(createItemRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createItemResponse.StatusCode);

        using var disableTypeRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/settings/tenant-dict-types/{dictType.Id:D}/disable",
            adminTenantToken,
            new { });
        using var disableTypeResponse = await client.SendAsync(disableTypeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, disableTypeResponse.StatusCode);
        using var activeItemsProblem = JsonDocument.Parse(
            await disableTypeResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            SettingsErrorCodes.DictTypeHasActiveItems,
            activeItemsProblem.RootElement.GetProperty("code").GetString());

        var createdItem = await createItemResponse.Content.ReadFromJsonAsync<DictItemResponse>(
            cancellationToken);
        Assert.IsNotNull(createdItem);

        using var disableItemRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/settings/tenant-dict-items/{createdItem.Id:D}/disable",
            adminTenantToken,
            new { });
        using var disableItemResponse = await client.SendAsync(disableItemRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableItemResponse.StatusCode);

        using var disableTypeAgainRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/settings/tenant-dict-types/{dictType.Id:D}/disable",
            adminTenantToken,
            new { });
        using var disableTypeAgainResponse = await client.SendAsync(
            disableTypeAgainRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableTypeAgainResponse.StatusCode);
        var disabledType = await disableTypeAgainResponse.Content.ReadFromJsonAsync<DictTypeResponse>(
            cancellationToken);
        Assert.IsNotNull(disabledType);
        Assert.IsFalse(disabledType.IsActive);
    }

    private static async Task VerifyDictItemLifecycleAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminTenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);
        var code = $"tit-{Guid.NewGuid():N}"[..12];

        using var createTypeRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/settings/tenant-dict-types",
            adminTenantToken,
            new CreateDictTypeRequest(code, "租户字典项测试", null, 1));
        using var createTypeResponse = await client.SendAsync(createTypeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createTypeResponse.StatusCode);
        var dictType = await createTypeResponse.Content.ReadFromJsonAsync<DictTypeResponse>(
            cancellationToken);
        Assert.IsNotNull(dictType);

        var value = $"tiv-{Guid.NewGuid():N}"[..10];
        using var createItemRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/settings/tenant-dict-types/{dictType.Id:D}/items",
            adminTenantToken,
            new CreateDictItemRequest("标签一", value, "#ff0000", 1));
        using var createItemResponse = await client.SendAsync(createItemRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createItemResponse.StatusCode);
        var createdItem = await createItemResponse.Content.ReadFromJsonAsync<DictItemResponse>(
            cancellationToken);
        Assert.IsNotNull(createdItem);
        Assert.AreEqual(value, createdItem.Value);

        using var duplicateItemRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/settings/tenant-dict-types/{dictType.Id:D}/items",
            adminTenantToken,
            new CreateDictItemRequest("重复值", value, null, 2));
        using var duplicateItemResponse = await client.SendAsync(
            duplicateItemRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, duplicateItemResponse.StatusCode);
        using var valueProblem = JsonDocument.Parse(
            await duplicateItemResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            SettingsErrorCodes.DictItemValueExists,
            valueProblem.RootElement.GetProperty("code").GetString());

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/settings/tenant-dict-types/{dictType.Id:D}/items?page=1&pageSize=20");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminTenantToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedDictItemResponses>(
            cancellationToken);
        Assert.IsNotNull(page);
        Assert.AreEqual(1, page.Total);

        using var updateItemRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/settings/tenant-dict-items/{createdItem.Id:D}",
            adminTenantToken,
            new UpdateDictItemRequest("更新标签", "#00ff00", 5, createdItem.Version));
        using var updateItemResponse = await client.SendAsync(updateItemRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, updateItemResponse.StatusCode);
        var updatedItem = await updateItemResponse.Content.ReadFromJsonAsync<DictItemResponse>(
            cancellationToken);
        Assert.IsNotNull(updatedItem);
        Assert.AreEqual("更新标签", updatedItem.Label);
        Assert.AreEqual("#00ff00", updatedItem.Color);
        Assert.AreEqual(createdItem.Version + 1, updatedItem.Version);

        using var staleItemRequest = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/settings/tenant-dict-items/{createdItem.Id:D}",
            adminTenantToken,
            new UpdateDictItemRequest("陈旧项", null, 6, createdItem.Version));
        using var staleItemResponse = await client.SendAsync(staleItemRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, staleItemResponse.StatusCode);
        using var itemVersionProblem = JsonDocument.Parse(
            await staleItemResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            SettingsErrorCodes.DictItemVersionConflict,
            itemVersionProblem.RootElement.GetProperty("code").GetString());
    }

    private sealed record PagedDictItemResponses(
        DictItemResponse[] Items,
        int Page,
        int PageSize,
        long Total);

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
