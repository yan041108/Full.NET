using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>先扩展清单格式守卫再移除旧守卫；未记账恢复必须保留文本和 PDF 原行。</summary>
[TestClass]
public sealed class Migration252AiKnowledgePdfManifestRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_pdf_manifest_format_expansion() => RecoveryAsync(true);
    [TestMethod] public Task MySql_recovers_pdf_manifest_format_expansion() => RecoveryAsync(false);

    private static async Task RecoveryAsync(bool sqlServer)
    {
        var connectionString = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        Migration249AiKnowledgeIndexVectorRecoveryTests.Replay(sqlServer, connectionString);
        Migration250AiKnowledgeTextManifestRecoveryTests.Replay(sqlServer, connectionString);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        var text = await Seed(); var pdf = await Seed(); var bounded = await Seed();
        const string insert = "INSERT INTO fn_ai_knowledge_text_manifest (Id,FormatKey,PayloadHash,Payload,CreatedAtUtc) VALUES (@TaskId,@FormatKey,@PayloadHash,@Payload,@Now)";
        // 本层合成字节仅验证 DDL；真实 PDF 编解码、租约与授权由 Ai 双库用例验证。
        var bytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        foreach (var p in new[] { text, pdf, bounded }) { p["Payload"] = bytes; p["PayloadHash"] = Convert.ToHexString(SHA256.HashData(bytes)); }
        text["FormatKey"] = "knowledge-text-manifest-v1"; pdf["FormatKey"] = "knowledge-pdf-manifest-v1";
        Assert.AreEqual(1, await db.ExecuteAsync(insert, text));
        await db.ExecuteAsync(sqlServer
            ? "ALTER TABLE dbo.fn_ai_knowledge_text_manifest WITH CHECK ADD CONSTRAINT CK_fn_ai_knowledge_text_manifest_Formats CHECK (((FormatKey='knowledge-text-manifest-v1' AND DATALENGTH(FormatKey)=26) OR (FormatKey='knowledge-pdf-manifest-v1' AND DATALENGTH(FormatKey)=25)) AND DATALENGTH(Payload) BETWEEN 8 AND 131072)"
            : "ALTER TABLE fn_ai_knowledge_text_manifest ADD CONSTRAINT CK_fn_ai_knowledge_text_manifest_Formats CHECK (((FormatKey='knowledge-text-manifest-v1' AND OCTET_LENGTH(FormatKey)=26) OR (FormatKey='knowledge-pdf-manifest-v1' AND OCTET_LENGTH(FormatKey)=25)) AND OCTET_LENGTH(Payload) BETWEEN 8 AND 131072)");
        // 扩展约束已完成但旧约束未移除时，旧文本仍可用而 PDF 不能提前进入。
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(insert, pdf); });
        Replay(sqlServer, connectionString);
        Assert.AreEqual(1, await db.ExecuteAsync(insert, pdf));
        await db.ExecuteAsync(sqlServer
            ? "ALTER TABLE dbo.fn_ai_knowledge_text_manifest DROP CONSTRAINT CK_fn_ai_knowledge_text_manifest_Formats; EXEC sys.sp_dropextendedproperty @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_text_manifest',@level2type=N'COLUMN',@level2name=N'Payload'"
            : "ALTER TABLE fn_ai_knowledge_text_manifest DROP CHECK CK_fn_ai_knowledge_text_manifest_Formats; ALTER TABLE fn_ai_knowledge_text_manifest MODIFY COLUMN Payload mediumblob NOT NULL COMMENT ''");
        Replay(sqlServer, connectionString); Replay(sqlServer, connectionString);
        await db.ExecuteAsync(sqlServer
            ? "ALTER TABLE dbo.fn_ai_knowledge_text_manifest NOCHECK CONSTRAINT CK_fn_ai_knowledge_text_manifest_Formats"
            : "ALTER TABLE fn_ai_knowledge_text_manifest ALTER CHECK CK_fn_ai_knowledge_text_manifest_Formats NOT ENFORCED");
        Replay(sqlServer, connectionString);
        foreach (var p in new[] { text, pdf })
        {
            CollectionAssert.AreEqual(bytes, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));
            await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(insert, p); });
        }
        var comment = await db.ExecuteScalarAsync<string>(sqlServer
            ? "SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest'),N'Payload','ColumnId') AND name=N'MS_Description'"
            : "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_text_manifest' AND COLUMN_NAME='Payload'");
        Assert.AreEqual("不含正文的完整有界文本或PDF批次清单", comment);
        foreach (var format in new[] { "unknown", "knowledge-vector-batch-v1", "KNOWLEDGE-PDF-MANIFEST-V1", "knowledge-pdf-manifest-v1 ", "knowledge-text-manifest-v1 " })
        {
            bounded["FormatKey"] = format;
            await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(insert, bounded); });
        }
        bounded["FormatKey"] = "knowledge-pdf-manifest-v1";
        foreach (var length in new[] { 0, 7, 131073 })
        {
            bounded["Payload"] = new byte[length];
            await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(insert, bounded); });
        }
        foreach (var length in new[] { 8, 131072 })
        {
            bounded["Payload"] = new byte[length]; Assert.AreEqual(1, await db.ExecuteAsync(insert, bounded));
            Assert.AreEqual(1, await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", bounded));
        }
        bounded["TaskId"] = Guid.CreateVersion7(); bounded["Payload"] = bytes;
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(insert, bounded); });

        async Task<Dictionary<string, object?>> Seed()
        {
            var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db);
            Assert.AreEqual(1, await db.ExecuteAsync(AiKnowledgeIndexTaskSql.EnqueueHost.Text, p)); return p;
        }
    }

    internal static void Replay(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString)
            : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                    && name.EndsWith("252_AiKnowledgePdfManifest.sql", StringComparison.Ordinal))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(1, result.Scripts);
    }
}
