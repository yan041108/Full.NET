using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Document;

/// <summary>第二批双库聚焦验收：服务端筛选、统计事实与分类并发不变量。</summary>
[TestClass]
public sealed class DocumentBatchTwoTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Filters_statistics_and_logical_events_use_current_readable_scope(DatabaseProvider provider)
    {
        using var factory = await FactoryAsync(provider);
        using var client = factory.CreateClientForHost("localhost");
        var identity = await factory.CreateHostIdentityAsync($"doc-b2-{Guid.NewGuid():N}", Permissions);
        var other = await factory.CreateHostIdentityAsync($"doc-b2-other-{Guid.NewGuid():N}", Permissions);
        var token = identity.AccessToken;
        var parent = await CategoryAsync(client, token, "Parent");
        var child = await CategoryAsync(client, token, "Child", parent.Id);
        var item = await JsonAsync<HostDocumentItemResponse>(client, token, HttpMethod.Post, Items,
            new CreateHostDocumentItemRequest("Actual content", null, HostDocumentType.Document, HostDocumentStatus.Draft, 0, null, child.Id, []), HttpStatusCode.Created);
        item = await DocumentLifecycleTestSupport.PublishAsync(client, token, item, CancellationToken.None);
        // 三份 1537 字节：当前大小只能计算一份，历史存储计算另外两份。
        item = await UploadAsync(client, token, item.Id, 1537);
        item = await UploadAsync(client, token, item.Id, 1537);
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>(); tenant.SetHost();
        try
        {
            var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var day = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
            var targetId = Guid.Empty;
            await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async ct =>
            {
                for (var index = 0; index < 110; index++)
                {
                    var id = Guid.CreateVersion7(); if (index == 100) targetId = id;
                    await commands.ExecuteAsync(new SqlStatement("test.document_batch_two.seed", """
                        INSERT INTO fn_document_item (Id, Title, DocumentNo, CategoryId, DocumentType, Status, CreatedAtUtc, CreatedByUserId)
                        VALUES (@Id, @Title, @No, @Category, 2, 1, @Now, @Actor)
                        """, SqlDataScope.HostOnly), Args(("Id", id), ("Title", index == 100 ? "Needle%_[" : $"Bulk {index:D3}"),
                            ("No", $"DOC-B2-{index}"), ("Category", child.Id), ("Now", day), ("Actor", item.CreatedByUserId)), ct);
                }
                return true;
            }, CancellationToken.None);
            var selected = await GetAsync<PagedResult<HostDocumentItemResponse>>(client, token,
                $"{Items}?keyword=Needle%25_%5B&categoryId={parent.Id}&status=1&documentType=2&page=1&pageSize=10");
            Assert.AreEqual(1L, selected.Total); Assert.AreEqual(targetId, selected.Items.Single().Id);
            var page1 = await GetAsync<PagedResult<HostDocumentItemResponse>>(client, token, $"{Items}?categoryId={parent.Id}&pageSize=100");
            var page2 = await GetAsync<PagedResult<HostDocumentItemResponse>>(client, token, $"{Items}?categoryId={parent.Id}&pageSize=100&page=2");
            Assert.AreEqual(111L, page1.Total); Assert.HasCount(100, page1.Items); Assert.HasCount(11, page2.Items);
            Assert.IsFalse(page1.Items.Select(x => x.Id).Intersect(page2.Items.Select(x => x.Id)).Any());
            var tagId = Guid.CreateVersion7();
            await commands.ExecuteAsync(new SqlStatement("test.document_batch_two.tag", """
                INSERT INTO fn_document_tag (Id, Name, CreatedAtUtc) VALUES (@Tag, 'Batch two tag', @Now);
                INSERT INTO fn_document_tag_assignment (DocumentItemId, TagId, CreatedAtUtc) VALUES (@Item, @Tag, @Now)
                """, SqlDataScope.HostOnly), Args(("Tag", tagId), ("Item", targetId), ("Now", day)));
            Assert.AreEqual(1L, (await GetAsync<PagedResult<HostDocumentItemResponse>>(client, token,
                $"{Items}?categoryId={parent.Id}&tagId={tagId}&status=1&documentType=2")).Total);

            var initial = await GetAsync<HostDocumentStatisticsResponse>(client, token, Statistics);
            Assert.AreEqual(111L, initial.Summary.TotalItems);
            Assert.AreEqual(1537L, initial.Summary.TotalSizeBytes);
            Assert.AreEqual(item.CurrentVersion!.SizeBytes + 16L, initial.Summary.HistorySizeBytes);
            Assert.AreEqual(1L, initial.Summary.TotalSizeKb);
            Assert.AreEqual(1L, initial.ByType.Sum(x => x.Count));
            Assert.AreEqual(111L, initial.TodayCreatedCount);
            Assert.AreEqual(0L, initial.TodayAccessCount);

            using (var invalidFilter = await SendAsync(client, token, HttpMethod.Get, Items + "?status=123"))
                Assert.AreEqual(HttpStatusCode.BadRequest, invalidFilter.StatusCode);
            using (var noKey = Authorized(HttpMethod.Get, $"{Items}/{item.Id}/content", token))
            {
                noKey.Headers.Range = new RangeHeaderValue(0, 1);
                using var rejected = await client.SendAsync(noKey);
                Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
            }
            var downloadKey = Guid.CreateVersion7();
            for (var n = 0; n < 2; n++)
            {
                using var request = Authorized(HttpMethod.Get, $"{Items}/{item.Id}/content?requestKey={downloadKey}", token);
                request.Headers.Range = new RangeHeaderValue(n, n + 1);
                using var response = await client.SendAsync(request);
                Assert.AreEqual(HttpStatusCode.PartialContent, response.StatusCode, await response.Content.ReadAsStringAsync());
            }
            var previewKey = Guid.CreateVersion7();
            for (var n = 0; n < 2; n++)
                using (var response = await SendAsync(client, token, HttpMethod.Get, $"{Items}/{item.Id}/preview?requestKey={previewKey}"))
                    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var share = await JsonAsync<HostDocumentShareResponse>(client, token, HttpMethod.Post, "/api/v1/document/host/shares",
                new CreateHostDocumentShareRequest(item.Id, 1), HttpStatusCode.Created);
            var path = $"/api/v1/document/public/shares/{share.ShareCode}";
            var openBody = new AccessHostDocumentShareRequest { RequestKey = Guid.CreateVersion7() };
            var access = await JsonAsync<HostDocumentShareAccessResponse>(client, null, HttpMethod.Post, path + "/access", openBody);
            await JsonAsync<HostDocumentShareAccessResponse>(client, null, HttpMethod.Post, path + "/access", openBody);
            foreach (var type in new[] { "download", "preview" })
                for (var n = 0; n < 2; n++)
                    using (var response = await SendAsync(client, null, HttpMethod.Post, path + "/content",
                        new AccessHostDocumentShareRequest { PreviewGrant = access.PreviewGrant, AccessTypeKey = type }))
                        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            var after = await GetAsync<HostDocumentStatisticsResponse>(client, token, Statistics);
            Assert.AreEqual(5L, after.TodayAccessCount);
            Assert.AreEqual(2L, after.TodayDownloadCount); Assert.AreEqual(2L, after.TodayPreviewCount);
            Assert.AreEqual(1L, after.TodayShareAccessCount);

            // 将原逻辑下载移到昨天，今天同键重放不能增加事件或刷新原发生时间。
            await commands.ExecuteAsync(new SqlStatement("test.document_batch_two.utc", """
                UPDATE fn_document_access_log SET OccurredAtUtc = @Yesterday
                WHERE DocumentItemId = @Item AND SourceKey = 'authenticated' AND AccessTypeKey = 'download'
                """, SqlDataScope.HostOnly), Args(("Yesterday", day.AddTicks(-1)), ("Item", item.Id)));
            using (var response = await SendAsync(client, token, HttpMethod.Get, $"{Items}/{item.Id}/content?requestKey={downloadKey}"))
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var utc = await GetAsync<HostDocumentStatisticsResponse>(client, token, Statistics);
            Assert.AreEqual(1L, utc.TodayDownloadCount);
            // 半开区间上界不计当日，下界计入；数据归属仍由当前文档 ACL 决定。
            await commands.ExecuteAsync(new SqlStatement("test.document_batch_two.upper", """
                UPDATE fn_document_access_log SET OccurredAtUtc = @Tomorrow
                WHERE DocumentItemId = @Item AND SourceKey = 'authenticated' AND AccessTypeKey = 'preview'
                """, SqlDataScope.HostOnly), Args(("Tomorrow", day.AddDays(1)), ("Item", item.Id)));
            Assert.AreEqual(1L, (await GetAsync<HostDocumentStatisticsResponse>(client, token, Statistics)).TodayPreviewCount);
            await commands.ExecuteAsync(new SqlStatement("test.document_batch_two.lower", """
                UPDATE fn_document_access_log SET OccurredAtUtc = @Day
                WHERE DocumentItemId = @Item AND AccessTypeKey = 'share_access'
                """, SqlDataScope.HostOnly), Args(("Day", day), ("Item", item.Id)));
            Assert.AreEqual(1L, (await GetAsync<HostDocumentStatisticsResponse>(client, token, Statistics)).TodayShareAccessCount);
            var latest = await GetAsync<HostDocumentItemResponse>(client, token, $"{Items}/{item.Id}");
            await JsonAsync<IReadOnlyList<HostDocumentPermissionResponse>>(client, token, HttpMethod.Post,
                "/api/v1/document/host/permissions", new SetHostDocumentPermissionsRequest(item.Id, [], "restricted", latest.Version));
            Assert.AreEqual(110L, (await GetAsync<HostDocumentStatisticsResponse>(client, other.AccessToken, Statistics)).Summary.TotalItems);
            var hidden = await GetAsync<PagedResult<HostDocumentItemResponse>>(client, other.AccessToken, $"{Items}?keyword=Actual");
            Assert.AreEqual(0L, hidden.Total); Assert.HasCount(0, hidden.Items);
            Assert.AreEqual(0L, (await GetAsync<HostDocumentStatisticsResponse>(client, other.AccessToken, Statistics)).TodayAccessCount);
            await commands.ExecuteAsync(new SqlStatement("test.document_batch_two.states", """
                UPDATE fn_document_item SET Status = 0 WHERE Id = @Target;
                UPDATE fn_document_item SET Status = 2 WHERE Title = 'Bulk 000' AND TenantId IS NULL;
                UPDATE fn_document_item SET IsDeleted = 1, DeletedAtUtc = @Now, DeletedByUserId = @Actor WHERE Title = 'Bulk 001' AND TenantId IS NULL
                """, SqlDataScope.HostOnly), Args(("Target", targetId), ("Now", day), ("Actor", item.CreatedByUserId)));
            Assert.AreEqual(107L, (await GetAsync<HostDocumentStatisticsResponse>(client, other.AccessToken, Statistics)).Summary.TotalItems);
            Assert.AreEqual(0L, (await GetAsync<PagedResult<HostDocumentItemResponse>>(client, other.AccessToken, $"{Items}?keyword=Needle&status=0")).Total);
            Assert.AreEqual(1L, (await GetAsync<PagedResult<HostDocumentItemResponse>>(client, token, $"{Items}?keyword=Needle&status=0")).Total);
        }
        finally { tenant.Clear(); }
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Category_moves_and_reference_creation_serialize_across_requests(DatabaseProvider provider)
    {
        using var factory = await FactoryAsync(provider);
        using var client = factory.CreateClientForHost("localhost");
        var identity = await factory.CreateHostIdentityAsync($"doc-tree-{Guid.NewGuid():N}", Permissions);
        var token = identity.AccessToken;
        var a = await CategoryAsync(client, token, "A"); var b = await CategoryAsync(client, token, "B");
        var moveResults = await Task.WhenAll(
            SendAsync(client, token, HttpMethod.Put, Categories + "/" + a.Id, new UpdateHostDocumentCategoryRequest(a.Name, b.Id, 0, a.Version)),
            SendAsync(client, token, HttpMethod.Put, Categories + "/" + b.Id, new UpdateHostDocumentCategoryRequest(b.Name, a.Id, 0, b.Version)));
        try
        {
            Assert.AreEqual(1, moveResults.Count(x => x.StatusCode == HttpStatusCode.OK));
            Assert.AreEqual(1, moveResults.Count(x => x.StatusCode == HttpStatusCode.BadRequest),
                string.Join(" | ", await Task.WhenAll(moveResults.Select(x => x.Content.ReadAsStringAsync()))));
        }
        finally { foreach (var result in moveResults) result.Dispose(); }
        var tree = await GetAsync<IReadOnlyList<HostDocumentCategoryResponse>>(client, token, Categories);
        var child = tree.Single(x => x.ParentId.HasValue);
        var root = tree.Single(x => x.Id == child.ParentId);
        using (var invalid = await SendAsync(client, token, HttpMethod.Put, Categories + "/" + root.Id,
            new UpdateHostDocumentCategoryRequest(root.Name, child.Id, 0, root.Version)))
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        var leaf = await CategoryAsync(client, token, "Race");
        var race = await Task.WhenAll(
            SendAsync(client, token, HttpMethod.Post, Categories + "/" + leaf.Id + "/delete", new DeleteHostDocumentCategoryRequest(leaf.Version)),
            SendAsync(client, token, HttpMethod.Post, Items,
                new CreateHostDocumentItemRequest("Reference race", null, HostDocumentType.Document, HostDocumentStatus.Draft, 0, null, leaf.Id, [])));
        try
        {
            Assert.AreEqual(1, race.Count(x => x.IsSuccessStatusCode),
                string.Join(" | ", await Task.WhenAll(race.Select(x => x.Content.ReadAsStringAsync()))));
            if (race[1].IsSuccessStatusCode)
            {
                var created = (await race[1].Content.ReadFromJsonAsync<HostDocumentItemResponse>())!;
                await JsonAsync<bool>(client, token, HttpMethod.Post, $"{Items}/{created.Id}/delete", new DeleteHostDocumentItemRequest(created.Version));
                using var inRecycle = await SendAsync(client, token, HttpMethod.Post, Categories + "/" + leaf.Id + "/delete", new DeleteHostDocumentCategoryRequest(leaf.Version));
                Assert.AreEqual(HttpStatusCode.UnprocessableEntity, inRecycle.StatusCode);
            }
        }
        finally { foreach (var result in race) result.Dispose(); }
    }

    private const string Items = "/api/v1/document/host/items";
    private const string Categories = "/api/v1/document/host/categories";
    private const string Statistics = "/api/v1/document/host/statistics";
    private static readonly string[] Permissions = [HostDocumentPermissions.Read, HostDocumentPermissions.Create, HostDocumentPermissions.Update,
        HostDocumentPermissions.AddVersion, HostDocumentPermissions.Publish, HostDocumentPermissions.Delete, HostDocumentPermissions.Download,
        HostDocumentCategoryPermissions.Read, HostDocumentCategoryPermissions.Create, HostDocumentCategoryPermissions.Update, HostDocumentCategoryPermissions.Delete,
        HostDocumentPermissionManagementPermissions.Set, HostDocumentPermissionManagementPermissions.Read, HostDocumentSharePermissions.Create,
        HostDocumentStatisticsPermissions.Read];
    private static async Task<FullNetApiFactory> FactoryAsync(DatabaseProvider provider)
    {
        var factory = new FullNetApiFactory(provider, provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await factory.InitializeAsync(); return factory;
    }
    private static Task<HostDocumentCategoryResponse> CategoryAsync(HttpClient client, string token, string name, Guid? parent = null) =>
        JsonAsync<HostDocumentCategoryResponse>(client, token, HttpMethod.Post, Categories,
            new CreateHostDocumentCategoryRequest(name, parent, 0), HttpStatusCode.Created);
    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] args) => args.ToDictionary(x => x.Key, x => x.Value);
    private static async Task<HostDocumentItemResponse> UploadAsync(HttpClient client, string token, Guid id, int size)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Enumerable.Repeat((byte)'x', size).ToArray()); file.Headers.ContentType = new("text/plain");
        form.Add(file, "file", "batch-two.txt"); form.Add(new StringContent(Guid.CreateVersion7().ToString()), "requestKey");
        using var request = Authorized(HttpMethod.Post, $"{Items}/{id}/versions/upload", token); request.Content = form;
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>())!;
    }
    private static Task<T> GetAsync<T>(HttpClient client, string token, string path) => JsonAsync<T>(client, token, HttpMethod.Get, path);
    private static async Task<T> JsonAsync<T>(HttpClient client, string? token, HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await SendAsync(client, token, method, path, body);
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static HttpRequestMessage Authorized(HttpMethod method, string path, string? token)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new("Bearer", token); return request;
    }
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string? token, HttpMethod method, string path, object? body = null)
    {
        using var request = Authorized(method, path, token); if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
