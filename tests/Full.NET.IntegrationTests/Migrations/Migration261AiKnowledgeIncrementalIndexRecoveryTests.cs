using System.Data.Common;
using System.Text.Json;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>增量意图迁移可未记账重放；恢复索引和注释时保留既有来源及创建时间。</summary>
[TestClass]
public sealed class Migration261AiKnowledgeIncrementalIndexRecoveryTests
{
    [TestMethod]
    public Task SqlServer_recovers_incremental_intent_metadata_without_rewriting_rows() => VerifyAsync(true);
    [TestMethod]
    public Task MySql_replays_atomic_incremental_intent_creation_without_rewriting_rows() => VerifyAsync(false);

    private static async Task VerifyAsync(bool sqlServer)
    {
        var connection = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        Replay();
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connection);
        var values = new { Id = Guid.CreateVersion7(), DocumentId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(), Now = DateTime.UtcNow };
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_change (Id,DocumentId,VersionId,VersionNumber,CreatedAtUtc,UpdatedAtUtc) VALUES (@Id,@DocumentId,@VersionId,3,@Now,@Now)", values);
        var before = await ReadRow();
        if (sqlServer)
        {
            await db.ExecuteAsync("DROP INDEX UQ_fn_ai_knowledge_index_change_Document ON fn_ai_knowledge_index_change");
            await db.ExecuteAsync("EXEC sys.sp_dropextendedproperty @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_knowledge_index_change',@level2type=N'COLUMN',@level2name=N'VersionNumber'");
        }
        Replay(); Replay();
        Assert.AreEqual(before, await ReadRow());
        await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_change (Id,DocumentId,VersionId,VersionNumber,CreatedAtUtc,UpdatedAtUtc) VALUES (@Next,@DocumentId,@VersionId,4,@Now,@Now)", new { Next = Guid.CreateVersion7(), values.DocumentId, values.VersionId, values.Now }));
        await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync("UPDATE fn_ai_knowledge_index_change SET VersionNumber=0 WHERE Id=@Id", values));
        Assert.AreEqual(before, await ReadRow());
        var annotatedColumns = await db.QuerySingleAsync<int>(sqlServer
            ? "SELECT COUNT(*) FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_change') AND minor_id>0 AND name=N'MS_Description'"
            : "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_change' AND COLUMN_COMMENT<>''");
        Assert.AreEqual(6, annotatedColumns);

        async Task<string> ReadRow() => JsonSerializer.Serialize(await db.QuerySingleAsync("SELECT Id,DocumentId,VersionId,VersionNumber,CreatedAtUtc,UpdatedAtUtc FROM fn_ai_knowledge_index_change WHERE Id=@Id", values));
        void Replay()
        {
            var builder = sqlServer ? DeployChanges.To.SqlDatabase(connection) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connection, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && name.EndsWith("261_AiKnowledgeIncrementalIndex.sql", StringComparison.Ordinal))
                .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString());
            Assert.HasCount(1, result.Scripts);
        }
    }
}
