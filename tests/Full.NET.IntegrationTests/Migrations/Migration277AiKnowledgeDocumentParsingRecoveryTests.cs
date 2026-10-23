using System.Data.Common;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Data.MySql;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>统一解析迁移必须可未记账重放，并保留历史清单、向量及来源策略。</summary>
[TestClass]
public sealed class Migration277AiKnowledgeDocumentParsingRecoveryTests
{
    [TestMethod] public Task SqlServer_recovers_renamed_manifest_policy_and_vector_bounds() => RecoverAsync(true);
    [TestMethod] public Task MySql_recovers_renamed_manifest_policy_and_vector_bounds() => RecoverAsync(false);
    private const string Manifest = "fn_ai_knowledge_document_manifest";
    private const string Vector = "fn_ai_knowledge_index_vector";
    private static async Task RecoverAsync(bool sqlServer)
    {
        var connection = await Migration246AiKnowledgeIndexTaskRecoveryTests.DatabaseAsync(sqlServer);
        Migration249AiKnowledgeIndexVectorRecoveryTests.Replay(sqlServer, connection);
        Migration253AiKnowledgePdfVectorRecoveryTests.Replay(sqlServer, connection);
        Migration255AiKnowledgeDocxPersistenceRecoveryTests.Replay(sqlServer, connection);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(sqlServer, connection);
        var before = await SeedAsync(db);
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_docx_manifest (Id,FormatKey,PayloadHash,Payload,CreatedAtUtc) VALUES (@TaskId,'knowledge-docx-manifest-v1',@Hash,@Payload,@Now)", before);
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_vector (Id,ProviderKey,ModelId,Dimensions,FormatKey,DistanceKey,PayloadHash,Payload,CreatedAtUtc) VALUES (@OperationId,'openai_compatible','text-embedding-3-small',1536,'knowledge-docx-vector-batch-v1','cosine',@Hash,@Payload,@Now)", before);
        Replay(sqlServer, connection);
        CollectionAssert.AreEqual((byte[])before["Payload"]!, await db.QuerySingleAsync<byte[]>($"SELECT Payload FROM {Manifest} WHERE Id=@TaskId", before));
        CollectionAssert.AreEqual((byte[])before["Payload"]!, await db.QuerySingleAsync<byte[]>($"SELECT Payload FROM {Vector} WHERE Id=@OperationId", before));
        Assert.AreEqual("default", await db.QuerySingleAsync<string>("SELECT ParsingPolicyKey FROM fn_ai_knowledge_version WHERE Id=@VersionId", before));
        Assert.AreEqual("default", await db.QuerySingleAsync<string>("SELECT ParsingPolicyKey FROM fn_ai_knowledge_import WHERE VersionId=@VersionId", before));
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ParsingPolicyKey='include_hidden' WHERE Id=@VersionId", before);
        var next = await SeedAsync(db); next["Payload"] = new byte[9 * 1024 * 1024];
        Assert.AreEqual(1, await db.ExecuteAsync($"INSERT INTO {Manifest} (Id,FormatKey,PayloadHash,Payload,CreatedAtUtc) VALUES (@TaskId,'knowledge-document-manifest-v1',@Hash,@Payload,@Now)", next));
        next["Payload"] = new byte[32];
        Assert.AreEqual(1, await db.ExecuteAsync($"INSERT INTO {Vector} (Id,ProviderKey,ModelId,Dimensions,FormatKey,DistanceKey,PayloadHash,Payload,CreatedAtUtc) VALUES (@OperationId,'openai_compatible','text-embedding-3-small',1536,'knowledge-document-vector-batch-v1','cosine',@Hash,@Payload,@Now)", next));
        // 已安装新守卫但被禁用，以及已移除原守卫，均是可恢复的中断状态。
        foreach (var (table, guard) in new[] { (Manifest, "CK_fn_ai_knowledge_document_manifest_Bounds"), (Vector, "CK_fn_ai_knowledge_index_vector_DocumentFormats") })
        {
            await db.ExecuteAsync(sqlServer ? $"ALTER TABLE dbo.{table} NOCHECK CONSTRAINT {guard}" : $"ALTER TABLE {table} ALTER CHECK {guard} NOT ENFORCED");
            Replay(sqlServer, connection);
            var trusted = await db.QuerySingleAsync<int>(sqlServer
                ? "SELECT COUNT(*) FROM sys.check_constraints WHERE name=@Guard AND is_disabled=0 AND is_not_trusted=0"
                : "SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME=@Guard AND ENFORCED='YES'", new { Guard = guard });
            Assert.AreEqual(1, trusted);
        }
        Replay(sqlServer, connection);
        Assert.AreEqual("include_hidden", await db.QuerySingleAsync<string>("SELECT ParsingPolicyKey FROM fn_ai_knowledge_version WHERE Id=@VersionId", before));
        Assert.AreEqual("queued", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", before));
        CollectionAssert.AreEqual((byte[])before["Payload"]!, await db.QuerySingleAsync<byte[]>($"SELECT Payload FROM {Manifest} WHERE Id=@TaskId", before));
        foreach (var format in new[] { "KNOWLEDGE-DOCUMENT-MANIFEST-V1", "knowledge-document-manifest-v1 ", "unknown" })
            await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync($"UPDATE {Manifest} SET FormatKey=@Format WHERE Id=@TaskId", new { Format = format, TaskId = next["TaskId"] }));
        foreach (var format in new[] { "KNOWLEDGE-DOCUMENT-VECTOR-BATCH-V1", "knowledge-document-vector-batch-v1 ", "unknown" })
            await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync($"UPDATE {Vector} SET FormatKey=@Format WHERE Id=@OperationId", new { Format = format, OperationId = next["OperationId"] }));
        var invalid = await SeedAsync(db); invalid["Payload"] = new byte[7];
        await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync($"INSERT INTO {Manifest} (Id,FormatKey,PayloadHash,Payload,CreatedAtUtc) VALUES (@TaskId,'knowledge-document-manifest-v1',@Hash,@Payload,@Now)", invalid));
    }
    private static async Task<Dictionary<string, object?>> SeedAsync(DbConnection db)
    {
        var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db);
        p["OperationId"] = Guid.CreateVersion7(); p["Hash"] = new string('A', 64); p["Payload"] = new byte[32];
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_task (Id,DocumentId,VersionId,DocumentVersion,KnowledgeBaseVersion,ModelConfigId,ModelConfigVersion,StateKey,AttemptCount,CreatedAtUtc,UpdatedAtUtc) VALUES (@TaskId,@DocumentId,@VersionId,1,1,@ModelConfigId,1,'queued',0,@Now,@Now)", p);
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_batch (Id,IndexTaskId,BatchOrdinal,RequestHash,InputCount,CreatedAtUtc) VALUES (@OperationId,@TaskId,0,@Hash,1,@Now)", p);
        return p;
    }
    private static void Replay(bool sqlServer, string connection)
    {
        var builder = sqlServer ? DeployChanges.To.SqlDatabase(connection) : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connection, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var result = builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name => name.Contains(sqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.", StringComparison.Ordinal)
            && name.EndsWith("277_AiKnowledgeDocumentParsing.sql", StringComparison.Ordinal)).JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful, result.Error?.ToString()); Assert.HasCount(1, result.Scripts);
    }
}
