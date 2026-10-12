using System.Data.Common;
using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Document.Contracts;
using Full.NET.IntegrationTests.Api;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Document;

/// <summary>双库 JIT 与 Native 共用分类引用、恢复和删除竞争验收。</summary>
internal static class DocumentCategoryReferenceAssertions
{
    private const string Items = "/api/v1/document/host/items";
    private const string Categories = "/api/v1/document/host/categories";

    public static async Task VerifyWaitingDeleteAsync(FullNetApiFactory factory, HttpClient client, string token,
        CancellationToken cancellationToken)
    {
        var parent = await CategoryAsync(client, token, cancellationToken);
        var mysql = factory.Provider == DatabaseProvider.MySql;
        var connectionString = mysql
            ? MySqlConnectionStringPolicy.Create(factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false)
            : factory.ConnectionString;
        await using DbConnection holder = mysql ? new MySqlConnection(connectionString) : new SqlConnection(connectionString);
        // 观测连接独立于应用事务；MySQL 仅使用夹具管理员观察锁，不改变应用权限。
        var observerString = mysql ? new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync())
        {
            Database = new MySqlConnectionStringBuilder(connectionString).Database,
        }.ConnectionString : connectionString;
        await using DbConnection observer = mysql ? new MySqlConnection(observerString) : new SqlConnection(observerString);
        await holder.OpenAsync(cancellationToken);
        await observer.OpenAsync(cancellationToken);
        var holderId = await holder.QuerySingleAsync<long>(mysql ? "SELECT CONNECTION_ID()" : "SELECT CAST(@@SPID AS bigint)");
        await using var transaction = await holder.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        // 模拟引用写入事务已经取得父锁、但文档尚未提交的窗口。
        await holder.ExecuteAsync(new CommandDefinition("UPDATE fn_document_category SET Version = Version WHERE Id = @Id",
            new { parent.Id }, transaction, cancellationToken: cancellationToken));
        var deleting = DeleteCategoryAsync(client, token, parent, cancellationToken);
        var committed = false;
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            var waiting = false;
            while (!waiting && DateTimeOffset.UtcNow < deadline)
            {
                var sql = mysql ? """
                    SELECT COUNT(*) FROM information_schema.innodb_trx t
                    JOIN information_schema.PROCESSLIST p ON p.ID = t.trx_mysql_thread_id
                    WHERE t.trx_state = 'LOCK WAIT' AND p.DB = DATABASE()
                      AND t.trx_query LIKE '%fn_document_category%'
                    """ : """
                    SELECT COUNT_BIG(*) FROM sys.dm_exec_requests r
                    CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) s
                    WHERE r.database_id = DB_ID() AND r.blocking_session_id = @HolderId
                      AND s.text LIKE '%fn_document_category%'
                    """;
                waiting = await observer.QuerySingleAsync<long>(new CommandDefinition(sql, new { HolderId = holderId },
                    cancellationToken: cancellationToken)) > 0;
                if (!waiting) await Task.Delay(50, cancellationToken);
            }
            Assert.IsTrue(waiting, "Category deletion must wait for the reference transaction before counting items.");
            await holder.ExecuteAsync(new CommandDefinition("""
                INSERT INTO fn_document_item (Id, TenantId, CategoryId, Title, CreatedAtUtc, CreatedByUserId, Version)
                VALUES (@Id, NULL, @CategoryId, @Title, @CreatedAtUtc, @Actor, 1)
                """, new { Id = Guid.CreateVersion7(), CategoryId = parent.Id, Title = "waiting-reference-" + Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = DateTime.UtcNow, Actor = Guid.CreateVersion7() }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            using var response = await deleting;
            await ErrorAsync(response, HttpStatusCode.UnprocessableEntity, DocumentErrorCodes.CategoryInUse, cancellationToken);
        }
        finally
        {
            // 屏障失败也释放自有事务，并回收请求，避免影响集中验收的后续场景。
            if (!committed) await transaction.RollbackAsync(CancellationToken.None);
            using var response = await deleting;
        }
    }

    public static async Task VerifyAsync(HttpClient client, string token, DatabaseProvider provider, string connectionString,
        CancellationToken cancellationToken)
    {
        await using DbConnection database = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        await database.OpenAsync(cancellationToken);
        var foreign = await CategoryAsync(client, token, cancellationToken);
        // 构造历史越界引用，仅操作 Document 自有表，验证 Host 不能读取分类侧租户数据。
        await database.ExecuteAsync(new CommandDefinition("UPDATE fn_document_category SET TenantId = @TenantId WHERE Id = @Id",
            new { TenantId = Guid.CreateVersion7(), foreign.Id }, cancellationToken: cancellationToken));
        var deleted = await CategoryAsync(client, token, cancellationToken);
        using (var response = await DeleteCategoryAsync(client, token, deleted, cancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        foreach (var invalid in new[] { Guid.Empty, Guid.CreateVersion7(), foreign.Id, deleted.Id })
        {
            using var response = await SendAsync(client, HttpMethod.Post, Items, token,
                new CreateHostDocumentItemRequest("invalid-" + Guid.NewGuid().ToString("N"), null) { CategoryId = invalid }, cancellationToken);
            await ErrorAsync(response, HttpStatusCode.BadRequest, DocumentErrorCodes.Invalid, cancellationToken);
        }

        var category = await CategoryAsync(client, token, cancellationToken);
        var item = await ItemAsync(client, token, category.Id, cancellationToken);
        using (var response = await SendAsync(client, HttpMethod.Put, $"{Items}/{item.Id:D}", token,
            new UpdateHostDocumentItemRequest(item.Title, null, item.Version) { CategoryId = foreign.Id }, cancellationToken))
            await ErrorAsync(response, HttpStatusCode.BadRequest, DocumentErrorCodes.Invalid, cancellationToken);
        using (var response = await SendAsync(client, HttpMethod.Put, $"{Items}/{item.Id:D}", token,
            new UpdateHostDocumentItemRequest(item.Title, null, item.Version), cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var updated = await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken);
            Assert.IsNotNull(updated);
            Assert.AreEqual(category.Id, updated.CategoryId);
        }
        using (var response = await DeleteCategoryAsync(client, token, category, cancellationToken))
            await ErrorAsync(response, HttpStatusCode.UnprocessableEntity, DocumentErrorCodes.CategoryInUse, cancellationToken);

        foreach (var recycle in new[] { false, true })
        {
            var parent = await CategoryAsync(client, token, cancellationToken);
            var trash = await ItemAsync(client, token, parent.Id, cancellationToken);
            await DeleteItemAsync(client, token, trash, cancellationToken);
            using (var response = await DeleteCategoryAsync(client, token, parent, cancellationToken))
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using (var response = await RestoreAsync(client, token, trash, recycle, cancellationToken))
            {
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                var restored = await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken);
                Assert.IsNotNull(restored);
                Assert.IsNull(restored.CategoryId);
            }

            var collision = await ItemAsync(client, token, null, cancellationToken);
            await DeleteItemAsync(client, token, collision, cancellationToken);
            _ = await ItemAsync(client, token, null, cancellationToken, collision.Title);
            using var rejected = await RestoreAsync(client, token, collision, recycle, cancellationToken);
            await ErrorAsync(rejected, HttpStatusCode.Conflict, DocumentErrorCodes.TitleConflict, cancellationToken);
        }

        foreach (var operation in new[] { "create", "update", "restore", "recycle" })
        {
            var parent = await CategoryAsync(client, token, cancellationToken);
            HostDocumentItemResponse? target = operation == "create" ? null : await ItemAsync(client, token,
                operation == "update" ? null : parent.Id, cancellationToken);
            if (operation is "restore" or "recycle") await DeleteItemAsync(client, token, target!, cancellationToken);
            var write = operation switch
            {
                "create" => SendAsync(client, HttpMethod.Post, Items, token,
                    new CreateHostDocumentItemRequest("race-" + Guid.NewGuid().ToString("N"), null) { CategoryId = parent.Id }, cancellationToken),
                "update" => SendAsync(client, HttpMethod.Put, $"{Items}/{target!.Id:D}", token,
                    new UpdateHostDocumentItemRequest(target.Title, null, target.Version) { CategoryId = parent.Id }, cancellationToken),
                _ => RestoreAsync(client, token, target!, operation == "recycle", cancellationToken),
            };
            var responses = await Task.WhenAll(DeleteCategoryAsync(client, token, parent, cancellationToken), write);
            try
            {
                if (responses[0].StatusCode == HttpStatusCode.OK)
                {
                    if (operation is "restore" or "recycle")
                    {
                        Assert.AreEqual(HttpStatusCode.OK, responses[1].StatusCode);
                        var restored = await responses[1].Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken);
                        Assert.IsNotNull(restored);
                        Assert.IsNull(restored.CategoryId);
                    }
                    else await ErrorAsync(responses[1], HttpStatusCode.BadRequest, DocumentErrorCodes.Invalid, cancellationToken);
                }
                else
                {
                    await ErrorAsync(responses[0], HttpStatusCode.UnprocessableEntity, DocumentErrorCodes.CategoryInUse, cancellationToken);
                    Assert.AreEqual(operation == "create" ? HttpStatusCode.Created : HttpStatusCode.OK, responses[1].StatusCode);
                }
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }

        var legacy = await ItemAsync(client, token, null, cancellationToken);
        await database.ExecuteAsync(new CommandDefinition("UPDATE fn_document_item SET CategoryId = @CategoryId WHERE Id = @Id",
            new { CategoryId = foreign.Id, legacy.Id }, cancellationToken: cancellationToken));
        using var statistics = await SendAsync(client, HttpMethod.Get, "/api/v1/document/host/statistics", token, null, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, statistics.StatusCode);
        var overview = await statistics.Content.ReadFromJsonAsync<HostDocumentStatisticsResponse>(cancellationToken);
        Assert.IsNotNull(overview);
        Assert.IsFalse(overview.ByCategory.Any(row => row.CategoryId == foreign.Id || row.CategoryName == foreign.Name));
    }

    private static async Task<HostDocumentCategoryResponse> CategoryAsync(HttpClient client, string token, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(client, HttpMethod.Post, Categories, token,
            new CreateHostDocumentCategoryRequest("reference-" + Guid.NewGuid().ToString("N"), null, 0), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HostDocumentCategoryResponse>(cancellationToken))!;
    }

    private static async Task<HostDocumentItemResponse> ItemAsync(HttpClient client, string token, Guid? category,
        CancellationToken cancellationToken, string? title = null)
    {
        using var response = await SendAsync(client, HttpMethod.Post, Items, token,
            new CreateHostDocumentItemRequest(title ?? "reference-" + Guid.NewGuid().ToString("N"), null) { CategoryId = category }, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        return (await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(cancellationToken))!;
    }

    private static Task<HttpResponseMessage> DeleteCategoryAsync(HttpClient client, string token, HostDocumentCategoryResponse row, CancellationToken cancellationToken) =>
        SendAsync(client, HttpMethod.Post, $"{Categories}/{row.Id:D}/delete", token, new DeleteHostDocumentCategoryRequest(row.Version), cancellationToken);

    private static async Task DeleteItemAsync(HttpClient client, string token, HostDocumentItemResponse row, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(client, HttpMethod.Post, $"{Items}/{row.Id:D}/delete", token, new DeleteHostDocumentItemRequest(row.Version), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<HttpResponseMessage> RestoreAsync(HttpClient client, string token, HostDocumentItemResponse row, bool recycle, CancellationToken cancellationToken) =>
        SendAsync(client, HttpMethod.Post, $"{(recycle ? "/api/v1/document/host/recycle-bin" : Items)}/{row.Id:D}/restore", token,
            new RestoreHostDocumentItemRequest(row.Version + 1), cancellationToken);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string token, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken cancellationToken)
    {
        Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(code, problem.RootElement.GetProperty("code").GetString());
    }
}
