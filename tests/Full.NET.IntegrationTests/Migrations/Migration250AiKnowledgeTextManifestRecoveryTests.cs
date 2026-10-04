using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结迁移上界，仅增加清单表；未记账恢复保留原载荷和任务外键。</summary>
[TestClass]
public sealed class Migration250AiKnowledgeTextManifestRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_unjournaled_text_manifest() => RecoveryAsync(true);
    [TestMethod] public Task MySql_recovers_unjournaled_text_manifest() => RecoveryAsync(false);

    private static async Task RecoveryAsync(bool sqlServer)
    {
        var connectionString = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        Migration249AiKnowledgeIndexVectorRecoveryTests.Replay(sqlServer, connectionString);
        Replay(sqlServer, connectionString);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db);
        Assert.AreEqual(1, await db.ExecuteAsync(Full.NET.Modules.Ai.Persistence.AiKnowledgeIndexTaskSql.EnqueueHost.Text, p));
        // 恢复测试保存合成字节；真实清单编解码和消费由 Unit 与 Ai 双库路径分别验证。
        byte[] bytes = Enumerable.Range(0, 32).Select(n => (byte)n).ToArray();
        p["Payload"] = bytes; p["PayloadHash"] = Convert.ToHexString(SHA256.HashData(bytes));
        const string insert = "INSERT INTO fn_ai_knowledge_text_manifest (Id,FormatKey,PayloadHash,Payload,CreatedAtUtc) VALUES (@TaskId,'knowledge-text-manifest-v1',@PayloadHash,@Payload,@Now)";
        Assert.AreEqual(1, await db.ExecuteAsync(insert, p));
        if (sqlServer)
            await db.ExecuteAsync("EXEC sys.sp_dropextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_text_manifest', @level2type=N'COLUMN', @level2name=N'Payload'");
        Replay(sqlServer, connectionString); Replay(sqlServer, connectionString);
        CollectionAssert.AreEqual(bytes, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));
        Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId", p));
        var comment = await db.ExecuteScalarAsync<string>(sqlServer
            ? "SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_text_manifest'),N'Payload','ColumnId') AND name=N'MS_Description'"
            : "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_text_manifest' AND COLUMN_NAME='Payload'");
        Assert.AreEqual("不含正文的完整有界文本批次清单", comment);
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(insert, p); });
        p["TaskId"] = Guid.CreateVersion7();
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(insert, p); });
    }

    internal static void Replay(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                    && name.EndsWith("250_AiKnowledgeTextManifest.sql", StringComparison.Ordinal))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(1, result.Scripts);
    }
}
