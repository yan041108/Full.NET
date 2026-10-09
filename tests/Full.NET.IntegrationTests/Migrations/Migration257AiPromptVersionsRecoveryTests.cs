using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.MySql;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>双库未记账重放恢复作用域、发布指针及注释，不改写正文和撤销代次。</summary>
[TestClass]
public sealed class Migration257AiPromptVersionsRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_prompt_guards_and_preserves_versions() => VerifyAsync(true);
    [TestMethod] public Task MySql_recovers_prompt_guards_and_preserves_versions() => VerifyAsync(false);
    private static async Task VerifyAsync(bool sqlServer)
    {
        var connectionString = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        Replay();
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        var p = new { Id = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(), UserId = Guid.CreateVersion7(), ClientId = Guid.CreateVersion7(), Hash = new string('a', 64), Now = DateTimeOffset.UtcNow };
        await db.ExecuteAsync("INSERT INTO fn_ai_prompt (Id,ScopeKey,TenantId,OwnerUserId,ClientRequestId,CreateHash,Name,Revision,LastVersion,ActiveVersionId,Epoch,CreatedAtUtc) VALUES (@Id,'host',NULL,@UserId,@ClientId,@Hash,'immutable',2,1,NULL,3,@Now)", p);
        await db.ExecuteAsync("INSERT INTO fn_ai_prompt_version (Id,PromptId,ScopeKey,TenantId,Version,FormatVersion,BodyJson,ModelConfigVersion,ProviderKey,ModelId,BodyHash,WasPublished,CreatedByUserId,CreatedAtUtc) VALUES (@VersionId,@Id,'host',NULL,1,1,'{}',1,'ollama','model',@Hash,1,@UserId,@Now)", p);
        await db.ExecuteAsync("UPDATE fn_ai_prompt SET ActiveVersionId=@VersionId WHERE Id=@Id", p);
        foreach (var (kind, name, table) in new[]
        {
            ("CHECK", "CK_fn_ai_prompt_State", "fn_ai_prompt"),
            ("CHECK", "CK_fn_ai_prompt_version_State", "fn_ai_prompt_version"),
            ("FOREIGN KEY", "FK_fn_ai_prompt_version_ScopeKey_PromptId", "fn_ai_prompt_version"),
            ("FOREIGN KEY", "FK_fn_ai_prompt_ScopeKey_Id_ActiveVersionId", "fn_ai_prompt"),
        })
        {
            await db.ExecuteAsync(sqlServer ? $"ALTER TABLE {table} DROP CONSTRAINT {name}" : $"ALTER TABLE {table} DROP {kind} {name}");
            Replay();
            Assert.AreEqual(3, await db.QuerySingleAsync<int>("SELECT Epoch FROM fn_ai_prompt WHERE Id=@Id", p));
            Assert.AreEqual("{}", await db.QuerySingleAsync<string>("SELECT BodyJson FROM fn_ai_prompt_version WHERE Id=@VersionId", p));
            Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT WasPublished FROM fn_ai_prompt_version WHERE Id=@VersionId", p));
        }
        const string retryIndex = "UX_fn_ai_prompt_ScopeKey_OwnerUserId_ClientRequestId";
        await db.ExecuteAsync(sqlServer ? $"DROP INDEX {retryIndex} ON fn_ai_prompt" : $"ALTER TABLE fn_ai_prompt DROP INDEX {retryIndex}"); Replay();
        await db.ExecuteAsync(sqlServer
            ? "EXEC sys.sp_dropextendedproperty @name=N'MS_Description',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_prompt',@level2type=N'COLUMN',@level2name=N'Epoch'"
            : "ALTER TABLE fn_ai_prompt MODIFY COLUMN Epoch int NOT NULL COMMENT ''");
        Replay();
        Assert.AreEqual("停用撤销代次；重新发布不会恢复旧代次", await db.QuerySingleAsync<string>(sqlServer
            ? "SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_ai_prompt') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_ai_prompt'),'Epoch','ColumnId') AND name='MS_Description'"
            : "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_prompt' AND COLUMN_NAME='Epoch'"));
        foreach (var set in new[] { "Revision=0", "LastVersion=3", "Epoch=-1", "ScopeKey='other'", "CreateHash='wrong'" })
            await Assert.ThrowsAsync<Exception>(async () => await db.ExecuteAsync("UPDATE fn_ai_prompt SET " + set + " WHERE Id=@Id", p));
        foreach (var set in new[] { "Version=0", "FormatVersion=2", "ModelConfigVersion=0", "BodyHash='wrong'", "BodyJson='not-json'", "ScopeKey='other'", "PromptId=@VersionId" })
            await Assert.ThrowsAsync<Exception>(async () => await db.ExecuteAsync("UPDATE fn_ai_prompt_version SET " + set + " WHERE Id=@VersionId", p));
        await Assert.ThrowsAsync<Exception>(async () => await db.ExecuteAsync("UPDATE fn_ai_prompt SET ActiveVersionId=@Id WHERE Id=@Id", p));
        // 应用降级保留发布历史；迁移重放不删除任何已发布数据。
        Replay(); Assert.AreEqual(p.VersionId, await db.QuerySingleAsync<Guid>("SELECT ActiveVersionId FROM fn_ai_prompt WHERE Id=@Id", p));
        void Replay()
        {
            var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && name.EndsWith("257_AiPromptVersions.sql", StringComparison.Ordinal))
                .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(1, result.Scripts);
        }
    }
}
