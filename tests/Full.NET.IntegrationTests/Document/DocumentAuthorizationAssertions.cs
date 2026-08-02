using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Document;

internal static class DocumentAuthorizationAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        var reader = await factory.CreateHostIdentityAsync(
            $"document-reader-{Guid.NewGuid():N}",
            [
                "identity.navigation.read",
                HostDocumentPermissions.Read,
                HostDocumentCategoryPermissions.Read,
            ],
            cancellationToken);

        await VerifyReadOnlyAccessAsync(client, reader.AccessToken, cancellationToken);
        await VerifyNavigationProjectionAsync(client, reader.AccessToken, cancellationToken);

        var manager = await factory.CreateHostIdentityAsync(
            $"document-manager-{Guid.NewGuid():N}",
            [
                "identity.navigation.read",
                HostDocumentPermissions.Read,
                HostDocumentPermissions.Create,
                HostDocumentPermissions.Update,
                HostDocumentPermissions.AddVersion,
                HostDocumentPermissions.Delete,
                HostDocumentPermissions.Restore,
                HostDocumentCategoryPermissions.Read,
                HostDocumentCategoryPermissions.Create,
                HostDocumentCategoryPermissions.Update,
                HostDocumentCategoryPermissions.Delete,
                HostDocumentTagPermissions.Manage,
            ],
            cancellationToken);
        await VerifyManagerNavigationAsync(client, manager.AccessToken, cancellationToken);
    }

    private static async Task VerifyReadOnlyAccessAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using (var listResponse = await client.SendAsync(
                   Authorized(HttpMethod.Get, "/api/v1/document/host/items", token),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        }

        using (var categoryListResponse = await client.SendAsync(
                   Authorized(HttpMethod.Get, "/api/v1/document/host/categories", token),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, categoryListResponse.StatusCode);
        }

        using (var createResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       "/api/v1/document/host/items",
                       token,
                       new CreateHostDocumentItemRequest("blocked", null)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, createResponse.StatusCode);
            using var problem = JsonDocument.Parse(
                await createResponse.Content.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(
                "authorization.permission_denied",
                problem.RootElement.GetProperty("code").GetString());
        }

        using (var categoryCreateResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       "/api/v1/document/host/categories",
                       token,
                       new CreateHostDocumentCategoryRequest("blocked", null, 0)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, categoryCreateResponse.StatusCode);
        }

        using (var tagCreateResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       "/api/v1/document/host/tags",
                       token,
                       new CreateHostDocumentTagRequest("blocked")),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, tagCreateResponse.StatusCode);
        }
    }

    private static async Task VerifyNavigationProjectionAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/navigation");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var navigation = await response.Content.ReadFromJsonAsync<NavigationNodeResponse[]>(
            cancellationToken);
        Assert.IsNotNull(navigation);
        Assert.IsTrue(navigation.Any(item => item.Id == "host-document-items"));
        Assert.IsTrue(navigation.Any(item => item.Id == "document-categories"));
        Assert.IsFalse(navigation.Any(item => item.Id == "document-tags"));
    }

    private static async Task VerifyManagerNavigationAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/navigation");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var navigation = await response.Content.ReadFromJsonAsync<NavigationNodeResponse[]>(
            cancellationToken);
        Assert.IsNotNull(navigation);
        foreach (var id in new[] { "host-document-items", "document-categories", "document-tags" })
        {
            Assert.IsTrue(
                navigation.Any(item => item.Id == id),
                $"缺少导航节点：{id}");
        }
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
