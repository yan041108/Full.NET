using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Document;

internal static class DocumentShareConcurrencyAssertions
{
    private const string ItemsPath = "/api/v1/document/host/items";
    private const string SharesPath = "/api/v1/document/host/shares";
    private const int ConcurrentAttempts = 20;

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var identity = await factory.CreateHostIdentityAsync(
            $"document-share-concurrency-{Guid.NewGuid():N}",
            [
                HostDocumentPermissions.Read,
                HostDocumentPermissions.Create,
                HostDocumentSharePermissions.Read,
                HostDocumentSharePermissions.Create,
                HostDocumentSharePermissions.UpdateStatus,
            ],
            cancellationToken);

        var document = await CreateDocumentAsync(client, identity.AccessToken, cancellationToken);
        const string password = "Share@2026!Secure";

        await VerifyTwentyWayConcurrentLimitAsync(
            factory,
            client,
            identity.AccessToken,
            document.Id,
            password,
            cancellationToken);
        await VerifyWrongPasswordDoesNotConsumeAsync(
            factory,
            client,
            identity.AccessToken,
            document.Id,
            password,
            cancellationToken);
        await VerifyDisabledShareDoesNotConsumeAsync(
            factory,
            client,
            identity.AccessToken,
            document.Id,
            password,
            cancellationToken);
        await VerifyExpiredShareDoesNotConsumeAsync(
            factory,
            client,
            identity.AccessToken,
            document.Id,
            password,
            cancellationToken);
    }

    private static async Task VerifyTwentyWayConcurrentLimitAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string token,
        Guid documentId,
        string password,
        CancellationToken cancellationToken)
    {
        var share = await CreateShareAsync(
            client,
            token,
            documentId,
            password,
            maxAccessCount: 1,
            cancellationToken);
        var path = $"/api/v1/document/public/shares/{share.ShareCode}/access";
        var attempts = Enumerable.Range(0, ConcurrentAttempts)
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
                $"访问上限为 1 时，{ConcurrentAttempts} 路并发必须仅成功一次。");
            Assert.IsTrue(
                responses.Where(response => response.StatusCode != HttpStatusCode.OK)
                    .All(response => response.StatusCode == HttpStatusCode.UnprocessableEntity),
                "未取得访问配额的并发请求必须稳定返回业务规则失败。");
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

    private static async Task VerifyWrongPasswordDoesNotConsumeAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string token,
        Guid documentId,
        string password,
        CancellationToken cancellationToken)
    {
        var share = await CreateShareAsync(
            client,
            token,
            documentId,
            password,
            maxAccessCount: 5,
            cancellationToken);
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/document/public/shares/{share.ShareCode}/access",
            new AccessHostDocumentShareRequest("WrongPassword!2026"),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(0, await QueryAccessCountAsync(factory, share.Id, cancellationToken));
    }

    private static async Task VerifyDisabledShareDoesNotConsumeAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string token,
        Guid documentId,
        string password,
        CancellationToken cancellationToken)
    {
        var share = await CreateShareAsync(
            client,
            token,
            documentId,
            password,
            maxAccessCount: 5,
            cancellationToken);
        using (var disableResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       $"{SharesPath}/{share.Id:D}/status",
                       token,
                       new UpdateHostDocumentShareStatusRequest(false, share.Version)),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        }

        using var accessResponse = await client.PostAsJsonAsync(
            $"/api/v1/document/public/shares/{share.ShareCode}/access",
            new AccessHostDocumentShareRequest(password),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, accessResponse.StatusCode);
        Assert.AreEqual(0, await QueryAccessCountAsync(factory, share.Id, cancellationToken));
    }

    private static async Task VerifyExpiredShareDoesNotConsumeAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string token,
        Guid documentId,
        string password,
        CancellationToken cancellationToken)
    {
        var share = await CreateShareAsync(
            client,
            token,
            documentId,
            password,
            maxAccessCount: 5,
            cancellationToken);
        await ExpireShareAsync(factory, share.Id, factory.Provider, cancellationToken);

        using var accessResponse = await client.PostAsJsonAsync(
            $"/api/v1/document/public/shares/{share.ShareCode}/access",
            new AccessHostDocumentShareRequest(password),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, accessResponse.StatusCode);
        Assert.AreEqual(0, await QueryAccessCountAsync(factory, share.Id, cancellationToken));
    }

    private static async Task ExpireShareAsync(
        FullNetApiFactory factory,
        Guid shareId,
        DatabaseProvider provider,
        CancellationToken cancellationToken)
    {
        var expireSql = provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                UPDATE fn_document_share
                SET ExpireTime = DATEADD(second, -60, SYSUTCDATETIME())
                WHERE Id = @Id AND TenantId IS NULL
                """,
            DatabaseProvider.MySql =>
                """
                UPDATE fn_document_share
                SET ExpireTime = DATE_SUB(UTC_TIMESTAMP(), INTERVAL 60 SECOND)
                WHERE Id = @Id AND TenantId IS NULL
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        await scope.ServiceProvider.GetRequiredService<ICommandExecutor>()
            .ExecuteAsync(
                new SqlStatement(
                    "test.document_share.force_expire",
                    expireSql,
                    SqlDataScope.HostOnly),
                new { Id = shareId },
                cancellationToken);
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
                    $"Share concurrency {Guid.NewGuid():N}",
                    "并发限额集成测试")),
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
        int maxAccessCount,
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
                    MaxAccessCount: maxAccessCount)),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var share = await response.Content.ReadFromJsonAsync<HostDocumentShareResponse>(cancellationToken);
        Assert.IsNotNull(share);
        return share;
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
