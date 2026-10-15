using System.Data.Common;
using System.Reflection;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结索引任务迁移上界，并以独立连接验证真实竞争和旧租约隔离。</summary>
[TestClass]
public sealed class Migration246AiKnowledgeIndexTaskRecoveryTests
{
    [TestMethod]
    public Task SqlServer_recovers_unjournaled_index_task_structure() => RecoveryAsync(true);
    [TestMethod]
    public Task MySql_recovers_unjournaled_index_task_structure() => RecoveryAsync(false);
    [TestMethod]
    public Task SqlServer_fences_index_leases_and_authority_changes() => LeasesAsync(true);
    [TestMethod]
    public Task MySql_fences_index_leases_and_authority_changes() => LeasesAsync(false);

    [TestMethod]
    public Task SqlServer_rechecks_dispatch_source_model_and_lease() => DispatchAsync(true);
    [TestMethod]
    public Task MySql_rechecks_dispatch_source_model_and_lease() => DispatchAsync(false);

    private static async Task DispatchAsync(bool sqlServer)
    {
        // 本用例验证强类型 UTC 快照，需与生产执行器一样注册 Guid/UTC Handler，不能用裸 Dapper 的本地时区转换。
        _ = new ServiceCollection().AddFullNetDapper(new ConfigurationBuilder().Build(), Environments.Development);
        var connectionString = await DatabaseAsync(sqlServer);
        Replay(sqlServer, connectionString);
        ReplayGenerationCompatibility(sqlServer, connectionString);
        await using var first = Connection(sqlServer, connectionString);
        await using var second = Connection(sqlServer, connectionString);
        await first.OpenAsync(); await second.OpenAsync();
        foreach (var tenant in new[] { false, true })
        {
            var p = await SeedAsync(first, tenant);
            Assert.AreEqual(1, await first.ExecuteAsync(Sql("Enqueue", tenant), p));
            Assert.IsNull(await second.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Sql("Dispatch", tenant), p));
            Assert.AreEqual(1, await first.ExecuteAsync(Sql("Acquire", tenant), p));
            var healthy = await second.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Sql("Dispatch", tenant), p);
            Assert.IsNotNull(healthy);
            Assert.AreEqual(p["TaskId"], healthy.TaskId); Assert.AreEqual(p["DocumentId"], healthy.DocumentId);
            Assert.AreEqual(p["VersionId"], healthy.VersionId); Assert.AreEqual(p["FileId"], healthy.FileId);
            Assert.AreEqual(p["ModelConfigId"], healthy.ModelConfigId); Assert.AreEqual(1, healthy.ModelConfigVersion);
            Assert.AreEqual(p["DocumentVersion"], healthy.DocumentVersion); Assert.AreEqual(p["KnowledgeBaseVersion"], healthy.KnowledgeBaseVersion);
            Assert.AreEqual(p["LeaseToken"], healthy.LeaseToken); Assert.AreEqual(new DateTimeOffset((DateTime)p["LeaseExpiresAtUtc"]!, TimeSpan.Zero), healthy.LeaseExpiresAtUtc);
            Assert.AreEqual("abcdef", healthy.ContentHash); Assert.AreEqual("text/plain", healthy.ContentType); Assert.AreEqual(10L, healthy.SizeBytes);
            // 每个拒绝条件都从健康租约独立验证，避免前一个撤权掩盖漏掉的过滤。
            foreach (var key in new[] { "TaskId", "DocumentId", "KnowledgeBaseId", "VersionId", "OwnerUserId", "LeaseToken" })
                Assert.IsNull(await second.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Sql("Dispatch", tenant), Copy(p, key, Guid.CreateVersion7())), key);
            foreach (var key in new[] { "DocumentVersion", "KnowledgeBaseVersion" })
                Assert.IsNull(await second.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Sql("Dispatch", tenant), Copy(p, key, 2)), key);
            Assert.IsNull(await second.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Sql("Dispatch", !tenant), p));
            if (tenant)
                Assert.IsNull(await second.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Sql("Dispatch", true), Copy(p, "TenantId", Guid.CreateVersion7())));
            var expired = Copy(p, "Now", p["LeaseExpiresAtUtc"]);
            Assert.IsNull(await second.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Sql("Dispatch", tenant), expired));
            var replacement = Copy(expired, "LeaseToken", Guid.CreateVersion7());
            replacement["LeaseExpiresAtUtc"] = ((DateTime)expired["Now"]!).AddSeconds(30);
            Assert.AreEqual(1, await second.ExecuteAsync(Sql("Acquire", tenant), replacement));
            Assert.IsNull(await first.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Sql("Dispatch", tenant), expired));
            Assert.IsNotNull(await first.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Sql("Dispatch", tenant), replacement));
            Assert.AreEqual(1, await second.ExecuteAsync(Sql("Cancel", tenant), replacement));
            Assert.IsNull(await first.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Sql("Dispatch", tenant), replacement));

            foreach (var mutation in new[]
            {
                "UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@KnowledgeBaseId",
                "UPDATE fn_ai_knowledge_base SET Version=Version+1 WHERE Id=@KnowledgeBaseId",
                "UPDATE fn_ai_knowledge_base SET EmbeddingModelVersion=2 WHERE Id=@KnowledgeBaseId",
                "UPDATE fn_ai_knowledge_base SET EmbeddingModelConfigId=@LeaseToken WHERE Id=@KnowledgeBaseId",
                "UPDATE fn_ai_model_config SET IsEnabled=0 WHERE Id=@ModelConfigId",
                "UPDATE fn_ai_model_config SET Version=Version+1 WHERE Id=@ModelConfigId",
                "UPDATE fn_ai_model_config SET TenantId=@OtherTenantId WHERE Id=@ModelConfigId",
                "UPDATE fn_ai_knowledge_document SET Version=Version+1 WHERE Id=@DocumentId",
                "UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@DocumentId",
                "UPDATE fn_ai_knowledge_import SET StateKey='cleanup_pending' WHERE VersionId=@VersionId",
                "INSERT INTO fn_ai_knowledge_version (Id,DocumentId,FileId,VersionNumber,OriginalFileName,ContentType,ContentHash,SizeBytes,UploadedByUserId,CreatedAtUtc) VALUES (@LeaseToken,@DocumentId,@FileId,2,'new.txt','text/plain','newhash',10,@OwnerUserId,@Now)",
            })
            {
                var scenario = await SeedAsync(first, tenant);
                Assert.AreEqual(1, await first.ExecuteAsync(Sql("Enqueue", tenant), scenario));
                Assert.AreEqual(1, await first.ExecuteAsync(Sql("Acquire", tenant), scenario));
                Assert.IsNotNull(await second.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Sql("Dispatch", tenant), scenario), mutation);
                await first.ExecuteAsync(mutation, scenario);
                Assert.IsNull(await second.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Sql("Dispatch", tenant), scenario), mutation);
            }
        }
        var hostModel = await SeedAsync(first, tenant: true);
        await first.ExecuteAsync("UPDATE fn_ai_model_config SET TenantId=NULL WHERE Id=@ModelConfigId", hostModel);
        Assert.AreEqual(1, await first.ExecuteAsync(Sql("Enqueue", true), hostModel));
        Assert.AreEqual(1, await first.ExecuteAsync(Sql("Acquire", true), hostModel));
        Assert.IsNotNull(await second.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Sql("Dispatch", true), hostModel));
    }

    private static async Task RecoveryAsync(bool sqlServer)
    {
        var connectionString = await DatabaseAsync(sqlServer);
        Replay(sqlServer, connectionString);
        await using var connection = Connection(sqlServer, connectionString);
        var p = await SeedAsync(connection);
        Assert.AreEqual(1, await connection.ExecuteAsync(Sql("Enqueue", false), p));
        await connection.ExecuteAsync("DROP INDEX IX_fn_ai_knowledge_index_task_StateTime ON " + (sqlServer ? "dbo." : "") + "fn_ai_knowledge_index_task");
        if (sqlServer)
            await connection.ExecuteAsync("EXEC sys.sp_dropextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_task', @level2type=N'COLUMN', @level2name=N'LeaseToken'");
        Replay(sqlServer, connectionString);
        Replay(sqlServer, connectionString);
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(connection, "fn_ai_knowledge_index_task", "IX_fn_ai_knowledge_index_task_StateTime", sqlServer));
        var comment = await connection.ExecuteScalarAsync<string>(sqlServer
            ? "SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.fn_ai_knowledge_index_task') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_task'), N'LeaseToken', 'ColumnId') AND name = N'MS_Description'"
            : "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_task' AND COLUMN_NAME='LeaseToken'");
        Assert.AreEqual("当前执行者租约令牌", comment);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE Id=@TaskId AND AttemptCount=0 AND StateKey='queued'", p));
        var duplicateRejected = false;
        try { await connection.ExecuteAsync(Sql("Enqueue", false), Copy(p, "TaskId", Guid.CreateVersion7())); }
        catch (DbException) { duplicateRejected = true; }
        Assert.IsTrue(duplicateRejected);
    }

    private static async Task LeasesAsync(bool sqlServer)
    {
        var connectionString = await DatabaseAsync(sqlServer);
        Replay(sqlServer, connectionString);
        ReplayGenerationCompatibility(sqlServer, connectionString);
        await using var first = Connection(sqlServer, connectionString);
        await using var second = Connection(sqlServer, connectionString);
        await first.OpenAsync();
        await second.OpenAsync();
        var p = await SeedAsync(first);
        Assert.AreEqual(1, await first.ExecuteAsync(Sql("Enqueue", false), p));
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Acquire", false), Copy(p, "VersionId", Guid.CreateVersion7())));
        var loser = Copy(p, "LeaseToken", Guid.CreateVersion7());
        var claims = await Task.WhenAll(first.ExecuteAsync(Sql("Acquire", false), p), second.ExecuteAsync(Sql("Acquire", false), loser));
        Assert.AreEqual(1, claims.Sum());
        var old = claims[0] == 1 ? p : loser;
        var now = (DateTime)p["Now"]!;
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Acquire", false), Copy(p, "OwnerUserId", Guid.CreateVersion7())));
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Renew", true), Copy(Copy(old, "LeaseExpiresAtUtc", now.AddSeconds(31)), "TenantId", Guid.CreateVersion7())));
        var next = Copy(p, "LeaseToken", Guid.CreateVersion7());
        next["Now"] = now.AddSeconds(31);
        next["LeaseExpiresAtUtc"] = now.AddSeconds(61);
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Renew", false), Copy(Copy(old, "Now", now.AddSeconds(30)), "LeaseExpiresAtUtc", now.AddSeconds(60))));
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Fail", false), Copy(old, "Now", now.AddSeconds(30))));
        Assert.AreEqual(1, await second.ExecuteAsync(Sql("Acquire", false), next));
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Renew", false), Copy(Copy(old, "Now", now.AddSeconds(32)), "LeaseExpiresAtUtc", now.AddSeconds(92))));
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Fail", false), Copy(old, "Now", next["Now"])));
        Assert.AreEqual(1, await second.ExecuteAsync(Sql("Renew", false), Copy(Copy(next, "Now", now.AddSeconds(32)), "LeaseExpiresAtUtc", now.AddSeconds(62))));
        Assert.AreEqual(1, await second.ExecuteAsync(Sql("Fail", false), next));
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Acquire", false), next));
        Assert.AreEqual(1, await first.ExecuteAsync(Sql("Retry", false), next));
        var staleToken = next["LeaseToken"];
        next["LeaseToken"] = Guid.CreateVersion7();
        Assert.AreEqual(1, await first.ExecuteAsync(Sql("Acquire", false), next));
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Fail", false), Copy(next, "LeaseToken", staleToken)));
        for (var attempt = 2; attempt <= 3; attempt++)
        {
            next["Now"] = now.AddSeconds(31 * attempt);
            next["LeaseExpiresAtUtc"] = ((DateTime)next["Now"]!).AddSeconds(30);
            next["LeaseToken"] = Guid.CreateVersion7();
            Assert.AreEqual(1, await second.ExecuteAsync(Sql("Acquire", false), next));
        }
        next["Now"] = now.AddSeconds(125);
        next["LeaseExpiresAtUtc"] = now.AddSeconds(155);
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Acquire", false), next));
        Assert.AreEqual(1, await first.ExecuteAsync(Sql("Retry", false), next));
        Assert.AreEqual(1, await first.ExecuteAsync(Sql("Acquire", false), next));
        next["Now"] = now.AddSeconds(126);
        next["LeaseExpiresAtUtc"] = now.AddSeconds(156);
        // 删除后续租/失败不得写回；取消仍可清除租约，之后任何代次都不能复活。
        await first.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1, Version=Version+1 WHERE Id=@DocumentId", p);
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Renew", false), next));
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Fail", false), next));
        Assert.AreEqual(1, await second.ExecuteAsync(Sql("Cancel", false), next));
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Acquire", false), next));
        Assert.AreEqual(0, await first.ExecuteAsync(Sql("Retry", false), next));
        Assert.AreEqual("cancelled", await first.ExecuteScalarAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", p));

        // 独立数据验证范围、批准撤销、模型变更和最新来源，不依赖前一条任务的取消状态。
        foreach (var mutation in new[]
        {
            "UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@KnowledgeBaseId",
            "UPDATE fn_ai_knowledge_base SET Version=Version+1 WHERE Id=@KnowledgeBaseId",
            "UPDATE fn_ai_knowledge_base SET EmbeddingModelVersion=2 WHERE Id=@KnowledgeBaseId",
            "UPDATE fn_ai_knowledge_base SET EmbeddingModelConfigId=@LeaseToken WHERE Id=@KnowledgeBaseId",
            "UPDATE fn_ai_model_config SET IsEnabled=0 WHERE Id=@ModelConfigId",
            "UPDATE fn_ai_model_config SET Version=Version+1 WHERE Id=@ModelConfigId",
            "UPDATE fn_ai_model_config SET TenantId=@OtherTenantId WHERE Id=@ModelConfigId",
            "UPDATE fn_ai_knowledge_document SET Version=Version+1 WHERE Id=@DocumentId",
            "UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@DocumentId",
            "UPDATE fn_ai_knowledge_import SET StateKey='cleanup_pending' WHERE VersionId=@VersionId",
            "INSERT INTO fn_ai_knowledge_version (Id,DocumentId,FileId,VersionNumber,OriginalFileName,ContentType,ContentHash,SizeBytes,UploadedByUserId,CreatedAtUtc) VALUES (@LeaseToken,@DocumentId,@FileId,2,'new.txt','text/plain','newhash',10,@OwnerUserId,@Now)",
        })
        {
          foreach (var phase in new[] { "queued", "running", "failed" })
          {
            var scenario = await SeedAsync(first, tenant: true);
            Assert.AreEqual(0, await first.ExecuteAsync(Sql("Enqueue", false), scenario));
            Assert.AreEqual(0, await first.ExecuteAsync(Sql("Enqueue", true), Copy(scenario, "TenantId", Guid.CreateVersion7())));
            Assert.AreEqual(1, await first.ExecuteAsync(Sql("Enqueue", true), scenario));
            Assert.AreEqual(0, await first.ExecuteAsync(Sql("Acquire", false), scenario));
            Assert.AreEqual(0, await first.ExecuteAsync(Sql("Acquire", true), Copy(scenario, "TenantId", Guid.CreateVersion7())));
            if (phase != "queued")
            {
                Assert.AreEqual(1, await first.ExecuteAsync(Sql("Acquire", true), scenario));
                scenario["Now"] = ((DateTime)scenario["Now"]!).AddSeconds(1);
                scenario["LeaseExpiresAtUtc"] = ((DateTime)scenario["Now"]!).AddSeconds(30);
                // 健康对照先证明同一组有效参数能够续租，避免其他前置条件掩盖撤权。
                Assert.AreEqual(1, await first.ExecuteAsync(Sql("Renew", true), scenario));
                if (phase == "failed") Assert.AreEqual(1, await first.ExecuteAsync(Sql("Fail", true), scenario));
            }
            scenario["Now"] = ((DateTime)scenario["Now"]!).AddSeconds(1);
            scenario["LeaseExpiresAtUtc"] = ((DateTime)scenario["Now"]!).AddSeconds(30);
            await first.ExecuteAsync(mutation, scenario);
            if (phase == "queued") Assert.AreEqual(0, await second.ExecuteAsync(Sql("Acquire", true), scenario), mutation);
            else if (phase == "failed") Assert.AreEqual(0, await second.ExecuteAsync(Sql("Retry", true), scenario), mutation);
            else
            {
                Assert.AreEqual(0, await second.ExecuteAsync(Sql("Renew", true), scenario), mutation);
                Assert.AreEqual(0, await second.ExecuteAsync(Sql("Fail", true), scenario), mutation);
            }
            Assert.AreEqual(0, await second.ExecuteAsync(Sql("Enqueue", true), Copy(scenario, "TaskId", Guid.CreateVersion7())), mutation);
            Assert.AreEqual(0, await second.ExecuteAsync(Sql("Cancel", true), Copy(scenario, "OwnerUserId", Guid.CreateVersion7())));
            Assert.AreEqual(0, await second.ExecuteAsync(Sql("Cancel", true), Copy(scenario, "TenantId", Guid.CreateVersion7())));
            Assert.AreEqual(1, await second.ExecuteAsync(Sql("Cancel", true), scenario), mutation);
            Assert.AreEqual(0, await second.ExecuteAsync(Sql("Cancel", true), scenario), mutation);
          }
        }
        var hostModel = await SeedAsync(first, tenant: true);
        await first.ExecuteAsync("UPDATE fn_ai_model_config SET TenantId=NULL WHERE Id=@ModelConfigId", hostModel);
        Assert.AreEqual(1, await first.ExecuteAsync(Sql("Enqueue", true), hostModel));
        Assert.AreEqual(1, await first.ExecuteAsync(Sql("Acquire", true), hostModel));
    }

    private static Dictionary<string, object?> Copy(Dictionary<string, object?> p, string key, object? value)
    {
        var copy = new Dictionary<string, object?>(p, StringComparer.Ordinal) { [key] = value };
        return copy;
    }

    internal static async Task<Dictionary<string, object?>> SeedAsync(DbConnection connection, bool tenant = false)
    {
        var now = DateTime.UtcNow;
        // 夹具统一到整秒，避免 SQL Server 的 DateTime 参数舍入掩盖精确到期断言；生产封装使用 DateTimeOffset。
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        var p = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["KnowledgeBaseId"] = Guid.CreateVersion7(), ["DocumentId"] = Guid.CreateVersion7(),
            ["OwnerUserId"] = Guid.CreateVersion7(), ["VersionId"] = Guid.CreateVersion7(),
            ["FileId"] = Guid.CreateVersion7(), ["ModelConfigId"] = Guid.CreateVersion7(),
            ["TaskId"] = Guid.CreateVersion7(), ["LeaseToken"] = Guid.CreateVersion7(),
            ["TenantId"] = tenant ? Guid.CreateVersion7() : null,
            ["OtherTenantId"] = Guid.CreateVersion7(),
            ["KnowledgeBaseVersion"] = 1, ["DocumentVersion"] = 1,
            ["Now"] = now, ["LeaseExpiresAtUtc"] = now.AddSeconds(30),
            ["FailureCode"] = "ai.knowledge.index_unavailable",
        };
        await connection.ExecuteAsync("INSERT INTO fn_ai_model_config (Id,TenantId,Name,ProviderKey,EndpointBaseUrl,ModelId,ApiKeyProtected,IsDefault,IsEnabled,CreatedAtUtc,Version) VALUES (@ModelConfigId,@TenantId,'model','openai_compatible','https://api.openai.com/v1','text-embedding-3-small','protected',0,1,@Now,1)", p);
        await connection.ExecuteAsync("INSERT INTO fn_ai_knowledge_base (Id,TenantId,OwnerUserId,Name,IsEnabled,DataClassification,EmbeddingModelConfigId,EmbeddingModelVersion,CreatedAtUtc,Version) VALUES (@KnowledgeBaseId,@TenantId,@OwnerUserId,'knowledge',1,'internal',@ModelConfigId,1,@Now,1)", p);
        await connection.ExecuteAsync("INSERT INTO fn_ai_knowledge_document (Id,KnowledgeBaseId,Title,IsDeleted,CreatedAtUtc,Version) VALUES (@DocumentId,@KnowledgeBaseId,'document',0,@Now,1)", p);
        await connection.ExecuteAsync("INSERT INTO fn_ai_knowledge_version (Id,DocumentId,FileId,VersionNumber,OriginalFileName,ContentType,ContentHash,SizeBytes,UploadedByUserId,CreatedAtUtc) VALUES (@VersionId,@DocumentId,@FileId,1,'a.txt','text/plain','abcdef',10,@OwnerUserId,@Now)", p);
        await connection.ExecuteAsync("INSERT INTO fn_ai_knowledge_import (Id,DocumentId,VersionId,RequestKey,ExpectedDocumentVersion,OriginalFileName,ContentType,ContentHash,SizeBytes,ActorUserId,FileId,StateKey,CreatedAtUtc,UpdatedAtUtc) VALUES (@TaskId,@DocumentId,@VersionId,@TaskId,1,'a.txt','text/plain','abcdef',10,@OwnerUserId,@FileId,'completed',@Now,@Now)", p);
        return p;
    }

    private static string Sql(string operation, bool tenant) => ((SqlStatement)typeof(AiModule).Assembly
        .GetType("Full.NET.Modules.Ai.Persistence.AiKnowledgeIndexTaskSql", true)!
        .GetField(operation + (tenant ? "Tenant" : "Host"), BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Text;
    internal static Task<string> DatabaseAsync(bool sqlServer) => sqlServer ? SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : SharedDatabaseFixture.CreateMySqlDatabaseAsync();
    internal static DbConnection Connection(bool sqlServer, string connectionString) => sqlServer ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);

    // 当前租约 SQL 识别多代次；恢复测试仍冻结 246 上界，执行行为场景显式追加兼容列。
    internal static void ReplayGenerationCompatibility(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && name.EndsWith("260_AiKnowledgeIndexRebuild.sql", StringComparison.Ordinal))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(1, result.Scripts);
    }

    private static void Replay(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        string[] scripts = ["184_AiModelConfig.sql", "203_PublishedModuleUuidStorage.sql", "240_AiKnowledgeBase.sql", "242_AiKnowledgeDocument.sql", "243_AiKnowledgeFileVersion.sql", "246_AiKnowledgeIndexTask.sql"];
        // 184 的历史字符串 UUID 必须沿正式 203 前向迁移转换；固定预设只保留所选 AI 表。
        var result = builder.WithPreprocessor(new PresetPublishedModuleCompatibilityPreprocessor(new HashSet<string>(scripts, StringComparer.Ordinal)))
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && scripts.Any(script => name.EndsWith(script, StringComparison.Ordinal)))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString());
        Assert.HasCount(6, result.Scripts);
    }
}
