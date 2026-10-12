using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Document;
using Full.NET.Modules.Document.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Document;

/// <summary>JIT 与 Native 共用标签真实引用数及回收站生命周期验收。</summary>
internal static class DocumentTagUseCountAssertions
{
    private const string Tags = "/api/v1/document/host/tags";
    private const string Items = "/api/v1/document/host/items";
    private const string Recycle = "/api/v1/document/host/recycle-bin";

    public static async Task VerifyAsync(HttpClient client, string token, DatabaseProvider provider, string connectionString, CancellationToken ct)
    {
        var a = await ReadAsync<HostDocumentTagResponse>(client, token, HttpMethod.Post, Tags, new CreateHostDocumentTagRequest(Name()), HttpStatusCode.Created, ct);
        var b = await ReadAsync<HostDocumentTagResponse>(client, token, HttpMethod.Post, Tags, new CreateHostDocumentTagRequest(Name()), HttpStatusCode.Created, ct);
        await CountAsync(client, token, a, 0, ct);
        await CountAsync(client, token, b, 0, ct);

        // 重复输入只创建一个关联，统计不能按请求数组长度累加。
        var first = await ItemAsync(client, token, null, [a.Id, a.Id], ct);
        await VerifyMutationReloadAsync(provider, connectionString, a.Id, first.Id, ct);
        await CountAsync(client, token, a, 1, ct);
        var second = await ItemAsync(client, token, null, [a.Id], ct);
        await CountAsync(client, token, a, 2, ct);

        await using var database = Connection(provider, connectionString);
        await database.OpenAsync(ct);
        await database.ExecuteAsync(new CommandDefinition("UPDATE fn_document_tag SET UseCount = 999 WHERE Id IN @Ids AND TenantId IS NULL", new { Ids = new[] { a.Id, b.Id } }, cancellationToken: ct));
        var foreignItem = Guid.CreateVersion7();
        var foreignTenant = Guid.CreateVersion7();
        try
        {
            // 仅在隔离测试库注入自有历史错误关联；不得把租户文档计数泄漏到Host目录。
            await database.ExecuteAsync(new CommandDefinition("""
                INSERT INTO fn_document_item (Id, TenantId, Title, CreatedAtUtc, CreatedByUserId, Version)
                VALUES (@Id, @TenantId, @Title, @Now, @Actor, 1)
                """, new { Id = foreignItem, TenantId = foreignTenant, Title = Name(), Now = DateTime.UtcNow, Actor = Guid.CreateVersion7() }, cancellationToken: ct));
            await database.ExecuteAsync(new CommandDefinition("INSERT INTO fn_document_tag_assignment (DocumentItemId, TagId, CreatedAtUtc) VALUES (@ItemId, @TagId, @Now)",
                new { ItemId = foreignItem, TagId = a.Id, Now = DateTime.UtcNow }, cancellationToken: ct));
            await CountAsync(client, token, a, 2, ct);
            await CountAsync(client, token, b, 0, ct);
            await CapturePlansAsync(database, provider, a.Id, ct);
            a = await ReadAsync<HostDocumentTagResponse>(client, token, HttpMethod.Put, $"{Tags}/{a.Id:D}", new UpdateHostDocumentTagRequest(Name(), a.Version), HttpStatusCode.OK, ct);
            Assert.AreEqual(2, a.UseCount, "Tag mutation reload must use the same authoritative projection.");
        }
        finally
        {
            // 清理必须完成；取消不应留下本测试创建的越界引用。
            await database.ExecuteAsync("DELETE FROM fn_document_tag_assignment WHERE DocumentItemId = @Id", new { Id = foreignItem });
            await database.ExecuteAsync("DELETE FROM fn_document_item WHERE Id = @Id AND TenantId = @TenantId", new { Id = foreignItem, TenantId = foreignTenant });
        }

        first = await ItemAsync(client, token, first, null, ct);
        await CountAsync(client, token, a, 2, ct);
        first = await ItemAsync(client, token, first, [b.Id], ct);
        await CountAsync(client, token, a, 1, ct);
        await CountAsync(client, token, b, 1, ct);
        first = await ItemAsync(client, token, first, [], ct);
        await CountAsync(client, token, b, 0, ct);
        using (var invalid = await SendAsync(client, token, HttpMethod.Put, $"{Items}/{first.Id:D}", new UpdateHostDocumentItemRequest(first.Title, null, first.Version) { TagIds = [Guid.CreateVersion7()] }, ct))
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        await CountAsync(client, token, a, 1, ct);
        await CountAsync(client, token, b, 0, ct);

        await DeleteItemAsync(client, token, second, ct);
        await CountAsync(client, token, a, 1, ct);
        using (var blocked = await SendAsync(client, token, HttpMethod.Post, $"{Tags}/{a.Id:D}/delete", new DeleteHostDocumentTagRequest(a.Version), ct))
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        second = await ReadAsync<HostDocumentItemResponse>(client, token, HttpMethod.Post, $"{Recycle}/{second.Id:D}/restore", new RestoreHostDocumentItemRequest(second.Version + 1), HttpStatusCode.OK, ct);
        await CountAsync(client, token, a, 1, ct);
        await DeleteItemAsync(client, token, second, ct);
        using (var purged = await SendAsync(client, token, HttpMethod.Post, $"{Recycle}/{second.Id:D}/purge", null, ct))
            Assert.AreEqual(HttpStatusCode.OK, purged.StatusCode, await purged.Content.ReadAsStringAsync(ct));
        await CountAsync(client, token, a, 0, ct);
        using (var removed = await SendAsync(client, token, HttpMethod.Post, $"{Tags}/{a.Id:D}/delete", new DeleteHostDocumentTagRequest(a.Version), ct))
            Assert.AreEqual(HttpStatusCode.OK, removed.StatusCode);
        using (var missing = await SendAsync(client, token, HttpMethod.Get, $"{Tags}/{a.Id:D}", null, ct))
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static async Task CountAsync(HttpClient client, string token, HostDocumentTagResponse tag, int count, CancellationToken ct)
    {
        var detail = await ReadAsync<HostDocumentTagResponse>(client, token, HttpMethod.Get, $"{Tags}/{tag.Id:D}", null, HttpStatusCode.OK, ct);
        Assert.AreEqual(count, detail.UseCount, "Detail must count retained Host references, ignoring the legacy counter.");
        Assert.AreEqual(tag.Version, detail.Version, "Derived reference counts must not change tag metadata versions.");
        var list = await ReadAsync<HostDocumentTagResponse[]>(client, token, HttpMethod.Get, Tags, null, HttpStatusCode.OK, ct);
        Assert.AreEqual(count, list.Single(row => row.Id == tag.Id).UseCount);
    }

    private static Task<HostDocumentItemResponse> ItemAsync(HttpClient client, string token, HostDocumentItemResponse? row, Guid[]? tags, CancellationToken ct) => row is null
        ? ReadAsync<HostDocumentItemResponse>(client, token, HttpMethod.Post, Items, new CreateHostDocumentItemRequest(Name(), null) { TagIds = tags }, HttpStatusCode.Created, ct)
        : ReadAsync<HostDocumentItemResponse>(client, token, HttpMethod.Put, $"{Items}/{row.Id:D}", new UpdateHostDocumentItemRequest(row.Title, null, row.Version) { TagIds = tags }, HttpStatusCode.OK, ct);

    private static async Task DeleteItemAsync(HttpClient client, string token, HostDocumentItemResponse row, CancellationToken ct)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post, $"{Items}/{row.Id:D}/delete", new DeleteHostDocumentItemRequest(row.Version), ct);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }

    private static async Task<T> ReadAsync<T>(HttpClient client, string token, HttpMethod method, string path, object? body, HttpStatusCode status, CancellationToken ct)
    {
        using var response = await SendAsync(client, token, method, path, body, ct);
        Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        var value = await response.Content.ReadFromJsonAsync<T>(ct);
        Assert.IsNotNull(value);
        return value;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, ct);
    }

    private static string Name() => "use-count-" + Guid.NewGuid().ToString("N");

    private static async Task VerifyMutationReloadAsync(DatabaseProvider provider, string cs, Guid tagId, Guid itemId, CancellationToken ct)
    {
        await using var tagWriter = Connection(provider, cs);
        await using var itemWriter = Connection(provider, cs);
        await tagWriter.OpenAsync(ct);
        await itemWriter.OpenAsync(ct);
        await using var tagTransaction = await tagWriter.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await using var itemTransaction = await itemWriter.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await tagWriter.ExecuteAsync(new CommandDefinition("UPDATE fn_document_tag SET Name = Name WHERE Id = @Id", new { Id = tagId }, tagTransaction, cancellationToken: ct));
        await itemWriter.ExecuteAsync(new CommandDefinition("UPDATE fn_document_item SET Title = Title WHERE Id = @Id", new { Id = itemId }, itemTransaction, cancellationToken: ct));
        var type = typeof(DocumentModule).Assembly.GetType("Full.NET.Modules.Document.Persistence.DocumentTagSql", throwOnError: true)!;
        // 旧派生实现没有专用回读，回退到其真实FindActiveById以建立锁式RC失败证据。
        var field = type.GetField("FindActiveByIdForMutationReload", BindingFlags.Public | BindingFlags.Static)
            ?? type.GetField("FindActiveById", BindingFlags.Public | BindingFlags.Static)!;
        var statement = (SqlStatement)field.GetValue(null)!;
        try
        {
            // 两个会话已分别持有标签/文档写锁；回读不得继续等待文档锁，消除锁循环的一条边。
            var result = await tagWriter.QuerySingleAsync<CountRow>(new CommandDefinition(statement.Text,
                new { Id = tagId, UseCount = 1 }, tagTransaction, commandTimeout: 2, cancellationToken: ct));
            Assert.AreEqual(1, result.UseCount);
        }
        catch (SqlException error) when (error.Number == -2)
        {
            Assert.Fail("Tag mutation reload waits on a referenced item lock while holding the tag write lock.");
        }
        finally
        {
            await itemTransaction.RollbackAsync();
            await tagTransaction.RollbackAsync();
        }
    }

    private sealed class CountRow { public int UseCount { get; set; } }

    private static async Task CapturePlansAsync(DbConnection database, DatabaseProvider provider, Guid id, CancellationToken ct)
    {
        var directory = Environment.GetEnvironmentVariable("FULLNET_DOCUMENT_TAG_PLAN_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        // 仅在JIT测试进程闭合读取两个正式SQL成员，不扩大生产可见性或Host反射边界。
        var type = typeof(DocumentModule).Assembly.GetType("Full.NET.Modules.Document.Persistence.DocumentTagSql", throwOnError: true)!;
        foreach (var member in new[] { "ListActive", "FindActiveById", "FindActiveByIdForMutationReload" })
        {
            var statement = (SqlStatement)type.GetField(member, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            string plan;
            if (provider == DatabaseProvider.SqlServer)
            {
                await database.ExecuteAsync(new CommandDefinition("SET SHOWPLAN_XML ON", cancellationToken: ct));
                try
                {
                    plan = await database.QuerySingleAsync<string>(new CommandDefinition(statement.Text,
                        new { Id = id, UseCount = 2, IsHot = (bool?)null, IsRecommended = (bool?)null }, cancellationToken: ct));
                }
                finally
                {
                    await database.ExecuteAsync(new CommandDefinition("SET SHOWPLAN_XML OFF", commandTimeout: 10));
                }
            }
            else
            {
                plan = await database.QuerySingleAsync<string>(new CommandDefinition("EXPLAIN FORMAT=JSON " + statement.Text,
                    new { Id = id, UseCount = 2, IsHot = (bool?)null, IsRecommended = (bool?)null }, cancellationToken: ct));
            }
            Assert.IsFalse(string.IsNullOrWhiteSpace(plan));
            await File.WriteAllTextAsync(Path.Combine(directory, provider + "-" + member + "." + (provider == DatabaseProvider.SqlServer ? "xml" : "json")), plan, ct);
        }
    }

    private static DbConnection Connection(DatabaseProvider provider, string cs) => provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
        : new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
}
