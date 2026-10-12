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

/// <summary>双库 JIT 与独立 Native API 共用版本竞争、回滚和预览生命周期验收。</summary>
internal static class DocumentLifecycleAssertions
{
    private const string Items = "/api/v1/document/host/items";

    public static async Task VerifyWaitingVersionAdmissionAsync(FullNetApiFactory factory, HttpClient client, string token, CancellationToken ct)
    {
        var item = await ReadAsync<HostDocumentItemResponse>(client, token, HttpMethod.Post, Items,
            new CreateHostDocumentItemRequest("version-lock-" + Guid.NewGuid().ToString("N"), null), HttpStatusCode.Created, ct);
        var first = await UploadAsync(client, token, item.Id, 0, ct);
        var mysql = factory.Provider == DatabaseProvider.MySql;
        var observerString = mysql ? new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync())
            { Database = new MySqlConnectionStringBuilder(factory.ConnectionString).Database }.ConnectionString : factory.ConnectionString;
        await using var holder = Connection(factory.Provider, factory.ConnectionString);
        await using var observer = Connection(factory.Provider, observerString);
        await holder.OpenAsync(ct);
        await observer.OpenAsync(ct);
        var holderId = await holder.QuerySingleAsync<long>(mysql ? "SELECT CONNECTION_ID()" : "SELECT CAST(@@SPID AS bigint)");
        await using var transaction = await holder.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await holder.QuerySingleAsync<int>(new CommandDefinition(mysql
            ? "SELECT 1 FROM fn_document_item WHERE Id = @Id AND TenantId IS NULL FOR UPDATE"
            : "SELECT 1 FROM fn_document_item WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id AND TenantId IS NULL",
            new { item.Id }, transaction, cancellationToken: ct));
        var requests = Enumerable.Range(0, 2).Select(_ => SendAsync(client, token, HttpMethod.Post,
            Items + $"/{item.Id}/versions", new AddHostDocumentVersionRequest(first.CurrentVersion!.FileId), ct)).ToArray();
        var committed = false;
        try
        {
            var sql = mysql ? """
                SELECT COUNT(DISTINCT w.REQUESTING_THREAD_ID) FROM performance_schema.data_lock_waits w
                JOIN performance_schema.threads b ON b.THREAD_ID = w.BLOCKING_THREAD_ID
                JOIN performance_schema.threads r ON r.THREAD_ID = w.REQUESTING_THREAD_ID
                JOIN performance_schema.data_locks l ON l.ENGINE = w.ENGINE AND l.ENGINE_LOCK_ID = w.REQUESTING_ENGINE_LOCK_ID
                WHERE b.PROCESSLIST_ID = @HolderId AND l.OBJECT_SCHEMA = DATABASE() AND l.OBJECT_NAME = 'fn_document_item'
                  AND l.LOCK_MODE LIKE 'X%' AND r.PROCESSLIST_INFO LIKE '%SELECT 1 FROM fn_document_item%'
                """ : """
                WITH WaitTree AS (
                    SELECT r.session_id FROM sys.dm_exec_requests r
                    WHERE r.database_id = DB_ID() AND r.blocking_session_id = @HolderId
                    UNION ALL
                    SELECT r.session_id FROM sys.dm_exec_requests r JOIN WaitTree w ON r.blocking_session_id = w.session_id
                    WHERE r.database_id = DB_ID()
                )
                SELECT COUNT_BIG(DISTINCT r.session_id) FROM WaitTree w JOIN sys.dm_exec_requests r ON r.session_id = w.session_id
                CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) s
                WHERE r.database_id = DB_ID()
                  AND s.text LIKE '%SELECT 1 FROM fn_document_item%' AND s.text LIKE '%UPDLOCK, HOLDLOCK%'
                OPTION (MAXRECURSION 16)
                """;
            var waiting = 0L;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (waiting < 2 && DateTimeOffset.UtcNow < deadline)
            {
                waiting = await observer.QuerySingleAsync<long>(new CommandDefinition(sql, new { HolderId = holderId }, cancellationToken: ct));
                if (waiting < 2) await Task.Delay(50, ct);
            }
            if (waiting < 2)
            {
                // 仅记录隔离测试库的阻塞关系与语句类别，不输出 SQL 参数或连接凭据。
                var diagnosticSql = mysql ? """
                    SELECT DISTINCT r.PROCESSLIST_ID AS Requester, b.PROCESSLIST_ID AS Blocker,
                        l.OBJECT_NAME AS ObjectName, l.LOCK_MODE AS LockMode,
                        CASE WHEN r.PROCESSLIST_INFO IS NULL THEN 1 ELSE 0 END AS InfoMissing,
                        CASE WHEN r.PROCESSLIST_INFO LIKE '%SELECT 1 FROM fn_document_item%' THEN 1 ELSE 0 END AS ItemGuard
                    FROM performance_schema.data_lock_waits w
                    JOIN performance_schema.threads b ON b.THREAD_ID = w.BLOCKING_THREAD_ID
                    JOIN performance_schema.threads r ON r.THREAD_ID = w.REQUESTING_THREAD_ID
                    JOIN performance_schema.data_locks l ON l.ENGINE = w.ENGINE AND l.ENGINE_LOCK_ID = w.REQUESTING_ENGINE_LOCK_ID
                    WHERE l.OBJECT_SCHEMA = DATABASE()
                    """ : """
                    SELECT r.session_id AS Requester, r.blocking_session_id AS Blocker,
                        CASE WHEN s.text LIKE '%SELECT 1 FROM fn_document_item%' THEN 1 ELSE 0 END AS ItemGuard,
                        CASE WHEN s.text LIKE '%fn_files_file%' THEN 1 ELSE 0 END AS FileOperation
                    FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) s
                    WHERE r.database_id = DB_ID() AND r.blocking_session_id > 0
                    """;
                var diagnostics = (await observer.QueryAsync(new CommandDefinition(diagnosticSql, cancellationToken: ct)))
                    .Cast<IDictionary<string, object>>().Select(row => new Dictionary<string, object>(row)).ToArray();
                Console.WriteLine("Document admission diagnostic holder=" + holderId + ": " + JsonSerializer.Serialize(diagnostics));
            }
            // SQL Server 会串联 U 锁等待者；MySQL 依据主表 X 锁及准入 SELECT 判断，不匹配易变的语句尾部。
            Assert.AreEqual(2L, waiting, "Both version requests must wait at the parent admission SELECT.");
            await transaction.CommitAsync(ct);
            committed = true;
            var versions = new List<int>();
            foreach (var request in requests)
            {
                using var response = await request;
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                versions.Add((await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(ct))!.CurrentVersion!.VersionNumber);
            }
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, versions);
        }
        finally
        {
            try
            {
                if (!committed) await transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                // 即使回滚或某个请求失败，也等待全部请求终止并释放所有成功响应。
                try { await Task.WhenAll(requests); }
                finally
                {
                    foreach (var request in requests)
                        if (request.Status == TaskStatus.RanToCompletion) request.Result.Dispose();
                }
            }
        }
    }

    private static DbConnection Connection(DatabaseProvider provider, string connectionString) => provider == DatabaseProvider.SqlServer
        ? new SqlConnection(connectionString) : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));

    public static async Task VerifyAsync(HttpClient client, string token, CancellationToken ct)
    {
        var item = await ReadAsync<HostDocumentItemResponse>(client, token, HttpMethod.Post, Items,
            new CreateHostDocumentItemRequest("lifecycle-" + Guid.NewGuid().ToString("N"), null), HttpStatusCode.Created, ct);
        // 同一文档的并发上传必须成功且分配不同版本号，不能把唯一键异常转换成偶发 500。
        var uploads = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => UploadAsync(client, token, item.Id, index, ct)));
        CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4 }, uploads.Select(value => value.CurrentVersion!.VersionNumber).ToArray());
        var current = await ReadAsync<HostDocumentItemResponse>(client, token, HttpMethod.Get, Items + $"/{item.Id}", null, HttpStatusCode.OK, ct);
        Assert.AreEqual(5L, current.Version);
        var target = uploads.Single(value => value.CurrentVersion!.VersionNumber == 1).CurrentVersion!;
        var rolled = await ReadAsync<HostDocumentItemResponse>(client, token, HttpMethod.Post,
            Items + $"/{item.Id}/versions/{target.Id}/rollback", new RollbackHostDocumentVersionRequest(current.Version), HttpStatusCode.OK, ct);
        Assert.AreEqual(target.Id, rolled.CurrentVersion!.Id);
        using (var rejected = await SendAsync(client, token, HttpMethod.Post,
                   Items + $"/{item.Id}/versions/{target.Id}/delete", new DeleteHostDocumentVersionRequest(rolled.Version), ct))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode, await rejected.Content.ReadAsStringAsync(ct));
            using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(ct));
            Assert.AreEqual(DocumentErrorCodes.VersionAlreadyCurrent, problem.RootElement.GetProperty("code").GetString());
        }

        var preview = await ReadAsync<HostDocumentPreviewTaskResponse>(client, token, HttpMethod.Post,
            "/api/v1/document/host/preview-tasks", new CreateHostDocumentPreviewTaskRequest(item.Id, target.Id), HttpStatusCode.Created, ct);
        Assert.AreEqual(target.FileId, preview.SourceFileId);
        using (var deleted = await SendAsync(client, token, HttpMethod.Post, Items + $"/{item.Id}/delete",
                   new DeleteHostDocumentItemRequest(rolled.Version), ct))
            Assert.AreEqual(HttpStatusCode.OK, deleted.StatusCode, await deleted.Content.ReadAsStringAsync(ct));
        using (var rejected = await SendAsync(client, token, HttpMethod.Post, "/api/v1/document/host/preview-tasks",
                   new CreateHostDocumentPreviewTaskRequest(item.Id, target.Id), ct))
            Assert.AreEqual(HttpStatusCode.NotFound, rejected.StatusCode, await rejected.Content.ReadAsStringAsync(ct));

        // 使用无文件版本的记录验证彻底删除；本场景不声称覆盖 Active Files Claim 的可靠释放。
        var empty = await ReadAsync<HostDocumentItemResponse>(client, token, HttpMethod.Post, Items,
            new CreateHostDocumentItemRequest("purge-lifecycle-" + Guid.NewGuid().ToString("N"), null), HttpStatusCode.Created, ct);
        using (var deleted = await SendAsync(client, token, HttpMethod.Post, Items + $"/{empty.Id}/delete",
                   new DeleteHostDocumentItemRequest(empty.Version), ct))
            Assert.AreEqual(HttpStatusCode.OK, deleted.StatusCode);
        using (var purged = await SendAsync(client, token, HttpMethod.Post, $"/api/v1/document/host/recycle-bin/{empty.Id}/purge", null, ct))
            Assert.AreEqual(HttpStatusCode.OK, purged.StatusCode, await purged.Content.ReadAsStringAsync(ct));
        using var missing = await SendAsync(client, token, HttpMethod.Post, $"/api/v1/document/host/recycle-bin/{empty.Id}/purge", null, ct);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static async Task<HostDocumentItemResponse> UploadAsync(HttpClient client, string token, Guid id, int index, CancellationToken ct)
    {
        using var body = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3, (byte)index]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        body.Add(file, "file", $"lifecycle-{index}.docx");
        using var request = new HttpRequestMessage(HttpMethod.Post, Items + $"/{id}/versions/upload") { Content = body };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, ct);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<HostDocumentItemResponse>(ct))!;
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
}
