using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Document;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>JIT 与原生 HTTP 共用真实行锁竞争；持锁仅触及自有测试数据库的一张业务表。</summary>
internal static class MediaBindingHttpAssertions
{
    private static readonly byte[] Image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aS1sAAAAASUVORK5CYII=");

    internal static async Task<Guid> VerifyAsync(HttpClient client, DatabaseProvider provider, string connectionString, string token, CancellationToken ct)
    {
        await using var db = DocumentReleaseTestConnections.Create(provider, connectionString); await db.OpenAsync(ct);
        var user = await db.QuerySingleAsync<Guid>(new CommandDefinition("SELECT Id FROM fn_identity_user WHERE Username = 'admin'", cancellationToken: ct));
        using var openApi = await client.GetAsync("/openapi/v1.json", ct); openApi.EnsureSuccessStatusCode();
        using var schema = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync(ct)); var paths = schema.RootElement.GetProperty("paths");
        foreach (var path in new[] { "/api/v1/me/profile/avatar", "/api/v1/me/profile/signature", "/api/v1/tenancy/branding/logo", "/api/v1/tenancy/tenants/{tenantId}/branding/logo" })
            foreach (var method in new[] { "post", "delete" })
                Assert.IsTrue(paths.GetProperty(path).GetProperty(method).GetProperty("responses").TryGetProperty("409", out _), path + " " + method);

        foreach (var kind in new[] { "avatar", "signature" })
        {
            // 每种媒体先走首次档案创建；这些档案与所有文件都属于本用例独立库。
            await db.ExecuteAsync(new CommandDefinition("DELETE FROM fn_identity_user_profile WHERE UserId = @User", new { User = user }, cancellationToken: ct));
            var route = "/api/v1/me/profile/" + kind;
            var column = kind == "avatar" ? "AvatarFileId" : "SignatureFileId";
            await VerifyTargetAsync(client, db, provider, connectionString, token, user, user, route,
                "fn_identity_user_profile", "UserId", column, "identity",
                file => kind == "avatar" ? HostFileReferenceClaimIdempotencyKeys.IdentityUserAvatar(user, file)
                    : HostFileReferenceClaimIdempotencyKeys.IdentityUserSignature(user, file), IdentityErrorCodes.ProfileVersionConflict, ct);
        }
        var slug = "media-" + Guid.CreateVersion7().ToString("N");
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tenancy/tenants/")
            { Content = JsonContent.Create(new ProvisionTenantRequest(slug, "媒体验收", slug + ".test")) };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var created = await client.SendAsync(create, ct);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync(ct));
        var tenant = (await created.Content.ReadFromJsonAsync<TenantSummary>(ct))!.Id;
        await VerifyTargetAsync(client, db, provider, connectionString, token, user, tenant,
            $"/api/v1/tenancy/tenants/{tenant:D}/branding/logo", "fn_tenancy_tenant", "Id", "LogoFileId", "tenancy",
            file => HostFileReferenceClaimIdempotencyKeys.TenancyTenantLogo(tenant, file), TenancyErrorCodes.VersionConflict, ct);
        return tenant;
    }

    private static async Task VerifyTargetAsync(HttpClient client, DbConnection db, DatabaseProvider provider, string cs,
        string token, Guid actor, Guid subject, string route, string table, string idColumn, string fileColumn,
        string owner, Func<Guid, string> claimKey, string conflictCode, CancellationToken ct)
    {
        using (var upload = await SendAsync(client, token, route, upload: true, ct))
            Assert.AreEqual(HttpStatusCode.OK, upload.StatusCode, await upload.Content.ReadAsStringAsync(ct));
        var beforeDelete = await ReadFileAsync(db, table, idColumn, fileColumn, subject, ct);
        var beforeDeleteKey = await ReadBindingKeyAsync(db, table, idColumn, fileColumn, subject, ct);
        await RaceAsync(client, db, provider, cs, token, actor, subject, route, table, idColumn, fileColumn,
            owner, claimKey, conflictCode, upload: false, ct);
        Assert.AreEqual("active", await ClaimStateAsync(db, beforeDeleteKey, ct));

        var current = await ReadFileAsync(db, table, idColumn, fileColumn, subject, ct);
        using (var deletion = await SendAsync(client, token, route, upload: false, ct))
            Assert.AreEqual(HttpStatusCode.OK, deletion.StatusCode, await deletion.Content.ReadAsStringAsync(ct));
        Assert.AreEqual("released", await ClaimStateAsync(db, claimKey(current), ct));
        using (var upload = await SendAsync(client, token, route, upload: true, ct))
            Assert.AreEqual(HttpStatusCode.OK, upload.StatusCode, await upload.Content.ReadAsStringAsync(ct));
        var beforeUpload = await ReadFileAsync(db, table, idColumn, fileColumn, subject, ct);
        var beforeUploadKey = await ReadBindingKeyAsync(db, table, idColumn, fileColumn, subject, ct);
        await RaceAsync(client, db, provider, cs, token, actor, subject, route, table, idColumn, fileColumn,
            owner, claimKey, conflictCode, upload: true, ct);
        Assert.AreEqual("active", await ClaimStateAsync(db, beforeUploadKey, ct));
    }

    private static async Task RaceAsync(HttpClient client, DbConnection observer, DatabaseProvider provider, string cs,
        string token, Guid actor, Guid subject, string route, string table, string idColumn, string fileColumn,
        string owner, Func<Guid, string> claimKey, string conflictCode, bool upload, CancellationToken ct)
    {
        var winner = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
        object timestamp = provider == DatabaseProvider.MySql ? now.UtcDateTime : now;
        await observer.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_files_file (Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, StorageKey, StorageState, CreatedAtUtc, CreatedByUserId)
            VALUES (@Id, NULL, 'winner.png', 'image/png', @Size, 'local', @Key, 'ready', @Now, @Actor)
            """, new { Id = winner, Size = Image.Length, Key = "media-proof/" + winner.ToString("N"), Now = timestamp, Actor = actor }, cancellationToken: ct));
        await observer.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_files_file_reference_claim (Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId, State, ContentHash, SizeBytes, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@Id, @Key, @File, @Owner, @Subject, 'active', NULL, @Size, @Now, @Now)
            """, new { Id = Guid.CreateVersion7(), Key = claimKey(winner), File = winner, Owner = owner, Subject = subject, Size = Image.Length, Now = timestamp }, cancellationToken: ct));
        var claimsBefore = (await observer.QueryAsync<Guid>(new CommandDefinition("SELECT Id FROM fn_files_file_reference_claim", cancellationToken: ct))).ToHashSet();
        await using var holder = DocumentReleaseTestConnections.Create(provider, cs); await holder.OpenAsync(ct);
        // 锁等待观测使用夹具管理员连接，不能扩大应用账号的数据库权限。
        var observationCs = provider == DatabaseProvider.MySql
            ? new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync())
                { Database = new MySqlConnectionStringBuilder(cs).Database }.ConnectionString
            : cs;
        await using var lockObserver = DocumentReleaseTestConnections.Create(provider, observationCs);
        await lockObserver.OpenAsync(ct);
        var holderId = await holder.QuerySingleAsync<long>(new CommandDefinition(provider == DatabaseProvider.MySql ? "SELECT CONNECTION_ID()" : "SELECT CAST(@@SPID AS bigint)", cancellationToken: ct));
        await using var transaction = await holder.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var lockSql = provider == DatabaseProvider.MySql
            ? $"SELECT {idColumn} FROM {table} WHERE {idColumn} = @Id FOR UPDATE"
            : $"SELECT {idColumn} FROM {table} WITH (UPDLOCK, ROWLOCK) WHERE {idColumn} = @Id";
        await holder.QuerySingleAsync<Guid>(new CommandDefinition(lockSql, new { Id = subject }, transaction, cancellationToken: ct));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pending = SendAsync(client, token, route, upload, cancellation.Token);
        HttpResponseMessage? response = null; Exception? primary = null; var committed = false;
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20); var waiting = false;
            while (!waiting && DateTimeOffset.UtcNow < deadline)
            {
                if (pending.IsCompleted)
                {
                    response = await pending;
                    Assert.Fail("媒体写入未等待自有持锁会话：" + response.StatusCode + " " + await response.Content.ReadAsStringAsync(ct));
                }
                var waitSql = provider == DatabaseProvider.MySql ? """
                    SELECT COUNT(*) FROM performance_schema.data_lock_waits w
                    JOIN performance_schema.threads t ON t.THREAD_ID = w.BLOCKING_THREAD_ID
                    JOIN performance_schema.data_locks l ON l.ENGINE = w.ENGINE AND l.ENGINE_LOCK_ID = w.REQUESTING_ENGINE_LOCK_ID
                    WHERE t.PROCESSLIST_ID = @Holder AND l.OBJECT_SCHEMA = DATABASE() AND l.OBJECT_NAME = @Table
                    """ : """
                    SELECT COUNT_BIG(*) FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) s
                    WHERE r.database_id = DB_ID() AND r.blocking_session_id = @Holder AND s.text LIKE @TablePattern
                    """;
                waiting = await lockObserver.QuerySingleAsync<long>(new CommandDefinition(waitSql,
                    new { Holder = holderId, Table = table, TablePattern = "%" + table + "%" }, cancellationToken: ct)) > 0;
                if (!waiting) await Task.Delay(50, ct);
            }
            Assert.IsTrue(waiting, "必须观察到媒体 UPDATE 竞争，不能用固定延时冒充快照窗口。");
            await holder.ExecuteAsync(new CommandDefinition($"UPDATE {table} SET {fileColumn} = @Winner, Version = Version + 1 WHERE {idColumn} = @Id",
                new { Winner = winner, Id = subject }, transaction, cancellationToken: ct));
            await transaction.CommitAsync(ct); committed = true;
            response = await pending.WaitAsync(TimeSpan.FromSeconds(30), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, body);
            using var problem = JsonDocument.Parse(body);
            Assert.AreEqual(conflictCode, problem.RootElement.GetProperty("code").GetString());
            Assert.AreEqual(winner, await ReadFileAsync(observer, table, idColumn, fileColumn, subject, ct));
            Assert.AreEqual("active", await ClaimStateAsync(observer, claimKey(winner), ct));
            var added = (await observer.QueryAsync<ClaimRow>(new CommandDefinition(
                "SELECT Id, State FROM fn_files_file_reference_claim", cancellationToken: ct))).Where(row => !claimsBefore.Contains(row.Id)).ToArray();
            Assert.HasCount(upload ? 1 : 0, added);
            if (upload) Assert.AreEqual("released", added[0].State);
        }
        catch (Exception exception) { primary = exception; }
        finally
        {
            // 无论断言或数据库步骤是否失败，先释放自有锁，再取消并排空本次请求，保留首个异常。
            try { if (!committed) await transaction.RollbackAsync(CancellationToken.None); }
            catch (Exception exception) { primary ??= exception; }
            cancellation.Cancel();
            try { response ??= await pending.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None); }
            catch (OperationCanceledException) when (primary is not null) { }
            catch (Exception exception) { primary ??= exception; }
            response?.Dispose();
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private static Task<Guid> ReadFileAsync(DbConnection db, string table, string idColumn, string column, Guid id, CancellationToken ct) =>
        db.QuerySingleAsync<Guid>(new CommandDefinition($"SELECT {column} FROM {table} WHERE {idColumn} = @Id", new { Id = id }, cancellationToken: ct));
    private static Task<string> ReadBindingKeyAsync(DbConnection db, string table, string idColumn, string fileColumn, Guid id, CancellationToken ct) =>
        db.QuerySingleAsync<string>(new CommandDefinition($"SELECT {fileColumn.Replace("FileId", "ClaimKey", StringComparison.Ordinal)} FROM {table} WHERE {idColumn} = @Id", new { Id = id }, cancellationToken: ct));
    private static Task<string> ClaimStateAsync(DbConnection db, string key, CancellationToken ct) =>
        db.QuerySingleAsync<string>(new CommandDefinition("SELECT State FROM fn_files_file_reference_claim WHERE IdempotencyKey = @Key", new { Key = key }, cancellationToken: ct));
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, string route, bool upload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(upload ? HttpMethod.Post : HttpMethod.Delete, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (upload)
        {
            var bytes = new ByteArrayContent(Image); bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            var form = new MultipartFormDataContent(); form.Add(bytes, "file", "image.png"); request.Content = form;
        }
        return await client.SendAsync(request, ct);
    }
    private sealed record ClaimRow(Guid Id, string State);
}
