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

/// <summary>JIT 与 Native 共用标题竞争和历史重复回归；持锁屏障另由真实双库验收。</summary>
internal static class DocumentTitleConcurrencyAssertions
{
    private const string Items = "/api/v1/document/host/items";
    private static readonly Guid GuardId = Guid.Parse("01a12eb3-0272-733d-910a-117839ae8c66");

    public static async Task VerifyAsync(HttpClient client, string token, DatabaseProvider provider, string connectionString, CancellationToken ct)
    {
        await using var database = Connection(provider, connectionString);
        await database.OpenAsync(ct);
        foreach (var operation in new[] { "create", "update", "restore", "recycle" })
        {
            var title = NewTitle();
            HostDocumentItemResponse? target = operation == "create" ? null : await ItemAsync(client, token, operation == "update" ? NewTitle() : title, null, ct);
            if (operation is "restore" or "recycle") await DeleteAsync(client, token, target!, ct);
            await OneWinnerAsync(CreateAsync(client, token, title, null, ct), MutateAsync(client, token, operation, target, title, ct), ct);
            Assert.AreEqual(1L, await CountAsync(database, title, ct));
        }

        // 同名允许存在于不同有效分类；未分类门禁不扩大业务唯一性范围。
        var categories = new List<Guid>();
        for (var index = 0; index < 2; index++)
        {
            using var response = await SendAsync(client, token, HttpMethod.Post, "/api/v1/document/host/categories",
                new CreateHostDocumentCategoryRequest(NewTitle(), null, 0), ct);
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
            categories.Add((await response.Content.ReadFromJsonAsync<HostDocumentCategoryResponse>(ct))!.Id);
        }
        var shared = NewTitle();
        foreach (var category in categories) _ = await ItemAsync(client, token, shared, category, ct);
        _ = await ItemAsync(client, token, shared, null, ct);

        // 两个回收站记录通过不同恢复路由竞争同一标题，也只能恢复一个。
        var first = await ItemAsync(client, token, NewTitle(), null, ct);
        await DeleteAsync(client, token, first, ct);
        var second = await ItemAsync(client, token, first.Title, null, ct);
        await DeleteAsync(client, token, second, ct);
        await OneWinnerAsync(MutateAsync(client, token, "restore", first, first.Title, ct),
            MutateAsync(client, token, "recycle", second, first.Title, ct), ct);

        // 仅注入 Document 自有历史数据：多条活跃同名记录必须返回既有冲突码，不能变成 500。
        var legacyTitle = NewTitle();
        for (var index = 0; index < 2; index++) await InsertAsync(database, legacyTitle, null, ct);
        foreach (var operation in new[] { "create", "update", "restore", "recycle" })
        {
            HostDocumentItemResponse? target = operation == "create" ? null : await ItemAsync(client, token, NewTitle(), null, ct);
            if (operation is "restore" or "recycle")
            {
                await DeleteAsync(client, token, target!, ct);
                await database.ExecuteAsync(new CommandDefinition("UPDATE fn_document_item SET Title = @Title WHERE Id = @Id",
                    new { target!.Id, Title = legacyTitle }, cancellationToken: ct));
            }
            using var response = await MutateAsync(client, token, operation, target, legacyTitle, ct);
            await ConflictAsync(response, ct);
            if (target is not null)
            {
                var version = await database.QuerySingleAsync<int>(new CommandDefinition(
                    "SELECT Version FROM fn_document_item WHERE Id = @Id AND TenantId IS NULL", new { target.Id }, cancellationToken: ct));
                Assert.AreEqual(target.Version + (operation is "restore" or "recycle" ? 1 : 0), version);
            }
        }
        Assert.AreEqual(2L, await CountAsync(database, legacyTitle, ct));
    }

    public static async Task VerifyWaitingAdmissionAsync(FullNetApiFactory factory, HttpClient client, string token, CancellationToken ct)
    {
        var mysql = factory.Provider == DatabaseProvider.MySql;
        await using var holder = Connection(factory.Provider, factory.ConnectionString);
        var observerString = mysql ? new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync())
            { Database = new MySqlConnectionStringBuilder(factory.ConnectionString).Database }.ConnectionString : factory.ConnectionString;
        await using var observer = Connection(factory.Provider, observerString);
        await holder.OpenAsync(ct);
        await observer.OpenAsync(ct);
        var holderId = await holder.QuerySingleAsync<long>(mysql ? "SELECT CONNECTION_ID()" : "SELECT CAST(@@SPID AS bigint)");
        foreach (var operation in new[] { "create", "update", "restore", "recycle" })
        {
            var title = NewTitle();
            HostDocumentItemResponse? target = operation == "create" ? null : await ItemAsync(client, token, operation == "update" ? NewTitle() : title, null, ct);
            if (operation is "restore" or "recycle") await DeleteAsync(client, token, target!, ct);
            await using var transaction = await holder.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await holder.ExecuteAsync(new CommandDefinition("UPDATE fn_document_item_write_guard SET TenantId = TenantId WHERE Id = @Id",
                new { Id = GuardId }, transaction, cancellationToken: ct));
            var pending = MutateAsync(client, token, operation, target, title, ct);
            var committed = false;
            try
            {
                // 观察关联自有持锁会话的真实等待，不能用请求耗时或 Task.WhenAll 代替屏障。
                var sql = mysql ? """
                    SELECT COUNT(*) FROM performance_schema.data_lock_waits w
                    JOIN performance_schema.threads t ON t.THREAD_ID = w.BLOCKING_THREAD_ID
                    JOIN performance_schema.data_locks l ON l.ENGINE = w.ENGINE AND l.ENGINE_LOCK_ID = w.REQUESTING_ENGINE_LOCK_ID
                    WHERE t.PROCESSLIST_ID = @HolderId AND l.OBJECT_SCHEMA = DATABASE()
                      AND l.OBJECT_NAME = 'fn_document_item_write_guard'
                    """ : """
                    SELECT COUNT_BIG(*) FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) s
                    WHERE r.database_id = DB_ID() AND r.blocking_session_id = @HolderId
                      AND s.text LIKE '%fn_document_item_write_guard%'
                    """;
                var waiting = false;
                var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
                while (!waiting && DateTimeOffset.UtcNow < deadline)
                {
                    waiting = await observer.QuerySingleAsync<long>(new CommandDefinition(sql, new { HolderId = holderId }, cancellationToken: ct)) > 0;
                    if (!waiting) await Task.Delay(50, ct);
                }
                Assert.IsTrue(waiting, $"{operation} must wait for the unclassified title admission transaction.");
                await InsertAsync(holder, title, transaction, ct);
                await transaction.CommitAsync(ct);
                committed = true;
                using var response = await pending;
                await ConflictAsync(response, ct);
                Assert.AreEqual(1L, await CountAsync(holder, title, ct));
            }
            finally
            {
                if (!committed) await transaction.RollbackAsync(CancellationToken.None);
                using var response = await pending;
            }
        }
    }

    private static DbConnection Connection(DatabaseProvider provider, string cs) => provider == DatabaseProvider.SqlServer
        ? new SqlConnection(cs) : new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
    private static string NewTitle() => "title-" + Guid.NewGuid().ToString("N");
    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string token, string title, Guid? category, CancellationToken ct) =>
        SendAsync(client, token, HttpMethod.Post, Items, new CreateHostDocumentItemRequest(title, null) { CategoryId = category }, ct);
    private static async Task<HostDocumentItemResponse> ItemAsync(HttpClient client, string token, string title, Guid? category, CancellationToken ct)
    {
        using var response = await CreateAsync(client, token, title, category, ct);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(ct))!;
    }
    private static async Task DeleteAsync(HttpClient client, string token, HostDocumentItemResponse row, CancellationToken ct)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post, $"{Items}/{row.Id:D}/delete", new DeleteHostDocumentItemRequest(row.Version), ct);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
    private static Task<HttpResponseMessage> MutateAsync(HttpClient client, string token, string operation, HostDocumentItemResponse? row, string title, CancellationToken ct) => operation switch
    {
        "create" => CreateAsync(client, token, title, null, ct),
        "update" => SendAsync(client, token, HttpMethod.Put, $"{Items}/{row!.Id:D}", new UpdateHostDocumentItemRequest(title, null, row.Version), ct),
        _ => SendAsync(client, token, HttpMethod.Post, $"{(operation == "recycle" ? "/api/v1/document/host/recycle-bin" : Items)}/{row!.Id:D}/restore",
            new RestoreHostDocumentItemRequest(row.Version + 1), ct),
    };
    private static async Task OneWinnerAsync(Task<HttpResponseMessage> first, Task<HttpResponseMessage> second, CancellationToken ct)
    {
        var responses = await Task.WhenAll(first, second);
        try
        {
            Assert.AreEqual(1, responses.Count(response => response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created));
            Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            await ConflictAsync(responses.Single(response => response.StatusCode == HttpStatusCode.Conflict), ct);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
    private static Task<int> InsertAsync(DbConnection connection, string title, DbTransaction? transaction, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_document_item (Id, TenantId, Title, CreatedAtUtc, CreatedByUserId, Version)
            VALUES (@Id, NULL, @Title, @Now, @Actor, 1)
            """, new { Id = Guid.CreateVersion7(), Title = title, Now = DateTime.UtcNow, Actor = Guid.CreateVersion7() }, transaction, cancellationToken: ct));
    private static Task<long> CountAsync(DbConnection connection, string title, CancellationToken ct) => connection.QuerySingleAsync<long>(new CommandDefinition(
        "SELECT COUNT(*) FROM fn_document_item WHERE TenantId IS NULL AND CategoryId IS NULL AND IsDeleted = 0 AND Title = @Title",
        new { Title = title }, cancellationToken: ct));
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, ct);
    }
    private static async Task ConflictAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, body);
        using var problem = JsonDocument.Parse(body);
        Assert.AreEqual(DocumentErrorCodes.TitleConflict, problem.RootElement.GetProperty("code").GetString());
    }
}
