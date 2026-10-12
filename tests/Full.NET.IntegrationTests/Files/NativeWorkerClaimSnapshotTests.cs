using System.Data;
using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>原生 Worker 实际等待释放写锁后，必须保留已并发确认的 Claim。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerClaimSnapshotTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task NativeWorker_preserves_confirmation_after_pending_probe(DatabaseProvider provider)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason)) Assert.Inconclusive(reason ?? "Native Worker artifact unavailable.");
        var ct = CancellationToken.None;
        var cs = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, cs, ct);
        var mysql = provider == DatabaseProvider.MySql;
        var observerCs = mysql ? new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync())
            { Database = new MySqlConnectionStringBuilder(cs).Database }.ConnectionString : cs;
        await using var holder = Connection(provider, cs);
        await using var observer = Connection(provider, observerCs);
        await holder.OpenAsync(ct);
        await observer.OpenAsync(ct);
        var fileId = Guid.CreateVersion7();
        var claimId = Guid.CreateVersion7();
        object stamp = mysql ? DateTime.UtcNow.AddHours(-1) : DateTimeOffset.UtcNow.AddHours(-1);
        await holder.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_files_file
                (Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, StorageKey, StorageState, CreatedAtUtc, CreatedByUserId)
            VALUES (@Id, NULL, 'native-claim-cas.bin', 'application/octet-stream', 1, 'local', @StorageKey, 'ready', @Stamp, @Actor)
            """, new { Id = fileId, StorageKey = "claim-cas/" + fileId.ToString("N"), Stamp = stamp, Actor = Guid.CreateVersion7() }));
        await InsertClaimAsync(holder, claimId, fileId, stamp);
        var holderId = await holder.QuerySingleAsync<long>(mysql ? "SELECT CONNECTION_ID()" : "SELECT CAST(@@SPID AS bigint)");
        await using var transaction = await holder.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        // U 锁允许 SQL Server 扫描读取；MySQL 普通快照读取也不等待此行的 FOR UPDATE 锁。
        await holder.QuerySingleAsync<int>(new CommandDefinition(mysql
            ? "SELECT 1 FROM fn_files_file_reference_claim WHERE Id = @Id FOR UPDATE"
            : "SELECT 1 FROM fn_files_file_reference_claim WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id",
            new { Id = claimId }, transaction));
        var committed = false;
        await using var host = await NativeWorkerProcessHost.StartAsync(artifact, provider, cs, NativeAotTestTimeouts.ProcessStartup,
            ct, enableFilesReferenceClaimReconciliation: true);
        try
        {
            var sql = mysql ? """
                SELECT COUNT(*) FROM performance_schema.data_lock_waits w
                JOIN performance_schema.threads b ON b.THREAD_ID = w.BLOCKING_THREAD_ID
                JOIN performance_schema.threads r ON r.THREAD_ID = w.REQUESTING_THREAD_ID
                JOIN performance_schema.data_locks l ON l.ENGINE = w.ENGINE AND l.ENGINE_LOCK_ID = w.REQUESTING_ENGINE_LOCK_ID
                WHERE b.PROCESSLIST_ID = @HolderId AND l.OBJECT_SCHEMA = DATABASE() AND l.OBJECT_NAME = 'fn_files_file_reference_claim'
                  AND l.LOCK_MODE LIKE 'X%' AND r.PROCESSLIST_INFO LIKE '%UPDATE fn_files_file_reference_claim%'
                """ : """
                SELECT COUNT_BIG(*) FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) s
                WHERE r.database_id = DB_ID() AND r.blocking_session_id = @HolderId
                  AND s.text LIKE '%UPDATE fn_files_file_reference_claim%' AND s.text LIKE '%ReleasedAtUtc%'
                """;
            var waiting = false;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (!waiting && DateTimeOffset.UtcNow < deadline)
            {
                waiting = await observer.QuerySingleAsync<long>(new CommandDefinition(sql, new { HolderId = holderId })) > 0;
                if (!waiting) await Task.Delay(50, ct);
            }
            Assert.IsTrue(waiting, "Native reconciliation must reach the release UPDATE behind the owned holder.");
            Assert.AreEqual(1, await holder.ExecuteAsync(new CommandDefinition("""
                UPDATE fn_files_file_reference_claim SET State = 'active', ConfirmedAtUtc = @Now, UpdatedAtUtc = @Now
                WHERE Id = @Id AND State = 'pending'
                """, new { Id = claimId, Now = mysql ? (object)DateTime.UtcNow : DateTimeOffset.UtcNow }, transaction)));
            await transaction.CommitAsync(ct);
            committed = true;

            // 后续同轮或下一轮的正常 Pending 释放是进度屏障，避免仅在确认提交后立即读 Active。
            var ordinary = Guid.CreateVersion7();
            await InsertClaimAsync(holder, ordinary, fileId, stamp);
            var released = false;
            deadline = DateTimeOffset.UtcNow.AddSeconds(25);
            while (!released && DateTimeOffset.UtcNow < deadline)
            {
                released = await holder.QuerySingleAsync<string>(new CommandDefinition(
                    "SELECT State FROM fn_files_file_reference_claim WHERE Id = @Id", new { Id = ordinary })) == "released";
                if (!released) await Task.Delay(100, ct);
            }
            Assert.IsTrue(released, "A normal stale Pending claim must still be released by the native worker.");
            await host.StopGracefullyAsync(TimeSpan.FromSeconds(30), ct);
            host.AssertNoFatalMarkersInLogs();
            Assert.AreEqual("active", await holder.QuerySingleAsync<string>(new CommandDefinition(
                "SELECT State FROM fn_files_file_reference_claim WHERE Id = @Id", new { Id = claimId })));
        }
        finally
        {
            if (!committed) await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    private static Task<int> InsertClaimAsync(DbConnection connection, Guid id, Guid fileId, object stamp)
    {
        var reference = Guid.CreateVersion7();
        return connection.ExecuteAsync(new CommandDefinition("""
        INSERT INTO fn_files_file_reference_claim
            (Id, IdempotencyKey, FileId, ConsumerModule, ConsumerReferenceId, State, ContentHash, SizeBytes, CreatedAtUtc, UpdatedAtUtc)
        VALUES (@Id, @Key, @FileId, 'document', @Reference, 'pending', NULL, 1, @Stamp, @Stamp)
        """, new { Id = id, Key = "document-version:" + reference.ToString("D"), FileId = fileId, Reference = reference, Stamp = stamp }));
    }

    private static DbConnection Connection(DatabaseProvider provider, string cs) => provider == DatabaseProvider.SqlServer
        ? new SqlConnection(cs) : new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
}
