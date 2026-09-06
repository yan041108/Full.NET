using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.IntegrationTests.Settings;

/// <summary>
/// Host 枚举/常量元数据目录验收夹具。
/// </summary>
internal static class SettingsEnumCatalogAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyListAndDetailAsync(client, cancellationToken);
        await VerifyDictGenerationAsync(factory, client, cancellationToken);
        await OpenApiSettingsEnumCatalogsContractAssertions.VerifyAsync(
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
            "/api/v1/settings/enum-catalogs");
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

    private static async Task VerifyListAndDetailAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/enum-catalogs");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var summaries = await listResponse.Content
            .ReadFromJsonAsync<EnumCatalogSummary[]>(cancellationToken);
        Assert.IsNotNull(summaries);
        var configValueKind = summaries.SingleOrDefault(
            item => item.Key == "settings.config_value_kind");
        Assert.IsNotNull(configValueKind);
        Assert.AreEqual(ConfigValueKinds.All.Count, configValueKind.MemberCount);

        using var detailRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/enum-catalogs/settings.config_value_kind");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var detailResponse = await client.SendAsync(detailRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content
            .ReadFromJsonAsync<EnumCatalogDetail>(cancellationToken);
        Assert.IsNotNull(detail);
        CollectionAssert.AreEqual(
            ConfigValueKinds.All.ToArray(),
            detail.Members.Select(member => member.Code).ToArray());

        using var missingRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/enum-catalogs/settings.missing_catalog");
        missingRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var missingResponse = await client.SendAsync(missingRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await missingResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            SettingsErrorCodes.EnumCatalogNotFound,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyDictGenerationAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [EnumCatalogPermissions.Read],
            cancellationToken);

        using var forbiddenPreviewRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/enum-catalogs/settings.config_value_kind/dict-generation-preview");
        forbiddenPreviewRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            readOnlyToken);
        using var forbiddenPreviewResponse = await client.SendAsync(
            forbiddenPreviewRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenPreviewResponse.StatusCode);

        var generateToken = await factory.CreateHostAccessTokenAsync(
            [EnumCatalogPermissions.GenerateDict, EnumCatalogPermissions.Read],
            cancellationToken);

        using var previewRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/enum-catalogs/settings.config_value_kind/dict-generation-preview");
        previewRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            generateToken);
        using var previewResponse = await client.SendAsync(previewRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content
            .ReadFromJsonAsync<EnumCatalogDictGenerationPreview>(cancellationToken);
        Assert.IsNotNull(preview);
        Assert.IsTrue(preview.WillCreateDictType);
        Assert.IsTrue(preview.Items.Count > 0);
        Assert.IsTrue(preview.Items.All(
            item => item.Action == EnumCatalogDictGenerationItemActions.Create
                || item.Action == EnumCatalogDictGenerationItemActions.InvalidValue));

        using var generateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/settings/enum-catalogs/settings.config_value_kind/dict-generation");
        generateRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            generateToken);
        using var generateResponse = await client.SendAsync(generateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, generateResponse.StatusCode);
        var result = await generateResponse.Content
            .ReadFromJsonAsync<EnumCatalogDictGenerationResult>(cancellationToken);
        Assert.IsNotNull(result);
        Assert.IsTrue(result.DictTypeCreated);
        Assert.IsTrue(result.ItemsCreated > 0);
        Assert.IsNotNull(result.DictTypeId);

        using var secondGenerateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/settings/enum-catalogs/settings.config_value_kind/dict-generation");
        secondGenerateRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            generateToken);
        using var secondGenerateResponse = await client.SendAsync(
            secondGenerateRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, secondGenerateResponse.StatusCode);
        var secondResult = await secondGenerateResponse.Content
            .ReadFromJsonAsync<EnumCatalogDictGenerationResult>(cancellationToken);
        Assert.IsNotNull(secondResult);
        Assert.IsFalse(secondResult.DictTypeCreated);
        Assert.AreEqual(0, secondResult.ItemsCreated);
        Assert.IsTrue(secondResult.ItemsSkipped > 0);

        using var accountPreviewRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/settings/enum-catalogs/identity.account_type/dict-generation-preview");
        accountPreviewRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            generateToken);
        using var accountPreviewResponse = await client.SendAsync(
            accountPreviewRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, accountPreviewResponse.StatusCode);
        var accountPreview = await accountPreviewResponse.Content
            .ReadFromJsonAsync<EnumCatalogDictGenerationPreview>(cancellationToken);
        Assert.IsNotNull(accountPreview);
        Assert.IsTrue(accountPreview.DictTypeExists);
        Assert.IsFalse(accountPreview.WillCreateDictType);
        Assert.IsTrue(accountPreview.Items.All(
            item => item.Action == EnumCatalogDictGenerationItemActions.SkipExists));
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
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }
}
