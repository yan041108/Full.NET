using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using DbUp;
using DbUp.Engine;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>扩展向量格式前先校验新守卫；重放保留原批次，非法历史行必须显式修复。</summary>
[TestClass]
public sealed class Migration253AiKnowledgePdfVectorRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_pdf_vector_format_expansion() => RecoveryAsync(true);
    [TestMethod] public Task MySql_recovers_pdf_vector_format_expansion() => RecoveryAsync(false);
    [TestMethod] public Task SqlServer_retains_old_guard_when_legacy_rows_are_invalid() => InvalidLegacyAsync(true);
    [TestMethod] public Task MySql_retains_old_guard_when_legacy_rows_are_invalid() => InvalidLegacyAsync(false);

    private const string Insert = "INSERT INTO fn_ai_knowledge_index_vector (Id,ProviderKey,ModelId,Dimensions,FormatKey,DistanceKey,PayloadHash,Payload,CreatedAtUtc) VALUES (@OperationId,'openai_compatible','text-embedding-3-small',@Dimensions,@FormatKey,@DistanceKey,@PayloadHash,@Payload,@Now)";
    private const string Table = "fn_ai_knowledge_index_vector";
    private const string Guard = "CK_fn_ai_knowledge_index_vector_Formats";
    private const string Bounds = "CK_fn_ai_knowledge_index_vector_Bounds";
    private const string TableComment = "知识库已结算文本或PDF向量批次派生数据";
    private const string PayloadComment = "完整有界文本或PDF分页片段与小端向量载荷";

    private static async Task RecoveryAsync(bool sqlServer)
    {
        var connectionString = await DatabaseAsync(sqlServer);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        var text = await SeedAsync(db); var pdf = await SeedAsync(db); var bounded = await SeedAsync(db);
        text["FormatKey"] = "knowledge-vector-batch-v1"; pdf["FormatKey"] = "knowledge-pdf-vector-batch-v1";
        Assert.AreEqual(1, await db.ExecuteAsync(Insert, text));
        // 合成字节仅证明 DDL；本切片不声明 PDF 运行时持久化、模型结算或 ready 已接入。
        await db.ExecuteAsync(sqlServer
            ? $"ALTER TABLE dbo.{Table} WITH CHECK ADD CONSTRAINT {Guard} CHECK ({Condition(true)})"
            : $"ALTER TABLE {Table} ADD CONSTRAINT {Guard} CHECK ({Condition(false)})");
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(Insert, pdf); });
        Replay(sqlServer, connectionString);
        Assert.AreEqual(1, await db.ExecuteAsync(Insert, pdf));
        Assert.AreEqual(0L, await ConstraintCountAsync(db, sqlServer, Bounds));
        Assert.AreEqual(1L, await ConstraintCountAsync(db, sqlServer, Guard, enabled: true));

        // MySQL 每个 DDL 都可先提交；逐个模拟已扩展但后续注释未完成的中断点。
        await db.ExecuteAsync(sqlServer
            ? $"EXEC sys.sp_dropextendedproperty @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'{Table}'"
            : $"ALTER TABLE {Table} COMMENT=''");
        Replay(sqlServer, connectionString);
        await db.ExecuteAsync(sqlServer
            ? $"EXEC sys.sp_dropextendedproperty @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'{Table}',@level2type=N'COLUMN',@level2name=N'Payload'"
            : $"ALTER TABLE {Table} MODIFY COLUMN Payload mediumblob NOT NULL COMMENT ''");
        Replay(sqlServer, connectionString);
        await db.ExecuteAsync(sqlServer ? $"ALTER TABLE dbo.{Table} DROP CONSTRAINT {Guard}" : $"ALTER TABLE {Table} DROP CHECK {Guard}");
        Replay(sqlServer, connectionString); Replay(sqlServer, connectionString);
        await db.ExecuteAsync(sqlServer ? $"ALTER TABLE dbo.{Table} NOCHECK CONSTRAINT {Guard}" : $"ALTER TABLE {Table} ALTER CHECK {Guard} NOT ENFORCED");
        Replay(sqlServer, connectionString);
        Assert.AreEqual(1L, await ConstraintCountAsync(db, sqlServer, Guard, enabled: true));

        foreach (var p in new[] { text, pdf })
        {
            CollectionAssert.AreEqual((byte[])p["Payload"]!, await db.QuerySingleAsync<byte[]>($"SELECT Payload FROM {Table} WHERE Id=@OperationId", p));
            Assert.AreEqual(p["PayloadHash"], await db.QuerySingleAsync<string>($"SELECT PayloadHash FROM {Table} WHERE Id=@OperationId", p));
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>($"SELECT COUNT(*) FROM {Table} WHERE Id=@OperationId", p));
            Assert.AreEqual("queued", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));
            await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(Insert, p); });
        }
        Assert.AreEqual(TableComment, await CommentAsync(db, sqlServer, column: false));
        Assert.AreEqual(PayloadComment, await CommentAsync(db, sqlServer, column: true));
        foreach (var format in new[] { "unknown", "KNOWLEDGE-PDF-VECTOR-BATCH-V1", "knowledge-pdf-vector-batch-v1 ", "knowledge-vector-batch-v1 ", "knowledge-pdf-manifest-v1" })
        {
            bounded["FormatKey"] = format;
            await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(Insert, bounded); });
        }
        foreach (var format in new[] { "knowledge-vector-batch-v1", "knowledge-pdf-vector-batch-v1" })
        {
            bounded["FormatKey"] = format;
            foreach (var distance in new[] { "COSINE", "cosine ", "euclidean" })
            {
                bounded["DistanceKey"] = distance;
                await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(Insert, bounded); });
            }
            bounded["DistanceKey"] = "cosine";
            foreach (var dimensions in new[] { 0, 1535, 1537, 3073 })
            {
                bounded["Dimensions"] = dimensions;
                await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(Insert, bounded); });
            }
            bounded["Dimensions"] = 1536;
            foreach (var length in new[] { 0, 7, 524289 })
            {
                bounded["Payload"] = new byte[length];
                await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(Insert, bounded); });
            }
            foreach (var dimensions in new[] { 1536, 3072 })
            foreach (var length in new[] { 8, 524288 })
            {
                bounded["Dimensions"] = dimensions; bounded["Payload"] = new byte[length];
                Assert.AreEqual(1, await db.ExecuteAsync(Insert, bounded));
                Assert.AreEqual(1, await db.ExecuteAsync($"DELETE FROM {Table} WHERE Id=@OperationId", bounded));
            }
        }
        bounded["OperationId"] = Guid.CreateVersion7(); bounded["Payload"] = new byte[8];
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(Insert, bounded); });
    }

    private static async Task InvalidLegacyAsync(bool sqlServer)
    {
        var connectionString = await DatabaseAsync(sqlServer);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        var valid = await SeedAsync(db); Assert.AreEqual(1, await db.ExecuteAsync(Insert, valid));
        // 两库旧二进制排序规则仍忽略尾空格；新约束不得静默接受或重写这些旧行。
        foreach (var fault in new[] { "FormatKey", "DistanceKey" })
        {
            // 第二轮保留一个禁用的新守卫，验证启用失败也不能移除旧守卫。
            if (fault == "DistanceKey")
                await db.ExecuteAsync(sqlServer
                    ? $"ALTER TABLE dbo.{Table} WITH NOCHECK ADD CONSTRAINT {Guard} CHECK ({Condition(true)}); ALTER TABLE dbo.{Table} NOCHECK CONSTRAINT {Guard}"
                    : $"ALTER TABLE {Table} ADD CONSTRAINT {Guard} CHECK ({Condition(false)}) NOT ENFORCED");
            var invalid = await SeedAsync(db); invalid[fault] = (string)invalid[fault]! + " ";
            Assert.AreEqual(1, await db.ExecuteAsync(Insert, invalid));
            var result = Upgrade(sqlServer, connectionString);
            Assert.IsFalse(result.Successful); Assert.IsNotNull(result.Error);
            Assert.AreEqual(1L, await ConstraintCountAsync(db, sqlServer, Bounds, enabled: true));
            CollectionAssert.AreEqual((byte[])invalid["Payload"]!, await db.QuerySingleAsync<byte[]>($"SELECT Payload FROM {Table} WHERE Id=@OperationId", invalid));
            var pdf = await SeedAsync(db); pdf["FormatKey"] = "knowledge-pdf-vector-batch-v1";
            await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(Insert, pdf); });
            // 仅独立测试库显式删除非法样本；生产脚本没有 DELETE、UPDATE 或自动修复。
            Assert.AreEqual(1, await db.ExecuteAsync($"DELETE FROM {Table} WHERE Id=@OperationId", invalid));
        }
        Replay(sqlServer, connectionString);
        CollectionAssert.AreEqual((byte[])valid["Payload"]!, await db.QuerySingleAsync<byte[]>($"SELECT Payload FROM {Table} WHERE Id=@OperationId", valid));
    }

    private static string Condition(bool sqlServer)
    {
        var length = sqlServer ? "DATALENGTH" : "OCTET_LENGTH";
        return $"Dimensions IN (1536,3072) AND ((FormatKey='knowledge-vector-batch-v1' AND {length}(FormatKey)=25) OR (FormatKey='knowledge-pdf-vector-batch-v1' AND {length}(FormatKey)=29)) AND DistanceKey='cosine' AND {length}(DistanceKey)=6 AND {length}(Payload) BETWEEN 8 AND 524288";
    }
    private static Task<long> ConstraintCountAsync(DbConnection db, bool sqlServer, string name, bool enabled = false) =>
        db.QuerySingleAsync<long>(sqlServer
            ? $"SELECT COUNT_BIG(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.{Table}') AND name=@Name" + (enabled ? " AND is_disabled=0 AND is_not_trusted=0" : "")
            : $"SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='{Table}' AND CONSTRAINT_NAME=@Name AND CONSTRAINT_TYPE='CHECK'" + (enabled ? " AND ENFORCED='YES'" : ""), new { Name = name });
    private static Task<string?> CommentAsync(DbConnection db, bool sqlServer, bool column) => db.ExecuteScalarAsync<string>(sqlServer
        ? $"SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.{Table}') AND minor_id=" + (column ? $"COLUMNPROPERTY(OBJECT_ID(N'dbo.{Table}'),N'Payload','ColumnId')" : "0") + " AND name=N'MS_Description'"
        : column ? $"SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='{Table}' AND COLUMN_NAME='Payload'"
        : $"SELECT TABLE_COMMENT FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='{Table}'");
    private static async Task<Dictionary<string, object?>> SeedAsync(DbConnection db)
    {
        var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db);
        Assert.AreEqual(1, await db.ExecuteAsync(AiKnowledgeIndexTaskSql.EnqueueHost.Text, p));
        p["OperationId"] = Guid.CreateVersion7(); p["Hash"] = new string('A', 64);
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_batch (Id,IndexTaskId,BatchOrdinal,RequestHash,InputCount,CreatedAtUtc) VALUES (@OperationId,@TaskId,0,@Hash,1,@Now)", p);
        p["Dimensions"] = 1536; p["FormatKey"] = "knowledge-vector-batch-v1"; p["DistanceKey"] = "cosine";
        p["Payload"] = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        p["PayloadHash"] = Convert.ToHexString(SHA256.HashData((byte[])p["Payload"]!)); return p;
    }
    private static async Task<string> DatabaseAsync(bool sqlServer)
    {
        var connectionString = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        Migration249AiKnowledgeIndexVectorRecoveryTests.Replay(sqlServer, connectionString); return connectionString;
    }
    internal static void Replay(bool sqlServer, string connectionString)
    {
        var result = Upgrade(sqlServer, connectionString);
        Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(1, result.Scripts);
    }
    private static DatabaseUpgradeResult Upgrade(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString)
            : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        return builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                    && name.EndsWith("253_AiKnowledgePdfVector.sql", StringComparison.Ordinal))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
    }
}
