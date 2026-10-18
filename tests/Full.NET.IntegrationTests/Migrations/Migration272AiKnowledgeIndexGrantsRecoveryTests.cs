using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>双库精确重放新增迁移，恢复未记账的约束和索引；不运行后续迁移或创建收费授权。</summary>
[TestClass]
public sealed class Migration272AiKnowledgeIndexGrantsRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_unjournaled_grant_constraints_and_indexes() => VerifyAsync(true);
    [TestMethod] public Task MySql_recovers_unjournaled_grant_constraints_and_indexes() => VerifyAsync(false);
    private static async Task VerifyAsync(bool sqlServer)
    {
        var connection = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        using var factory = new FullNetApiFactory(sqlServer ? DatabaseProvider.SqlServer : DatabaseProvider.MySql, connection);
        await factory.InitializeAsync();
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connection);
        Replay();
        foreach (var item in new[]
        {
            (Table: "fn_ai_index_grant", Name: "CK_fn_ai_index_grant_State", Kind: "CHECK"),
            (Table: "fn_ai_index_grant_item", Name: "CK_fn_ai_index_grant_item_State", Kind: "CHECK"),
            (Table: "fn_ai_index_grant_item", Name: "FK_fn_ai_index_grant_item_GrantId", Kind: "FOREIGN KEY")
        })
        {
            await db.ExecuteAsync(sqlServer ? $"ALTER TABLE {item.Table} DROP CONSTRAINT {item.Name}" : $"ALTER TABLE {item.Table} DROP {item.Kind} {item.Name}");
            Replay();
            var exists = await db.QuerySingleAsync<int>(sqlServer
                ? "SELECT COUNT(*) FROM sys.objects WHERE parent_object_id=OBJECT_ID(@Table) AND name=@Name"
                : "SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME=@Table AND CONSTRAINT_NAME=@Name", new { item.Table, item.Name });
            Assert.AreEqual(1, exists);
        }
        const string index = "IX_fn_ai_index_grant_KnowledgeBaseId_OwnerUserId_Id";
        await db.ExecuteAsync(sqlServer ? $"DROP INDEX {index} ON fn_ai_index_grant" : $"ALTER TABLE fn_ai_index_grant DROP INDEX {index}");
        Replay();
        Assert.AreEqual(1, await db.QuerySingleAsync<int>(sqlServer
            ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('fn_ai_index_grant') AND name=@Name"
            : "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_index_grant' AND INDEX_NAME=@Name", new { Name = index }));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_index_grant"));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_index_grant_item"));
        Replay();

        void Replay()
        {
            var builder = sqlServer ? DeployChanges.To.SqlDatabase(connection)
                : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connection, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                    name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                        && name.EndsWith("272_AiKnowledgeIndexGrants.sql", StringComparison.Ordinal))
                .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(1, result.Scripts);
        }
    }
}
