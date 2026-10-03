using System.Data.Common;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>仅重放 240/241，验证授权保留、后置索引恢复及本模块约束。</summary>
[TestClass]
public sealed class Migration241AiKnowledgeMemberRecoveryTests
{
    [TestMethod]
    public Task SqlServer_recovers_member_index_and_comment_without_losing_grants() => VerifyAsync(true);
    [TestMethod]
    public Task MySql_recovers_member_index_without_losing_grants() => VerifyAsync(false);

    private static async Task VerifyAsync(bool sqlServer)
    {
        var connectionString = sqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Replay(sqlServer, connectionString);
        await using DbConnection connection = sqlServer ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        var id = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        await connection.ExecuteAsync("""
            INSERT INTO fn_ai_knowledge_base (Id, TenantId, OwnerUserId, Name, Description, IsEnabled, DataClassification, CreatedAtUtc, Version)
            VALUES (@Id, NULL, @Owner, 'preserved', NULL, 1, 'internal', @CreatedAtUtc, 1)
            """, new { Id = id, Owner = Guid.CreateVersion7(), CreatedAtUtc = DateTime.UtcNow });
        var insert = "INSERT INTO fn_ai_knowledge_member (Id, KnowledgeBaseId, UserId, CreatedAtUtc) VALUES (@MemberId, @Id, @User, @CreatedAtUtc)";
        await connection.ExecuteAsync(insert, new { MemberId = Guid.CreateVersion7(), Id = id, User = user, CreatedAtUtc = DateTime.UtcNow });
        await connection.ExecuteAsync(sqlServer
            ? "DROP INDEX IX_fn_ai_knowledge_member_UserBase ON dbo.fn_ai_knowledge_member"
            : "DROP INDEX IX_fn_ai_knowledge_member_UserBase ON fn_ai_knowledge_member");
        if (sqlServer)
            await connection.ExecuteAsync("""
                EXEC sys.sp_dropextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=N'dbo',
                    @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_member', @level2type=N'COLUMN', @level2name=N'UserId'
                """);
        Replay(sqlServer, connectionString);
        Replay(sqlServer, connectionString);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(connection, "fn_ai_knowledge_member", "IX_fn_ai_knowledge_member_UserBase", sqlServer));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_member WHERE KnowledgeBaseId = @Id AND UserId = @User", new { Id = id, User = user }));
        var comment = await connection.ExecuteScalarAsync<string>(sqlServer ? """
            SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties
            WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_member')
                AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_member'), N'UserId', 'ColumnId') AND name = N'MS_Description'
            """ : """
            SELECT COLUMN_COMMENT FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_ai_knowledge_member' AND COLUMN_NAME = 'UserId'
            """);
        Assert.AreEqual("获授权用户标识", comment);
        // 授权重复和悬空知识库在双库均须被拒绝，用户标识不建立跨模块外键。
        foreach (var parent in new[] { id, Guid.CreateVersion7() })
        {
            var rejected = false;
            try { await connection.ExecuteAsync(insert, new { MemberId = Guid.CreateVersion7(), Id = parent, User = user, CreatedAtUtc = DateTime.UtcNow }); }
            catch (DbException) { rejected = true; }
            Assert.IsTrue(rejected);
        }
    }

    private static void Replay(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString)
            : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
                name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
                    && (name.EndsWith("240_AiKnowledgeBase.sql", StringComparison.Ordinal) || name.EndsWith("241_AiKnowledgeMember.sql", StringComparison.Ordinal)))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString());
        Assert.HasCount(2, result.Scripts);
    }
}
