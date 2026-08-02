using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;

namespace Full.NET.IntegrationTests.Document;

internal static class DocumentHostCategoryTagAssertions
{
    private const string CategoriesPath = "/api/v1/document/host/categories";
    private const string TagsPath = "/api/v1/document/host/tags";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        var manager = await factory.CreateHostIdentityAsync(
            $"document-taxonomy-{Guid.NewGuid():N}",
            [
                HostDocumentPermissions.Read,
                HostDocumentCategoryPermissions.Manage,
                HostDocumentTagPermissions.Manage,
            ],
            cancellationToken);

        var category = await CreateCategoryAsync(client, manager.AccessToken, cancellationToken);
        await VerifyDuplicateCategoryNameAsync(client, manager.AccessToken, category, cancellationToken);
        var updatedCategory = await UpdateCategoryAsync(client, manager.AccessToken, category, cancellationToken);
        await DeleteCategoryAsync(client, manager.AccessToken, updatedCategory, cancellationToken);

        var tag = await CreateTagAsync(client, manager.AccessToken, cancellationToken);
        await VerifyDuplicateTagNameAsync(client, manager.AccessToken, tag, cancellationToken);
        var updatedTag = await UpdateTagAsync(client, manager.AccessToken, tag, cancellationToken);
        await DeleteTagAsync(client, manager.AccessToken, updatedTag, cancellationToken);

        await OpenApiDocumentHostCategoriesTagsContractAssertions.VerifyAsync(client, cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CategoriesPath);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await factory.CreateHostAccessTokenAsync(
                ["platform.dashboard.read"],
                cancellationToken));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<HostDocumentCategoryResponse> CreateCategoryAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                CategoriesPath,
                token,
                new CreateHostDocumentCategoryRequest($" Cat {Guid.NewGuid():N}"[..16], null, 10)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<HostDocumentCategoryResponse>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(1, created.Version);

        using var listResponse = await client.SendAsync(
            Authorized(HttpMethod.Get, CategoriesPath, token),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<HostDocumentCategoryResponse[]>(cancellationToken);
        Assert.IsNotNull(list);
        Assert.IsTrue(list.Any(entry => entry.Id == created.Id));

        using var getResponse = await client.SendAsync(
            Authorized(HttpMethod.Get, $"{CategoriesPath}/{created.Id:D}", token),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);
        return created;
    }

    private static async Task VerifyDuplicateCategoryNameAsync(
        HttpClient client,
        string token,
        HostDocumentCategoryResponse category,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                CategoriesPath,
                token,
                new CreateHostDocumentCategoryRequest(category.Name, null, 0)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            DocumentErrorCodes.CategoryNameExists,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<HostDocumentCategoryResponse> UpdateCategoryAsync(
        HttpClient client,
        string token,
        HostDocumentCategoryResponse category,
        CancellationToken cancellationToken)
    {
        var newName = $"Updated {Guid.NewGuid():N}"[..20];
        using var response = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Put,
                $"{CategoriesPath}/{category.Id:D}",
                token,
                new UpdateHostDocumentCategoryRequest(newName, null, 20, category.Version)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<HostDocumentCategoryResponse>(cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual(newName, updated.Name);
        Assert.AreEqual(2, updated.Version);
        return updated;
    }

    private static async Task DeleteCategoryAsync(
        HttpClient client,
        string token,
        HostDocumentCategoryResponse category,
        CancellationToken cancellationToken)
    {
        using var deleteResponse = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                $"{CategoriesPath}/{category.Id:D}/delete",
                token,
                new DeleteHostDocumentCategoryRequest(category.Version)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var missingResponse = await client.SendAsync(
            Authorized(HttpMethod.Get, $"{CategoriesPath}/{category.Id:D}", token),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    private static async Task<HostDocumentTagResponse> CreateTagAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                TagsPath,
                token,
                new CreateHostDocumentTagRequest($"tag-{Guid.NewGuid():N}"[..16])),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<HostDocumentTagResponse>(cancellationToken);
        Assert.IsNotNull(created);
        return created;
    }

    private static async Task VerifyDuplicateTagNameAsync(
        HttpClient client,
        string token,
        HostDocumentTagResponse tag,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, TagsPath, token, new CreateHostDocumentTagRequest(tag.Name)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            DocumentErrorCodes.TagNameExists,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<HostDocumentTagResponse> UpdateTagAsync(
        HttpClient client,
        string token,
        HostDocumentTagResponse tag,
        CancellationToken cancellationToken)
    {
        var newName = $"tag-upd-{Guid.NewGuid():N}"[..20];
        using var response = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Put,
                $"{TagsPath}/{tag.Id:D}",
                token,
                new UpdateHostDocumentTagRequest(newName, tag.Version)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<HostDocumentTagResponse>(cancellationToken);
        Assert.IsNotNull(updated);
        Assert.AreEqual(newName, updated.Name);
        return updated;
    }

    private static async Task DeleteTagAsync(
        HttpClient client,
        string token,
        HostDocumentTagResponse tag,
        CancellationToken cancellationToken)
    {
        using var deleteResponse = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                $"{TagsPath}/{tag.Id:D}/delete",
                token,
                new DeleteHostDocumentTagRequest(tag.Version)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage AuthorizedJson<T>(HttpMethod method, string path, string token, T body)
    {
        var request = Authorized(method, path, token);
        request.Content = JsonContent.Create(body);
        return request;
    }
}
