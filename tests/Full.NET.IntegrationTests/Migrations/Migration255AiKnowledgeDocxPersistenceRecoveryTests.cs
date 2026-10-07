using System.Data.Common;
using Dapper;
using DbUp;
using DbUp.Engine;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>固定 255 上界验证 DOCX 扩展与中断重放；合成载荷仅证明数据库结构。</summary>
[TestClass]
public sealed class Migration255AiKnowledgeDocxPersistenceRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_docx_persistence_expansion() => RecoveryAsync(true);
    [TestMethod] public Task MySql_recovers_docx_persistence_expansion() => RecoveryAsync(false);
    [TestMethod] public Task SqlServer_keeps_old_guard_when_legacy_rows_are_invalid() => InvalidLegacyAsync(true);
    [TestMethod] public Task MySql_keeps_old_guard_when_legacy_rows_are_invalid() => InvalidLegacyAsync(false);
    private const string Table = "fn_ai_knowledge_index_vector";
    private const string Guard = "CK_fn_ai_knowledge_index_vector_DocxFormats";
    private const string OldGuard = "CK_fn_ai_knowledge_index_vector_Formats";
    private const string Manifest = "fn_ai_knowledge_docx_manifest";
    private const string InsertVector = "INSERT INTO fn_ai_knowledge_index_vector (Id,ProviderKey,ModelId,Dimensions,FormatKey,DistanceKey,PayloadHash,Payload,CreatedAtUtc) VALUES (@OperationId,'openai_compatible','text-embedding-3-small',@Dimensions,@FormatKey,'cosine',@Hash,@Payload,@Now)";
    private const string InsertManifest = "INSERT INTO fn_ai_knowledge_docx_manifest (Id,FormatKey,PayloadHash,Payload,CreatedAtUtc) VALUES (@TaskId,@FormatKey,@Hash,@Payload,@Now)";

    private static async Task RecoveryAsync(bool sqlServer)
    {
        var connectionString = await DatabaseAsync(sqlServer);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        var text = await SeedAsync(db); text["FormatKey"] = "knowledge-vector-batch-v1";
        var pdf = await SeedAsync(db); pdf["FormatKey"] = "knowledge-pdf-vector-batch-v1";
        var docx = await SeedAsync(db); docx["FormatKey"] = "knowledge-docx-vector-batch-v1";
        Assert.AreEqual(1, await db.ExecuteAsync(InsertVector, text)); Assert.AreEqual(1, await db.ExecuteAsync(InsertVector, pdf));
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(InsertVector, docx); });
        Replay(sqlServer, connectionString);
        Assert.AreEqual(1, await db.ExecuteAsync(InsertVector, docx));
        Assert.AreEqual(0L, await ConstraintCountAsync(db, sqlServer, OldGuard));
        Assert.AreEqual(1L, await ConstraintCountAsync(db, sqlServer, Guard, true));
        var manifest = await SeedAsync(db); manifest["FormatKey"] = "knowledge-docx-manifest-v1";
        Assert.AreEqual(1, await db.ExecuteAsync(InsertManifest, manifest));
        // 新约束已安装、旧守卫尚未移除是合法中断点；先启用并验证新约束，再允许移除旧守卫。
        Assert.AreEqual(1, await db.ExecuteAsync($"DELETE FROM {Table} WHERE Id=@OperationId", docx));
        var oldCondition = "Dimensions IN (1536,3072) AND ((FormatKey='knowledge-vector-batch-v1' AND " + (sqlServer ? "DATALENGTH" : "OCTET_LENGTH") + "(FormatKey)=25) OR (FormatKey='knowledge-pdf-vector-batch-v1' AND " + (sqlServer ? "DATALENGTH" : "OCTET_LENGTH") + "(FormatKey)=29)) AND DistanceKey='cosine' AND " + (sqlServer ? "DATALENGTH" : "OCTET_LENGTH") + "(DistanceKey)=6 AND " + (sqlServer ? "DATALENGTH" : "OCTET_LENGTH") + "(Payload) BETWEEN 8 AND 524288";
        await db.ExecuteAsync(sqlServer
            ? $"ALTER TABLE dbo.{Table} WITH CHECK ADD CONSTRAINT {OldGuard} CHECK ({oldCondition}); ALTER TABLE dbo.{Table} NOCHECK CONSTRAINT {Guard}"
            : $"ALTER TABLE {Table} ADD CONSTRAINT {OldGuard} CHECK ({oldCondition}); ALTER TABLE {Table} ALTER CHECK {Guard} NOT ENFORCED");
        Replay(sqlServer, connectionString); Assert.AreEqual(0L, await ConstraintCountAsync(db, sqlServer, OldGuard));
        Assert.AreEqual(1L, await ConstraintCountAsync(db, sqlServer, Guard, true));
        Assert.AreEqual(1, await db.ExecuteAsync(InsertVector, docx));
        await db.ExecuteAsync(sqlServer ? $"ALTER TABLE dbo.{Table} DROP CONSTRAINT {Guard}" : $"ALTER TABLE {Table} DROP CHECK {Guard}");
        Replay(sqlServer, connectionString); Replay(sqlServer, connectionString);
        // 表已创建但说明尚未完成时仍可重放，既有原字节不得被重写。
        foreach (var (column, type) in new[] { ("Id", "binary(16)"), ("FormatKey", "varchar(32) CHARACTER SET ascii COLLATE ascii_bin"),
            ("PayloadHash", "char(64) CHARACTER SET ascii COLLATE ascii_bin"), ("Payload", "mediumblob"), ("CreatedAtUtc", "datetime(6)") })
        {
            if (sqlServer) await db.ExecuteAsync($"EXEC sys.sp_dropextendedproperty @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'{Manifest}',@level2type=N'COLUMN',@level2name=N'{column}'");
            else await db.ExecuteAsync($"ALTER TABLE {Manifest} MODIFY COLUMN {column} {type} NOT NULL COMMENT ''");
            Replay(sqlServer, connectionString);
            var comment = await db.QuerySingleAsync<string>(sqlServer
                ? $"SELECT CONVERT(nvarchar(4000),value) FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.{Manifest}') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.{Manifest}'),@Column,'ColumnId') AND name=N'MS_Description'"
                : $"SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='{Manifest}' AND COLUMN_NAME=@Column", new { Column = column });
            Assert.IsFalse(string.IsNullOrWhiteSpace(comment), column);
        }
        foreach (var p in new[] { text, pdf, docx })
        {
            CollectionAssert.AreEqual((byte[])p["Payload"]!, await db.QuerySingleAsync<byte[]>($"SELECT Payload FROM {Table} WHERE Id=@OperationId", p));
            Assert.AreEqual("queued", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
        }
        CollectionAssert.AreEqual((byte[])manifest["Payload"]!, await db.QuerySingleAsync<byte[]>($"SELECT Payload FROM {Manifest} WHERE Id=@TaskId", manifest));
        var bounds = await SeedAsync(db);
        foreach (var format in new[] { "unknown", "KNOWLEDGE-DOCX-VECTOR-BATCH-V1", "knowledge-docx-vector-batch-v1 " })
        { bounds["FormatKey"] = format; await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(InsertVector, bounds); }); }
        foreach (var format in new[] { "knowledge-text-manifest-v1", "KNOWLEDGE-DOCX-MANIFEST-V1", "knowledge-docx-manifest-v1 " })
        { bounds["FormatKey"] = format; await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(InsertManifest, bounds); }); }
        bounds["FormatKey"] = "knowledge-docx-manifest-v1";
        foreach (var length in new[] { 0, 7, 8388609 })
        { bounds["Payload"] = new byte[length]; await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(InsertManifest, bounds); }); }
    }

    private static async Task InvalidLegacyAsync(bool sqlServer)
    {
        var connectionString = await DatabaseAsync(sqlServer);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        // 模拟旧约束被运维禁用后出现的非法存量；扩展不能移除旧守卫或修复载荷。
        await db.ExecuteAsync(sqlServer ? $"ALTER TABLE dbo.{Table} NOCHECK CONSTRAINT {OldGuard}" : $"ALTER TABLE {Table} ALTER CHECK {OldGuard} NOT ENFORCED");
        var invalid = await SeedAsync(db); invalid["Dimensions"] = 0;
        Assert.AreEqual(1, await db.ExecuteAsync(InsertVector, invalid));
        var result = Upgrade(sqlServer, connectionString); Assert.IsFalse(result.Successful); Assert.IsNotNull(result.Error);
        Assert.AreEqual(1L, await ConstraintCountAsync(db, sqlServer, OldGuard));
        CollectionAssert.AreEqual((byte[])invalid["Payload"]!, await db.QuerySingleAsync<byte[]>($"SELECT Payload FROM {Table} WHERE Id=@OperationId", invalid));
    }

    private static Task<long> ConstraintCountAsync(DbConnection db, bool sqlServer, string name, bool enabled = false) => db.QuerySingleAsync<long>(sqlServer
        ? $"SELECT COUNT_BIG(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.{Table}') AND name=@Name" + (enabled ? " AND is_disabled=0 AND is_not_trusted=0" : "")
        : $"SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='{Table}' AND CONSTRAINT_NAME=@Name AND CONSTRAINT_TYPE='CHECK'" + (enabled ? " AND ENFORCED='YES'" : ""), new { Name = name });
    private static async Task<Dictionary<string, object?>> SeedAsync(DbConnection db)
    {
        var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db);
        Assert.AreEqual(1, await db.ExecuteAsync(AiKnowledgeIndexTaskSql.EnqueueHost.Text, p));
        p["OperationId"] = Guid.CreateVersion7(); p["Hash"] = new string('A', 64);
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_batch (Id,IndexTaskId,BatchOrdinal,RequestHash,InputCount,CreatedAtUtc) VALUES (@OperationId,@TaskId,0,@Hash,1,@Now)", p);
        p["Dimensions"] = 1536; p["FormatKey"] = "knowledge-vector-batch-v1"; p["Payload"] = new byte[32]; return p;
    }
    private static async Task<string> DatabaseAsync(bool sqlServer)
    {
        var connectionString = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        Migration249AiKnowledgeIndexVectorRecoveryTests.Replay(sqlServer, connectionString);
        Migration253AiKnowledgePdfVectorRecoveryTests.Replay(sqlServer, connectionString); return connectionString;
    }
    internal static void Replay(bool sqlServer, string connectionString)
    { var result = Upgrade(sqlServer, connectionString); Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(1, result.Scripts); }
    private static DatabaseUpgradeResult Upgrade(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        return builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
            name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && name.EndsWith("255_AiKnowledgeDocxPersistence.sql", StringComparison.Ordinal))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
    }
}
