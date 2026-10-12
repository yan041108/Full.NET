using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Document;

/// <summary>JIT/Native 共用目录名称与标签引用验收，确定性等待屏障由 JIT 双库执行。</summary>
internal static class DocumentTaxonomyConcurrencyAssertions
{
    private const string Categories = "/api/v1/document/host/categories";
    private const string Tags = "/api/v1/document/host/tags";
    private const string Items = "/api/v1/document/host/items";
    private static readonly Guid CategoryGuard = Guid.Parse("01a12ef8-d360-7d89-91f6-7e58ddbfcd73");
    private static readonly Guid TagGuard = Guid.Parse("01a12ef8-d36c-78d9-b24b-93aa39be0747");

    public static async Task VerifyAsync(HttpClient client, string token, DatabaseProvider provider, string cs, CancellationToken ct)
    {
        await using var database = Connection(provider, cs);
        await database.OpenAsync(ct);
        Assert.AreEqual(0L, await database.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_document_category WHERE TenantId IS NULL AND IsDeleted = 0"),
            "Run before other taxonomy fixtures so first-category admission is tested against an empty active catalog.");
        foreach (var category in new[] { true, false })
        {
            var code = category ? DocumentErrorCodes.CategoryNameExists : DocumentErrorCodes.TagNameExists;
            var name = Name();
            await OneWinnerAsync(CreateAsync(client, token, category, name, null, ct), CreateAsync(client, token, category, name, null, ct), code, ct);
            var id = await FindIdAsync(database, category, name, ct);
            using (var unchanged = await UpdateAsync(client, token, category, id, name, 1, ct))
                Assert.AreEqual(HttpStatusCode.OK, unchanged.StatusCode, await unchanged.Content.ReadAsStringAsync(ct));
            var otherId = await CatalogAsync(client, token, category, Name(), null, ct);
            var contested = Name();
            await OneWinnerAsync(CreateAsync(client, token, category, contested, null, ct), UpdateAsync(client, token, category, otherId, contested, 1, ct), code, ct);

        }

        // 分类名称只在同一父级唯一，不将目录门禁变成全局名称约束。
        var left = await CatalogAsync(client, token, true, Name(), null, ct);
        var right = await CatalogAsync(client, token, true, Name(), null, ct);
        var childName = Name();
        _ = await CatalogAsync(client, token, true, childName, left, ct);
        _ = await CatalogAsync(client, token, true, childName, right, ct);

        foreach (var update in new[] { false, true })
        {
            var tag = await CatalogAsync(client, token, false, Name(), null, ct);
            HostDocumentItemResponse? item = update ? await ItemAsync(client, token, [], ct) : null;
            var responses = await Task.WhenAll(DeleteTagAsync(client, token, tag, ct), WriteItemAsync(client, token, item, [tag], ct));
            try
            {
                if (responses[0].StatusCode == HttpStatusCode.OK)
                    await ErrorAsync(responses[1], HttpStatusCode.BadRequest, DocumentErrorCodes.InvalidTag, ct);
                else
                {
                    await ErrorAsync(responses[0], HttpStatusCode.UnprocessableEntity, DocumentErrorCodes.TagInUse, ct);
                    Assert.AreEqual(update ? HttpStatusCode.OK : HttpStatusCode.Created, responses[1].StatusCode, await responses[1].Content.ReadAsStringAsync(ct));
                }
            }
            finally { foreach (var response in responses) response.Dispose(); }
            Assert.AreEqual(0L, await database.QuerySingleAsync<long>(new CommandDefinition("""
                SELECT COUNT(*) FROM fn_document_tag_assignment a JOIN fn_document_tag t ON t.Id = a.TagId
                WHERE a.TagId = @TagId AND (t.IsDeleted <> 0 OR t.TenantId IS NOT NULL)
                """, new { TagId = tag }, cancellationToken: ct)));
        }
        await VerifyHistoricalNamesAsync(database, client, token, provider, ct);
    }

    private static async Task VerifyHistoricalNamesAsync(DbConnection db, HttpClient client, string token, DatabaseProvider provider, CancellationToken ct)
    {
        foreach (var category in new[] { true, false })
        {
            // 仅本用例的隔离数据库模拟缺失唯一索引的历史结构；正式并发场景已先完成。
            var drop = category ? "DROP INDEX UX_fn_document_category_Scope_Parent_Name ON fn_document_category"
                : "DROP INDEX UX_fn_document_tag_Scope_Name ON fn_document_tag";
            await db.ExecuteAsync(new CommandDefinition(drop, cancellationToken: ct));
            var ids = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
            try
            {
                var legacy = Name();
                foreach (var id in ids) await InsertCatalogAsync(db, null, category, id, legacy, ct);
                var code = category ? DocumentErrorCodes.CategoryNameExists : DocumentErrorCodes.TagNameExists;
                using (var response = await CreateAsync(client, token, category, legacy, null, ct))
                    await ErrorAsync(response, HttpStatusCode.Conflict, code, ct);
                var target = await CatalogAsync(client, token, category, Name(), null, ct);
                using (var response = await UpdateAsync(client, token, category, target, legacy, 1, ct))
                    await ErrorAsync(response, HttpStatusCode.Conflict, code, ct);
                Assert.AreEqual(1L, await VersionAsync(db, category, target, ct));
            }
            finally
            {
                // 只软删除本夹具的两个同名行，并恢复 053 的正式索引，不污染后续共享验收。
                var cleanup = category ? "UPDATE fn_document_category SET IsDeleted = 1, DeletedAtUtc = @Now, DeletedByUserId = @Actor WHERE Id IN @Ids AND TenantId IS NULL"
                    : "UPDATE fn_document_tag SET IsDeleted = 1, DeletedAtUtc = @Now, DeletedByUserId = @Actor WHERE Id IN @Ids AND TenantId IS NULL";
                await db.ExecuteAsync(new CommandDefinition(cleanup, new { Ids = ids, Now = DateTime.UtcNow, Actor = Guid.CreateVersion7() }));
                var restore = category ? "CREATE UNIQUE INDEX UX_fn_document_category_Scope_Parent_Name ON fn_document_category(TenantId, ParentId, Name)"
                    : "CREATE UNIQUE INDEX UX_fn_document_tag_Scope_Name ON fn_document_tag(TenantId, Name)";
                // 精确恢复 053：SQL Server 过滤活动行；MySQL 保持原列集合与 NULL 语义。
                if (provider == DatabaseProvider.SqlServer) restore += " WHERE IsDeleted = 0";
                await db.ExecuteAsync(new CommandDefinition(restore));
            }
        }
    }

    public static async Task VerifyWaitingEmptyCategoryAsync(FullNetApiFactory factory, HttpClient client, string token, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        var name = Name();
        await WaitAndCommitAsync(factory, CategoryGuard, () => CreateAsync(client, token, true, name, null, ct),
            (db, tx) => InsertCatalogAsync(db, tx, true, id, name, ct), HttpStatusCode.Conflict, DocumentErrorCodes.CategoryNameExists, ct);
        // 只清理本屏障创建的无引用行，恢复空活动目录供后续共同断言使用。
        await using var database = Connection(factory.Provider, factory.ConnectionString);
        await database.OpenAsync(ct);
        await database.ExecuteAsync(new CommandDefinition("""
            UPDATE fn_document_category SET IsDeleted = 1, DeletedAtUtc = @Now, DeletedByUserId = @Actor
            WHERE Id = @Id AND TenantId IS NULL AND IsDeleted = 0
            """, new { Id = id, Now = DateTime.UtcNow, Actor = Guid.CreateVersion7() }, cancellationToken: ct));
    }

    public static async Task VerifyWaitingTagsAsync(FullNetApiFactory factory, HttpClient client, string token, CancellationToken ct)
    {
        foreach (var update in new[] { false, true })
        {
            var id = update ? await CatalogAsync(client, token, false, Name(), null, ct) : Guid.Empty;
            var name = Name();
            await WaitAndCommitAsync(factory, TagGuard,
                () => update ? UpdateAsync(client, token, false, id, name, 1, ct) : CreateAsync(client, token, false, name, null, ct),
                (db, tx) => InsertCatalogAsync(db, tx, false, Guid.CreateVersion7(), name, ct), HttpStatusCode.Conflict, DocumentErrorCodes.TagNameExists, ct);
        }

        var referenced = await CatalogAsync(client, token, false, Name(), null, ct);
        await WaitAndCommitAsync(factory, TagGuard, () => DeleteTagAsync(client, token, referenced, ct), async (db, tx) =>
        {
            // 模拟关联校验已成功但文档/关联尚未提交的事务窗口。
            var item = Guid.CreateVersion7();
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO fn_document_item (Id, TenantId, Title, CreatedAtUtc, CreatedByUserId, Version)
                VALUES (@Id, NULL, @Title, @Now, @Actor, 1)
                """, new { Id = item, Title = Name(), Now = DateTime.UtcNow, Actor = Guid.CreateVersion7() }, tx, cancellationToken: ct));
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO fn_document_tag_assignment (DocumentItemId, TagId, CreatedAtUtc) VALUES (@ItemId, @TagId, @Now)
                """, new { ItemId = item, TagId = referenced, Now = DateTime.UtcNow }, tx, cancellationToken: ct));
        }, HttpStatusCode.UnprocessableEntity, DocumentErrorCodes.TagInUse, ct);

        foreach (var update in new[] { false, true })
        {
            var tag = await CatalogAsync(client, token, false, Name(), null, ct);
            HostDocumentItemResponse? item = update ? await ItemAsync(client, token, [], ct) : null;
            await WaitAndCommitAsync(factory, TagGuard, () => WriteItemAsync(client, token, item, [tag], ct),
                async (db, tx) => await db.ExecuteAsync(new CommandDefinition("""
                    UPDATE fn_document_tag SET IsDeleted = 1, Version = Version + 1, DeletedAtUtc = @Now, DeletedByUserId = @Actor
                    WHERE Id = @Id AND TenantId IS NULL AND IsDeleted = 0
                    """, new { Id = tag, Now = DateTime.UtcNow, Actor = Guid.CreateVersion7() }, tx, cancellationToken: ct)),
                HttpStatusCode.BadRequest, DocumentErrorCodes.InvalidTag, ct);
        }
    }

    private static async Task WaitAndCommitAsync(FullNetApiFactory factory, Guid scope, Func<Task<HttpResponseMessage>> start,
        Func<DbConnection, DbTransaction, Task> publish, HttpStatusCode status, string code, CancellationToken ct)
    {
        var mysql = factory.Provider == DatabaseProvider.MySql;
        await using var holder = Connection(factory.Provider, factory.ConnectionString);
        var observerCs = mysql ? new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync())
            { Database = new MySqlConnectionStringBuilder(factory.ConnectionString).Database }.ConnectionString : factory.ConnectionString;
        await using var observer = Connection(factory.Provider, observerCs);
        await holder.OpenAsync(ct);
        await observer.OpenAsync(ct);
        var holderId = await holder.QuerySingleAsync<long>(mysql ? "SELECT CONNECTION_ID()" : "SELECT CAST(@@SPID AS bigint)");
        await using var transaction = await holder.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await holder.ExecuteAsync(new CommandDefinition("UPDATE fn_document_taxonomy_write_guard SET TenantId = TenantId WHERE Id = @Id",
            new { Id = scope }, transaction, cancellationToken: ct));
        var pending = start();
        var committed = false;
        try
        {
            // 关联自有持锁会话和当前数据库，观察真实锁等待，不能以请求耗时替代屏障。
            var sql = mysql ? """
                SELECT COUNT(*) FROM performance_schema.data_lock_waits w
                JOIN performance_schema.threads t ON t.THREAD_ID = w.BLOCKING_THREAD_ID
                JOIN performance_schema.data_locks l ON l.ENGINE = w.ENGINE AND l.ENGINE_LOCK_ID = w.REQUESTING_ENGINE_LOCK_ID
                WHERE t.PROCESSLIST_ID = @HolderId AND l.OBJECT_SCHEMA = DATABASE()
                  AND l.OBJECT_NAME = 'fn_document_taxonomy_write_guard'
                """ : """
                SELECT COUNT_BIG(*) FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) s
                WHERE r.database_id = DB_ID() AND r.blocking_session_id = @HolderId AND s.text LIKE '%fn_document_taxonomy_write_guard%'
                """;
            var waiting = false;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (!waiting && DateTimeOffset.UtcNow < deadline)
            {
                waiting = await observer.QuerySingleAsync<long>(new CommandDefinition(sql, new { HolderId = holderId }, cancellationToken: ct)) > 0;
                if (!waiting) await Task.Delay(50, ct);
            }
            Assert.IsTrue(waiting, "Catalog mutation/reference must wait for the owning scope transaction.");
            await publish(holder, transaction);
            await transaction.CommitAsync(ct);
            committed = true;
            using var response = await pending;
            await ErrorAsync(response, status, code, ct);
        }
        finally
        {
            if (!committed) await transaction.RollbackAsync(CancellationToken.None);
            using var response = await pending;
        }
    }

    private static DbConnection Connection(DatabaseProvider provider, string cs) => provider == DatabaseProvider.SqlServer ? new SqlConnection(cs)
        : new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
    private static string Name() => "catalog-" + Guid.NewGuid().ToString("N");
    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string token, bool category, string name, Guid? parent, CancellationToken ct) =>
        SendAsync(client, token, HttpMethod.Post, category ? Categories : Tags,
            category ? new CreateHostDocumentCategoryRequest(name, parent, 0) : (object)new CreateHostDocumentTagRequest(name), ct);
    private static Task<HttpResponseMessage> UpdateAsync(HttpClient client, string token, bool category, Guid id, string name, long version, CancellationToken ct) =>
        SendAsync(client, token, HttpMethod.Put, $"{(category ? Categories : Tags)}/{id:D}",
            category ? new UpdateHostDocumentCategoryRequest(name, null, 0, version) : (object)new UpdateHostDocumentTagRequest(name, version), ct);
    private static async Task<Guid> CatalogAsync(HttpClient client, string token, bool category, string name, Guid? parent, CancellationToken ct)
    {
        using var response = await CreateAsync(client, token, category, name, parent, ct);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("id").GetGuid();
    }
    private static Task<HttpResponseMessage> DeleteTagAsync(HttpClient client, string token, Guid id, CancellationToken ct) =>
        SendAsync(client, token, HttpMethod.Post, $"{Tags}/{id:D}/delete", new DeleteHostDocumentTagRequest(1), ct);
    private static Task<HttpResponseMessage> WriteItemAsync(HttpClient client, string token, HostDocumentItemResponse? row, Guid[] tags, CancellationToken ct) =>
        row is null ? SendAsync(client, token, HttpMethod.Post, Items, new CreateHostDocumentItemRequest(Name(), null) { TagIds = tags }, ct)
            : SendAsync(client, token, HttpMethod.Put, $"{Items}/{row.Id:D}", new UpdateHostDocumentItemRequest(row.Title, null, row.Version) { TagIds = tags }, ct);
    private static async Task<HostDocumentItemResponse> ItemAsync(HttpClient client, string token, Guid[] tags, CancellationToken ct)
    {
        using var response = await WriteItemAsync(client, token, null, tags, ct);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(ct))!;
    }
    private static Task<int> InsertCatalogAsync(DbConnection db, DbTransaction? tx, bool category, Guid id, string name, CancellationToken ct) =>
        db.ExecuteAsync(new CommandDefinition(category ? """
            INSERT INTO fn_document_category (Id, TenantId, ParentId, Name, SortOrder, CreatedAtUtc, Version)
            VALUES (@Id, NULL, NULL, @Name, 0, @Now, 1)
            """ : """
            INSERT INTO fn_document_tag (Id, TenantId, Name, CreatedAtUtc, Version, UseCount, IsHot, IsRecommended)
            VALUES (@Id, NULL, @Name, @Now, 1, 0, 0, 0)
            """, new { Id = id, Name = name, Now = DateTime.UtcNow }, tx, cancellationToken: ct));
    private static Task<Guid> FindIdAsync(DbConnection db, bool category, string name, CancellationToken ct) => db.QuerySingleAsync<Guid>(new CommandDefinition(category
        ? "SELECT Id FROM fn_document_category WHERE TenantId IS NULL AND IsDeleted = 0 AND ParentId IS NULL AND Name = @Name"
        : "SELECT Id FROM fn_document_tag WHERE TenantId IS NULL AND IsDeleted = 0 AND Name = @Name", new { Name = name }, cancellationToken: ct));
    private static Task<long> VersionAsync(DbConnection db, bool category, Guid id, CancellationToken ct) => db.QuerySingleAsync<long>(new CommandDefinition(category
        ? "SELECT Version FROM fn_document_category WHERE Id = @Id AND TenantId IS NULL"
        : "SELECT Version FROM fn_document_tag WHERE Id = @Id AND TenantId IS NULL", new { Id = id }, cancellationToken: ct));
    private static async Task OneWinnerAsync(Task<HttpResponseMessage> first, Task<HttpResponseMessage> second, string code, CancellationToken ct)
    {
        var responses = await Task.WhenAll(first, second);
        try
        {
            Assert.AreEqual(1, responses.Count(response => response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
            await ErrorAsync(responses.Single(response => response.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, code, ct);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, ct);
    }
    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.AreEqual(status, response.StatusCode, body);
        using var json = JsonDocument.Parse(body);
        Assert.AreEqual(code, json.RootElement.GetProperty("code").GetString());
    }
}
