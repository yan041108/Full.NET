using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Document;

internal static class DocumentShareSecurityAssertions
{
    private const string ItemsPath = "/api/v1/document/host/items";
    private const string SharesPath = "/api/v1/document/host/shares";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        await OpenApiDocumentHostSharesContractAssertions.VerifyAsync(client, cancellationToken);
        var identity = await factory.CreateHostIdentityAsync(
            $"document-share-{Guid.NewGuid():N}",
            [
                HostDocumentPermissions.Read,
                HostDocumentPermissions.Create,
                HostDocumentSharePermissions.Read,
                HostDocumentSharePermissions.Create,
            ],
            cancellationToken);

        var document = await CreateDocumentAsync(client, identity.AccessToken, cancellationToken);
        const string password = "Share@2026!Secure";
        var share = await CreateShareAsync(
            client,
            identity.AccessToken,
            document.Id,
            password,
            cancellationToken);

        await VerifyStoredHashAsync(factory, share.Id, password, cancellationToken);
        await VerifyManagementResponseAsync(client, identity.AccessToken, share.Id, cancellationToken);
        await VerifyLegacyGetIsClosedAsync(client, share.ShareCode, cancellationToken);
        await VerifyWrongPasswordDoesNotConsumeAsync(factory, client, share, cancellationToken);
        await VerifyConcurrentLimitIsAtomicAsync(factory, client, share, password, cancellationToken);
    }

    private static async Task<HostDocumentItemResponse> CreateDocumentAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                ItemsPath,
                token,
                new CreateHostDocumentItemRequest(
                    $"Share security {Guid.NewGuid():N}",
                    "匿名分享安全集成测试")),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken);
        Assert.IsNotNull(document);
        return document;
    }

    private static async Task<HostDocumentShareResponse> CreateShareAsync(
        HttpClient client,
        string token,
        Guid documentId,
        string password,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                SharesPath,
                token,
                new CreateHostDocumentShareRequest(
                    documentId,
                    ValidDays: 7,
                    Password: password,
                    MaxAccessCount: 1)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var share = await response.Content.ReadFromJsonAsync<HostDocumentShareResponse>(cancellationToken);
        Assert.IsNotNull(share);
        Assert.IsTrue(share.HasPassword);
        Assert.IsNull(share.Password);
        return share;
    }

    private static async Task VerifyStoredHashAsync(
        FullNetApiFactory factory,
        Guid shareId,
        string password,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var storedHash = await query.QuerySingleOrDefaultAsync<string>(
            new SqlStatement(
                "test.document_share.password_hash",
                "SELECT PasswordHash FROM fn_document_share WHERE Id = @Id AND TenantId IS NULL",
                SqlDataScope.HostOnly),
            new { Id = shareId },
            cancellationToken);

        Assert.IsFalse(string.IsNullOrWhiteSpace(storedHash));
        Assert.AreNotEqual(password, storedHash);
        Assert.IsFalse(storedHash.Contains(password, StringComparison.Ordinal));
    }

    private static async Task VerifyManagementResponseAsync(
        HttpClient client,
        string token,
        Guid shareId,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            Authorized(HttpMethod.Get, SharesPath + "?page=1&pageSize=20", token),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.IsTrue(json.Contains(shareId.ToString("D"), StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("\"password\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("passwordHash", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task VerifyLegacyGetIsClosedAsync(
        HttpClient client,
        string shareCode,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{SharesPath}/by-code/{shareCode}", cancellationToken);
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private static async Task VerifyWrongPasswordDoesNotConsumeAsync(
        FullNetApiFactory factory,
        HttpClient client,
        HostDocumentShareResponse share,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/document/public/shares/{share.ShareCode}/access",
            new AccessHostDocumentShareRequest("WrongPassword!2026"),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(0, await QueryAccessCountAsync(factory, share.Id, cancellationToken));
    }

    private static async Task VerifyConcurrentLimitIsAtomicAsync(
        FullNetApiFactory factory,
        HttpClient client,
        HostDocumentShareResponse share,
        string password,
        CancellationToken cancellationToken)
    {
        var path = $"/api/v1/document/public/shares/{share.ShareCode}/access";
        var attempts = Enumerable.Range(0, 8)
            .Select(_ => client.PostAsJsonAsync(
                path,
                new AccessHostDocumentShareRequest(password),
                cancellationToken))
            .ToArray();
        var responses = await Task.WhenAll(attempts);
        try
        {
            Assert.AreEqual(
                1,
                responses.Count(response => response.StatusCode == HttpStatusCode.OK),
                "访问上限为 1 时，并发请求必须仅成功一次。");
            Assert.IsTrue(
                responses.Where(response => response.StatusCode != HttpStatusCode.OK)
                    .All(response => response.StatusCode == HttpStatusCode.UnprocessableEntity),
                "未取得访问配额的请求必须稳定返回业务规则失败。");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        Assert.AreEqual(1, await QueryAccessCountAsync(factory, share.Id, cancellationToken));
    }

    private static async Task<int> QueryAccessCountAsync(
        FullNetApiFactory factory,
        Guid shareId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<int>(
                new SqlStatement(
                    "test.document_share.access_count",
                    "SELECT AccessCount FROM fn_document_share WHERE Id = @Id AND TenantId IS NULL",
                    SqlDataScope.HostOnly),
                new { Id = shareId },
                cancellationToken);
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
