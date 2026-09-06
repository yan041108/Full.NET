using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Regions.Contracts;
using Full.NET.Seeding.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Regions;

/// <summary>行政区域纵向切片验收夹具。</summary>
internal static class RegionsAdministrativeRegionAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        await OpenApiRegionsAdministrativeRegionsContractAssertions.VerifyAsync(client, cancellationToken);

        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var baselineCount = await CountRegionsAsync(client, adminToken, cancellationToken);
        Assert.IsTrue(baselineCount >= 14, "Baseline seed should provide at least 14 regions.");

        var roots = await ListChildrenAsync(client, adminToken, null, cancellationToken);
        Assert.IsTrue(roots.Any(item => item.Code == "110000"));
        Assert.IsTrue(roots.Any(item => item.Code == "440000"));

        var tree = await GetTreeAsync(client, adminToken, null, 3, cancellationToken);
        Assert.IsTrue(tree.Any(node => node.Code == "110000" && node.Children.Count > 0));

        var created = await CreateRegionAsync(
            client,
            adminToken,
            new CreateAdministrativeRegionRequest(
                null,
                "999999",
                "测试省",
                "测试",
                "中国,测试省",
                null,
                null,
                1,
                "省",
                "ceshi",
                null,
                null,
                99,
                "integration"),
            cancellationToken);
        Assert.AreEqual("999999", created.Code);
        Assert.AreEqual(1, created.Version);

        var fetched = await GetRegionAsync(client, adminToken, created.Id, cancellationToken);
        Assert.AreEqual("测试省", fetched.Name);

        var updated = await UpdateRegionAsync(
            client,
            adminToken,
            created.Id,
            new UpdateAdministrativeRegionRequest(
                null,
                "测试省更新",
                "测试",
                "中国,测试省",
                null,
                null,
                1,
                "省",
                "ceshi",
                null,
                null,
                99,
                "integration-updated",
                created.Version),
            cancellationToken);
        Assert.AreEqual("测试省更新", updated.Name);
        Assert.AreEqual(2, updated.Version);

        var importPreview = await PreviewImportAsync(
            client,
            adminToken,
            new ImportAdministrativeRegionsRequest(
                "china.administrative",
                "1.0.1",
                "digest-preview",
                AdministrativeRegionImportMergeModes.Merge,
                [
                    new ImportAdministrativeRegionItem(
                        "999999",
                        null,
                        "测试省更新",
                        "测试",
                        "中国,测试省",
                        null,
                        null,
                        1,
                        "省",
                        "ceshi",
                        null,
                        null,
                        99),
                    new ImportAdministrativeRegionItem(
                        "999998",
                        "999999",
                        "测试市",
                        "测试市",
                        "中国,测试省,测试市",
                        null,
                        null,
                        2,
                        null,
                        null,
                        null,
                        null,
                        10),
                ]),
            cancellationToken);
        Assert.HasCount(1, importPreview.Added);
        Assert.AreEqual("999998", importPreview.Added[0].Code);

        var importApplied = await ApplyImportAsync(
            client,
            adminToken,
            new ImportAdministrativeRegionsRequest(
                "china.administrative",
                "1.0.1",
                "digest-apply",
                AdministrativeRegionImportMergeModes.Merge,
                [
                    new ImportAdministrativeRegionItem(
                        "999999",
                        null,
                        "测试省更新",
                        "测试",
                        "中国,测试省",
                        null,
                        null,
                        1,
                        "省",
                        "ceshi",
                        null,
                        null,
                        99),
                    new ImportAdministrativeRegionItem(
                        "999998",
                        "999999",
                        "测试市",
                        "测试市",
                        "中国,测试省,测试市",
                        null,
                        null,
                        2,
                        null,
                        null,
                        null,
                        null,
                        10),
                ]),
            cancellationToken);
        Assert.AreEqual(1, importApplied.AddedCount);
        Assert.AreEqual("1.0.1", importApplied.Manifest.DatasetVersion);

        var manifest = await GetLatestManifestAsync(client, adminToken, cancellationToken);
        Assert.AreEqual("digest-apply", manifest.SourceDigest);

        await VerifySeedIdempotencyAsync(factory, cancellationToken);

        using var deleteRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/regions/administrative-regions/{created.Id:D}/delete",
            adminToken,
            new DeleteAdministrativeRegionRequest(updated.Version));
        using var deleteResponse = await client.SendAsync(deleteRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    private static async Task VerifySeedIdempotencyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISeedOrchestrator>();
        var first = await orchestrator.RunAsync(SeedProfile.Baseline, cancellationToken);
        var second = await orchestrator.RunAsync(SeedProfile.Baseline, cancellationToken);
        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(second.IsSuccess);
        Assert.IsTrue(second.Value!.SkippedCount >= first.Value!.SkippedCount);
    }

    private static async Task<long> CountRegionsAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/regions/administrative-regions?page=1&pageSize=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<AdministrativeRegionResponse>>(
            cancellationToken);
        return page!.Total;
    }

    private static async Task<IReadOnlyList<AdministrativeRegionChildResponse>> ListChildrenAsync(
        HttpClient client,
        string accessToken,
        Guid? parentId,
        CancellationToken cancellationToken)
    {
        var path = parentId is null
            ? "/api/v1/regions/administrative-regions/children"
            : $"/api/v1/regions/administrative-regions/children?parentId={parentId:D}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<AdministrativeRegionChildResponse>>(
            cancellationToken))!;
    }

    private static async Task<IReadOnlyList<AdministrativeRegionTreeNodeResponse>> GetTreeAsync(
        HttpClient client,
        string accessToken,
        Guid? parentId,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var path = parentId is null
            ? $"/api/v1/regions/administrative-regions/tree?maxDepth={maxDepth}"
            : $"/api/v1/regions/administrative-regions/tree?parentId={parentId:D}&maxDepth={maxDepth}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<AdministrativeRegionTreeNodeResponse>>(
            cancellationToken))!;
    }

    private static async Task<AdministrativeRegionResponse> CreateRegionAsync(
        HttpClient client,
        string accessToken,
        CreateAdministrativeRegionRequest body,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/regions/administrative-regions",
            accessToken,
            body);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdministrativeRegionResponse>(cancellationToken))!;
    }

    private static async Task<AdministrativeRegionResponse> GetRegionAsync(
        HttpClient client,
        string accessToken,
        Guid regionId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/regions/administrative-regions/{regionId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdministrativeRegionResponse>(cancellationToken))!;
    }

    private static async Task<AdministrativeRegionResponse> UpdateRegionAsync(
        HttpClient client,
        string accessToken,
        Guid regionId,
        UpdateAdministrativeRegionRequest body,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Put,
            $"/api/v1/regions/administrative-regions/{regionId:D}",
            accessToken,
            body);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdministrativeRegionResponse>(cancellationToken))!;
    }

    private static async Task<ImportAdministrativeRegionsPreviewResponse> PreviewImportAsync(
        HttpClient client,
        string accessToken,
        ImportAdministrativeRegionsRequest body,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/regions/administrative-regions/import/preview",
            accessToken,
            body);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportAdministrativeRegionsPreviewResponse>(
            cancellationToken))!;
    }

    private static async Task<ImportAdministrativeRegionsApplyResponse> ApplyImportAsync(
        HttpClient client,
        string accessToken,
        ImportAdministrativeRegionsRequest body,
        CancellationToken cancellationToken)
    {
        using var request = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/regions/administrative-regions/import/apply",
            accessToken,
            body);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportAdministrativeRegionsApplyResponse>(
            cancellationToken))!;
    }

    private static async Task<AdministrativeRegionDatasetManifestResponse> GetLatestManifestAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/regions/administrative-regions/dataset-manifest/latest?datasetKey=china.administrative");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdministrativeRegionDatasetManifestResponse>(
            cancellationToken))!;
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
