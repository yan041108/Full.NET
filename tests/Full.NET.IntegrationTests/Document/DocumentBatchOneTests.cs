using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Document;

/// <summary>第一批同场景双库验证，聚焦公开状态、配额、并发版本和可靠释放边界。</summary>
[TestClass]
public sealed class DocumentBatchOneTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Metadata_acl_sessions_versions_and_release_follow_batch_one_contract(DatabaseProvider database)
    {
        using var factory = new FullNetApiFactory(database, database == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        string[] permissions = [HostDocumentPermissions.Read, HostDocumentPermissions.Create, HostDocumentPermissions.Update,
            HostDocumentPermissions.AddVersion, HostDocumentPermissions.Download, HostDocumentPermissions.Delete,
            HostDocumentPermissions.RollbackVersion, HostDocumentPermissions.DeleteVersion, HostDocumentPermissions.Publish,
            HostDocumentPermissions.Archive, HostDocumentPermissions.Unarchive, HostDocumentPermissionManagementPermissions.Read,
            HostDocumentPermissionManagementPermissions.Set, HostDocumentSharePermissions.Create, HostDocumentSharePermissions.Read,
            HostDocumentSharePermissions.UpdateStatus, HostDocumentRecycleBinPermissions.Purge];
        var alice = await factory.CreateHostIdentityAsync($"doc-b1-a-{Guid.NewGuid():N}", permissions);
        var bob = await factory.CreateHostIdentityAsync($"doc-b1-b-{Guid.NewGuid():N}", permissions);
        var item = await JsonAsync<HostDocumentItemResponse>(client, alice.AccessToken, HttpMethod.Post,
            "/api/v1/document/host/items", new CreateHostDocumentItemRequest("First batch", "Original"), HttpStatusCode.Created);
        Assert.AreEqual(HostDocumentStatus.Draft, item.Status);
        Assert.IsTrue(item.DocumentNo.StartsWith("DOC-", StringComparison.Ordinal));
        using (var denied = await SendAsync(client, bob.AccessToken, HttpMethod.Get, ItemPath(item.Id)))
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode, "Other Host readers cannot see an ungranted draft.");
        using (var partial = await SendAsync(client, alice.AccessToken, HttpMethod.Put, ItemPath(item.Id), new { title = "Partial", version = item.Version }))
            Assert.AreEqual(HttpStatusCode.BadRequest, partial.StatusCode);
        item = await JsonAsync<HostDocumentItemResponse>(client, alice.AccessToken, HttpMethod.Put, ItemPath(item.Id),
            new UpdateHostDocumentItemRequest("Renamed", null, null, "https://example.com/thumb.png", 7, [], item.Version));
        Assert.AreEqual(7, item.Sort);
        Assert.IsNull(item.Description);
        Assert.AreEqual("https://example.com/thumb.png", item.Thumbnail);

        var uploadKey = Guid.CreateVersion7();
        var firstContent = new string('x', 1537);
        item = await UploadAsync(client, alice.AccessToken, item.Id, firstContent, uploadKey);
        Assert.AreEqual(1, item.SizeKb);
        var firstVersion = item.CurrentVersion!;
        var replay = await UploadAsync(client, alice.AccessToken, item.Id, firstContent, uploadKey);
        Assert.AreEqual(firstVersion.Id, replay.CurrentVersion!.Id);
        Assert.AreEqual(item.Version, replay.Version);
        item = await JsonAsync<HostDocumentItemResponse>(client, alice.AccessToken, HttpMethod.Post, ItemPath(item.Id) + "/publish",
            new ChangeHostDocumentStatusRequest(item.Version));
        Assert.AreEqual(HostDocumentStatus.Published, item.Status);
        item = await JsonAsync<HostDocumentItemResponse>(client, alice.AccessToken, HttpMethod.Put, ItemPath(item.Id),
            new UpdateHostDocumentItemRequest("Title only", item.Description, item.CategoryId, item.Thumbnail, item.Sort, [], item.Version));
        Assert.AreEqual(HostDocumentStatus.Published, item.Status);
        Assert.AreEqual(7, item.Sort);
        Assert.AreEqual("https://example.com/thumb.png", item.Thumbnail);

        var share = await JsonAsync<HostDocumentShareResponse>(client, alice.AccessToken, HttpMethod.Post,
            "/api/v1/document/host/shares", new CreateHostDocumentShareRequest(item.Id, 1, "batch-one-secret", 1), HttpStatusCode.Created);
        var publicPath = $"/api/v1/document/public/shares/{share.ShareCode}";
        var open = new AccessHostDocumentShareRequest("batch-one-secret") { RequestKey = Guid.CreateVersion7() };
        // 相同请求并发重试都返回一次打开的结果，额度只消耗一次。
        var opens = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            JsonAsync<HostDocumentShareAccessResponse>(client, null, HttpMethod.Post, publicPath + "/access", open)));
        Assert.IsTrue(opens.All(value => value.AccessCountRemaining == 0));
        Assert.AreEqual("document.txt", opens[0].FileName);
        Assert.AreEqual("text/plain", opens[0].MimeType);
        using (var exhausted = await SendAsync(client, null, HttpMethod.Post, publicPath + "/access",
            open with { RequestKey = Guid.CreateVersion7() })) Assert.AreEqual(HttpStatusCode.Conflict, exhausted.StatusCode);
        using (var raw = await SendAsync(client, null, HttpMethod.Post, publicPath + "/content", new AccessHostDocumentShareRequest("batch-one-secret")))
            Assert.AreEqual(HttpStatusCode.NotFound, raw.StatusCode);
        var credential = new AccessHostDocumentShareRequest { PreviewGrant = opens[0].PreviewGrant };
        using (var content = await SendAsync(client, null, HttpMethod.Post, publicPath + "/content", credential))
        {
            Assert.AreEqual(HttpStatusCode.OK, content.StatusCode);
            Assert.AreEqual(firstContent, await content.Content.ReadAsStringAsync());
        }
        // 独立 Host 实例复用权威会话与共享 Key Ring，凭据不依赖创建实例的内存。
        using (var secondHost = factory.WithWebHostBuilder(_ => { }))
        using (var secondClient = secondHost.CreateClient())
        using (var content = await SendAsync(secondClient, null, HttpMethod.Post, publicPath + "/content", credential))
        {
            Assert.AreEqual(HttpStatusCode.OK, content.StatusCode, await content.Content.ReadAsStringAsync());
            Assert.AreEqual(firstContent, await content.Content.ReadAsStringAsync());
        }

        var beforeAcl = item.Version;
        await JsonAsync<HostDocumentPermissionResponse[]>(client, alice.AccessToken, HttpMethod.Post,
            "/api/v1/document/host/permissions", new SetHostDocumentPermissionsRequest(item.Id, [], "restricted", beforeAcl));
        using (var denied = await SendAsync(client, alice.AccessToken, HttpMethod.Get, ItemPath(item.Id))) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var revoked = await SendAsync(client, null, HttpMethod.Post, publicPath + "/content", credential)) Assert.AreEqual(HttpStatusCode.NotFound, revoked.StatusCode);
        var context = await JsonAsync<HostDocumentPermissionContext>(client, alice.AccessToken, HttpMethod.Get,
            $"/api/v1/document/host/permissions/context/{item.Id}");
        Assert.AreEqual(beforeAcl + 1, context.Version);
        await JsonAsync<HostDocumentPermissionResponse[]>(client, alice.AccessToken, HttpMethod.Post,
            "/api/v1/document/host/permissions", new SetHostDocumentPermissionsRequest(item.Id,
                [new HostDocumentPermissionEntry(alice.UserId, ["read", "write", "share", "delete"])], "restricted", context.Version));
        using (var stale = await SendAsync(client, alice.AccessToken, HttpMethod.Post, "/api/v1/document/host/permissions",
            new SetHostDocumentPermissionsRequest(item.Id, [], "host", context.Version))) Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        item = await JsonAsync<HostDocumentItemResponse>(client, alice.AccessToken, HttpMethod.Get, ItemPath(item.Id));

        item = await UploadAsync(client, alice.AccessToken, item.Id, "version-two", Guid.CreateVersion7());
        var secondVersion = item.CurrentVersion!;
        Assert.AreEqual(2, secondVersion.VersionNumber);
        item = await JsonAsync<HostDocumentItemResponse>(client, alice.AccessToken, HttpMethod.Post,
            ItemPath(item.Id) + $"/versions/{firstVersion.Id}/rollback", new RollbackHostDocumentVersionRequest(item.Version));
        item = await JsonAsync<HostDocumentItemResponse>(client, alice.AccessToken, HttpMethod.Post,
            ItemPath(item.Id) + $"/versions/{secondVersion.Id}/delete", new DeleteHostDocumentVersionRequest(item.Version));
        item = await UploadAsync(client, alice.AccessToken, item.Id, "version-three", Guid.CreateVersion7());
        Assert.AreEqual(3, item.CurrentVersion!.VersionNumber);
        var lastVersion = item.CurrentVersion;

        // 同一物理文件可被多个版本引用；不同请求并发追加必须取得不同编号。
        var appended = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            JsonAsync<HostDocumentItemResponse>(client, alice.AccessToken, HttpMethod.Post, ItemPath(item.Id) + "/versions",
                new AddHostDocumentVersionRequest(firstVersion.FileId, "Shared source") { RequestKey = Guid.CreateVersion7() })));
        CollectionAssert.AreEquivalent(new[] { 4, 5 }, appended.Select(value => value.CurrentVersion!.VersionNumber).ToArray());
        item = appended.MaxBy(value => value.Version)!;
        var allVersions = new[] { firstVersion, secondVersion, lastVersion }
            .Concat(appended.Select(value => value.CurrentVersion!)).ToArray();

        var raceTarget = appended.MinBy(value => value.CurrentVersion!.VersionNumber)!.CurrentVersion!;
        var rollback = SendAsync(client, alice.AccessToken, HttpMethod.Post,
            ItemPath(item.Id) + $"/versions/{raceTarget.Id}/rollback", new RollbackHostDocumentVersionRequest(item.Version));
        var retention = Task.Run(async () =>
        {
            await using var retentionScope = factory.Services.CreateAsyncScope();
            var contextWriter = retentionScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            contextWriter.SetHost();
            try
            {
                return await retentionScope.ServiceProvider.GetRequiredService<Full.NET.Modules.Document.Features.ManageHostDocumentItems.DocumentVersionDeletionService>()
                    .TryDeleteVersionForRetentionAsync(item.Id, raceTarget.Id);
            }
            finally { contextWriter.Clear(); }
        });
        using (var response = await rollback)
        {
            var trimmed = await retention;
            if (response.StatusCode == HttpStatusCode.OK) Assert.IsFalse(trimmed, "A current version must survive retention.");
            else
            {
                Assert.IsTrue(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
                Assert.IsTrue(trimmed);
            }
        }
        item = await JsonAsync<HostDocumentItemResponse>(client, alice.AccessToken, HttpMethod.Get, ItemPath(item.Id));
        Assert.IsNotNull(item.CurrentVersion, "Concurrent rollback and retention must never leave a dangling current pointer.");

        item = await JsonAsync<HostDocumentItemResponse>(client, alice.AccessToken, HttpMethod.Post,
            ItemPath(item.Id) + "/archive", new ChangeHostDocumentStatusRequest(item.Version));
        using (var archivedUpdate = await SendAsync(client, alice.AccessToken, HttpMethod.Put, ItemPath(item.Id),
            new UpdateHostDocumentItemRequest("Forbidden", null, null, null, 0, [], item.Version))) Assert.AreEqual(HttpStatusCode.Conflict, archivedUpdate.StatusCode);
        item = await JsonAsync<HostDocumentItemResponse>(client, alice.AccessToken, HttpMethod.Post,
            ItemPath(item.Id) + "/unarchive", new ChangeHostDocumentStatusRequest(item.Version));
        Assert.AreEqual(HostDocumentStatus.Draft, item.Status);
        using (var oldOpen = await SendAsync(client, null, HttpMethod.Post, publicPath + "/access", open)) Assert.AreEqual(HttpStatusCode.NotFound, oldOpen.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        tenant.SetHost();
        try
        {
            var claims = scope.ServiceProvider.GetRequiredService<IHostFileReferenceClaimService>();
            Assert.IsTrue(await claims.HasOpenClaimsAsync(lastVersion.FileId));
            var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var titledDownloads = await queries.QuerySingleOrDefaultAsync<long>(new SqlStatement("test.document_download_title", """
                SELECT COUNT(1) FROM fn_document_access_log
                WHERE DocumentItemId=@DocumentId AND AccessTypeKey='download' AND DocumentTitle='Title only'
                """, SqlDataScope.HostOnly), new Dictionary<string, object?> { ["DocumentId"] = item.Id });
            Assert.AreEqual(1L, titledDownloads, "同一分享会话跨实例重试只记一次下载，标题必须来自文档。");
            // 超过一批的关联日志要求跨事务继续清理；保留当前 ACL，避免中途丢失后续批次授权。
            var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var seedLog = new SqlStatement("test.document_purge_logs", """
                INSERT INTO fn_document_access_log (Id, DocumentItemId, DocumentTitle, AccessTypeKey, SourceKey, OccurredAtUtc)
                VALUES (@Id, @DocumentId, 'Purge boundary', 'share_access', 'share', @Now)
                """, SqlDataScope.HostOnly);
            await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async ct =>
            {
                for (var index = 0; index < 105; index++)
                    await commands.ExecuteAsync(seedLog, new Dictionary<string, object?> {
                        ["Id"] = Guid.CreateVersion7(), ["DocumentId"] = item.Id, ["Now"] = DateTimeOffset.UtcNow }, ct);
                return true;
            }, CancellationToken.None);
            await JsonAsync<bool>(client, alice.AccessToken, HttpMethod.Post, ItemPath(item.Id) + "/delete", new DeleteHostDocumentItemRequest(item.Version));
            await JsonAsync<bool>(client, alice.AccessToken, HttpMethod.Post, $"/api/v1/document/host/recycle-bin/{item.Id}/purge");
            // 确认 Outbox 已原子保存释放事实，再重放实际 Files 消费者验证幂等。
            var count = await queries.QuerySingleOrDefaultAsync<long>(new SqlStatement("test.document_release_count",
                "SELECT COUNT(1) FROM fn_outbox_message WHERE TenantId IS NULL AND MessageType = @EventType", SqlDataScope.HostOnly),
                new Dictionary<string, object?> { ["EventType"] = DocumentFileReferenceEventTypes.Released });
            Assert.IsTrue(count >= 5);
            var serializer = scope.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>();
            var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler>().Single(value => value.EventType == DocumentFileReferenceEventTypes.Released);
            foreach (var version in allVersions)
            {
                var payload = serializer.Serialize(new DocumentFileReferenceReleased(version.Id, version.FileId));
                await handler.HandleAsync(payload, CancellationToken.None);
                await handler.HandleAsync(payload, CancellationToken.None);
                if (version.Id == firstVersion.Id)
                    Assert.IsTrue(await claims.HasOpenClaimsAsync(firstVersion.FileId), "Other version claims must survive this exact release.");
            }
            foreach (var fileId in allVersions.Select(version => version.FileId).Distinct())
                Assert.IsFalse(await claims.HasOpenClaimsAsync(fileId));
        }
        finally { tenant.Clear(); }
    }

    private static string ItemPath(Guid id) => $"/api/v1/document/host/items/{id}";

    private static async Task<HostDocumentItemResponse> UploadAsync(HttpClient client, string token, Guid id, string text, Guid key)
    {
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        multipart.Add(file, "file", "document.txt");
        multipart.Add(new StringContent(key.ToString("D")), "requestKey");
        using var request = new HttpRequestMessage(HttpMethod.Post, ItemPath(id) + "/versions/upload") { Content = multipart };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>())!;
    }

    private static async Task<T> JsonAsync<T>(HttpClient client, string? token, HttpMethod method, string path,
        object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await SendAsync(client, token, method, path, body);
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string? token, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
