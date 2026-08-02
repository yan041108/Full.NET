using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Document;

internal static class DocumentHostItemAssertions
{
    private const string ItemsPath = "/api/v1/document/host/items";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        using (var anonymous = await client.GetAsync(ItemsPath, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        var wrongToken = await factory.CreateHostAccessTokenAsync(
            ["platform.dashboard.read"],
            cancellationToken);
        using (var forbidden = await client.SendAsync(
                   Authorized(HttpMethod.Get, ItemsPath, wrongToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var writer = await factory.CreateHostIdentityAsync(
            $"document-writer-{Guid.NewGuid():N}",
            [
                HostDocumentPermissions.Read,
                HostDocumentPermissions.Write,
                HostDocumentPermissions.Delete,
                HostFilePermissions.Read,
                HostFilePermissions.Write,
            ],
            cancellationToken);

        var created = await CreateItemAsync(client, writer.AccessToken, cancellationToken);
        var fileId = await UploadHostFileAsync(client, writer.AccessToken, cancellationToken);
        var withVersion = await AddVersionAsync(
            client,
            writer.AccessToken,
            created.Id,
            fileId,
            cancellationToken);

        await VerifyListAndGetAsync(client, writer.AccessToken, withVersion, cancellationToken);
        await VerifyInvalidFileReferenceAsync(client, writer.AccessToken, created.Id, cancellationToken);
        await VerifyDeleteAndRestoreAsync(client, writer.AccessToken, withVersion, cancellationToken);
    }

    private static async Task<HostDocumentItemResponse> CreateItemAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                ItemsPath,
                token,
                new CreateHostDocumentItemRequest(" Host spec ", " integration ")),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual("Host spec", created.Title);
        Assert.AreEqual("integration", created.Description);
        Assert.AreEqual(1, created.Version);
        Assert.IsNull(created.CurrentVersion);
        return created;
    }

    private static async Task<Guid> UploadHostFileAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes($"document-{Guid.NewGuid():N}");
        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(payload);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        uploadContent.Add(fileContent, "file", "document.txt");
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/files/host-files")
        {
            Content = uploadContent,
        };
        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var uploadResponse = await client.SendAsync(uploadRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, uploadResponse.StatusCode);
        var created = await uploadResponse.Content.ReadFromJsonAsync<HostFileResponse>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            created.ContentHash);
        return created.Id;
    }

    private static async Task<HostDocumentItemResponse> AddVersionAsync(
        HttpClient client,
        string token,
        Guid itemId,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                $"{ItemsPath}/{itemId:D}/versions",
                token,
                new AddHostDocumentVersionRequest(fileId)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken);
        Assert.IsNotNull(updated);
        Assert.IsNotNull(updated.CurrentVersion);
        Assert.AreEqual(1, updated.CurrentVersion.VersionNumber);
        Assert.AreEqual(fileId, updated.CurrentVersion.FileId);
        Assert.AreEqual(2, updated.Version);
        return updated;
    }

    private static async Task VerifyListAndGetAsync(
        HttpClient client,
        string token,
        HostDocumentItemResponse item,
        CancellationToken cancellationToken)
    {
        using (var listResponse = await client.SendAsync(
                   Authorized(HttpMethod.Get, ItemsPath + "?page=1&pageSize=20", token),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
            var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<HostDocumentItemResponse>>(
                cancellationToken);
            Assert.IsNotNull(page);
            Assert.IsTrue(page.Items.Any(entry => entry.Id == item.Id));
        }

        using var getResponse = await client.SendAsync(
            Authorized(HttpMethod.Get, $"{ItemsPath}/{item.Id:D}", token),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);
        var loaded = await getResponse.Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(item.CurrentVersion!.FileId, loaded.CurrentVersion!.FileId);
    }

    private static async Task VerifyInvalidFileReferenceAsync(
        HttpClient client,
        string token,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                $"{ItemsPath}/{itemId:D}/versions",
                token,
                new AddHostDocumentVersionRequest(Guid.CreateVersion7())),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            DocumentErrorCodes.InvalidFileReference,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyDeleteAndRestoreAsync(
        HttpClient client,
        string token,
        HostDocumentItemResponse item,
        CancellationToken cancellationToken)
    {
        using (var deleteResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{ItemsPath}/{item.Id:D}/delete",
                       token,
                       new DeleteHostDocumentItemRequest(item.Version)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);
        }

        using (var missingResponse = await client.SendAsync(
                   Authorized(HttpMethod.Get, $"{ItemsPath}/{item.Id:D}", token),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        }

        using var restoreResponse = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                $"{ItemsPath}/{item.Id:D}/restore",
                token,
                new RestoreHostDocumentItemRequest(item.Version + 1)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, restoreResponse.StatusCode);
        var restored = await restoreResponse.Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken);
        Assert.IsNotNull(restored);
        Assert.IsNotNull(restored.CurrentVersion);
        Assert.AreEqual(item.Version + 2, restored.Version);
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
