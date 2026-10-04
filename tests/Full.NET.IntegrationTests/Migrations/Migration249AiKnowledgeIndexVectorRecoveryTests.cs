using System.Data.Common;
using System.Security.Cryptography;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>原始向量只能完整保留或拒绝，重放迁移不能重写已结算批次。</summary>
[TestClass]
public sealed class Migration249AiKnowledgeIndexVectorRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_unjournaled_vector_batch() => RecoveryAsync(true);
    [TestMethod] public Task MySql_recovers_unjournaled_vector_batch() => RecoveryAsync(false);

    private static async Task RecoveryAsync(bool sqlServer)
    {
        var connectionString = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        Replay(sqlServer, connectionString);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connectionString);
        var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db);
        Assert.AreEqual(1, await db.ExecuteAsync(AiKnowledgeIndexTaskSql.EnqueueHost.Text, p));
        var operation = Guid.CreateVersion7(); var vector = new float[1536]; vector[0] = 1;
        var batch = new AiKnowledgeIndexEmbeddingBatch(operation, 0, new(), "utf8-text-v1", AiKnowledgeCl100kTokenizer.Version,
            "structure-window-v1", new(), [new(new(0, 0, 9, "migration", "migration", 3, AiKnowledgeTextBlockKind.Paragraph, [], []), vector)]);
        var bytes = AiKnowledgeIndexVectorCodec.Encode(batch, 1536, default);
        p["OperationId"] = operation; p["Hash"] = new string('A', 64); p["PayloadHash"] = Convert.ToHexString(SHA256.HashData(bytes)); p["Payload"] = bytes;
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_batch (Id,IndexTaskId,BatchOrdinal,RequestHash,InputCount,CreatedAtUtc) VALUES (@OperationId,@TaskId,0,@Hash,1,@Now)", p);
        const string insert = "INSERT INTO fn_ai_knowledge_index_vector (Id,ProviderKey,ModelId,Dimensions,FormatKey,DistanceKey,PayloadHash,Payload,CreatedAtUtc) VALUES (@OperationId,'openai_compatible','text-embedding-3-small',1536,'knowledge-vector-batch-v1','cosine',@PayloadHash,@Payload,@Now)";
        Assert.AreEqual(1, await db.ExecuteAsync(insert, p));
        if (sqlServer)
            await db.ExecuteAsync("EXEC sys.sp_dropextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_knowledge_index_vector', @level2type=N'COLUMN', @level2name=N'Payload'");
        Replay(sqlServer, connectionString); Replay(sqlServer, connectionString);
        CollectionAssert.AreEqual(bytes, await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_index_vector WHERE Id=@OperationId", p));
        Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_index_vector WHERE Id=@OperationId", p));
        var comment = await db.ExecuteScalarAsync<string>(sqlServer
            ? "SELECT CAST(value AS nvarchar(4000)) FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_knowledge_index_vector'),N'Payload','ColumnId') AND name=N'MS_Description'"
            : "SELECT COLUMN_COMMENT FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_index_vector' AND COLUMN_NAME='Payload'");
        Assert.AreEqual("完整有界片段与小端向量载荷", comment);
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(insert, p); });
        p["OperationId"] = Guid.CreateVersion7();
        await Assert.ThrowsAsync<DbException>(async () => { await db.ExecuteAsync(insert, p); });
    }

    internal static void Replay(bool sqlServer, string connectionString)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connectionString) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        string[] scripts = ["184_AiModelConfig.sql", "203_PublishedModuleUuidStorage.sql", "240_AiKnowledgeBase.sql", "242_AiKnowledgeDocument.sql", "243_AiKnowledgeFileVersion.sql", "246_AiKnowledgeIndexTask.sql", "247_AiKnowledgeIndexBatch.sql", "248_AiKnowledgeIndexSession.sql", "249_AiKnowledgeIndexVector.sql"];
        var result = builder.WithPreprocessor(new PresetPublishedModuleCompatibilityPreprocessor(new HashSet<string>(scripts, StringComparer.Ordinal)))
            .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal) && scripts.Any(script => name.EndsWith(script, StringComparison.Ordinal)))
            .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(9, result.Scripts);
    }
}
