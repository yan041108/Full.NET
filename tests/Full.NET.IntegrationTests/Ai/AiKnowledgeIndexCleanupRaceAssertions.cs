using System.Net;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>重放生产执行器实际准备的向量写入，确定性验证清理锁的两种先后顺序。</summary>
internal static class AiKnowledgeIndexCleanupRaceAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, HttpClient client, Guid owner, Guid? tenantId, AiKnowledgeIndexCleanupFaults faults)
    {
        await using var db = AiOperationBudgetAssertions.Connection(factory);
        foreach (var writerFirst in new[] { true, false })
        {
            var row = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, tenantId is not null);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET OwnerUserId=@Owner,TenantId=@Tenant WHERE Id=@Id", new { Owner = owner, Tenant = tenantId, Id = row["KnowledgeBaseId"] });
            await db.ExecuteAsync("UPDATE fn_ai_model_config SET TenantId=@Tenant WHERE Id=@Id", new { Tenant = tenantId, Id = row["ModelConfigId"] });
            var bytes = Encoding.UTF8.GetBytes("清理竞争原始载荷 race proof"); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size WHERE Id=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@Id", new { Hash = hash, Size = bytes.Length, Id = row["VersionId"] });
            using var enqueue = await client.PostAsync(AiKnowledgeIndexStatusApiAssertions.Path(row), null);
            Assert.AreEqual(HttpStatusCode.Accepted, enqueue.StatusCode, await enqueue.Content.ReadAsStringAsync());
            using var json = JsonDocument.Parse(await enqueue.Content.ReadAsStringAsync()); var taskId = json.RootElement.GetProperty("taskId").GetGuid(); row["TaskId"] = taskId;
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ICurrentTenantContextWriter>();
            if (tenantId is { } id) context.SetTenant(new(id, "acme", "测试")); else context.SetHost();
            var tasks = services.GetRequiredService<AiKnowledgeIndexTaskStore>();
            var binding = new AiKnowledgeIndexTaskBinding((Guid)row["KnowledgeBaseId"]!, (Guid)row["DocumentId"]!, (Guid)row["VersionId"]!, 1, 1, owner);
            var held = await tasks.AcquireAsync(binding, taskId, default); Assert.IsNotNull(held);
            // 本用例验证数据库锁顺序，准备代次不依赖宿主在初始租约的五秒余量内完成；生产租约边界另有覆盖。
            Assert.AreEqual(1, await db.ExecuteAsync(
                "UPDATE fn_ai_knowledge_index_task SET LeaseExpiresAtUtc=@Until WHERE Id=@TaskId AND LeaseToken=@Lease AND StateKey='running'",
                new { TaskId = taskId, Lease = held.LeaseToken, Until = DateTime.UtcNow.AddMinutes(5) }));
            var files = new AiKnowledgeIndexEmbeddingAssertions.ControlledFiles((Guid)row["FileId"]!, binding.VersionId, bytes, hash);
            var provider = new AiKnowledgeIndexEmbeddingAssertions.ControlledFactory();
            var generation = AiKnowledgeIndexStatusApiAssertions.Generation(services, tasks, files, provider);
            Assert.IsTrue(await generation.ExecuteTextGenerationAsync(binding, taskId, held.LeaseToken!.Value, default));
            var statement = faults.VectorStatement!; var parameters = new Dictionary<string, object?>(faults.VectorParameters!);
            var paid = JsonSerializer.Serialize(await db.QueryAsync("SELECT * FROM fn_ai_operation_budget WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId)", row));
            // 模拟响应丢失后的持久运行态；保留同一收费批次，旧执行者仍带原写入和租约。
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_vector WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId)", row);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='running',LeaseToken=@Lease,LeaseExpiresAtUtc=@Until WHERE Id=@TaskId",
                new { Lease = held.LeaseToken, Until = DateTime.UtcNow.AddMinutes(5), TaskId = taskId });
            parameters["Now"] = DateTimeOffset.UtcNow;
            faults.VersionId = binding.VersionId; faults.TenantId = tenantId;
            faults.VectorEntering = Signal(); faults.CleanupEntering = Signal();
            faults.ContinueVector = Signal(); faults.ContinueCleanup = Signal();
            if (writerFirst) faults.VectorWritten = Signal(); else faults.CleanupLocked = Signal();
            Task<int>? writing = null; Task<HttpResponseMessage>? cleaning = null;
            var cleanup = $"/api/v1/ai/knowledge-bases/{binding.KnowledgeBaseId}/documents/{binding.DocumentId}/imports/cleanup";
            try
            {
                if (writerFirst)
                {
                    writing = Write(); await faults.VectorWritten!.Task.WaitAsync(TimeSpan.FromSeconds(20));
                }
                // 已提交墓碑但进程尚未清理的崩溃状态；本实验只验证数据库并发，不代替杀进程实验。
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1,Version=Version+1 WHERE Id=@DocumentId", row, commandTimeout: 10);
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET StateKey='cleanup_pending' WHERE DocumentId=@DocumentId", row, commandTimeout: 10);
                cleaning = client.PostAsync(cleanup, null);
                if (writerFirst)
                {
                    await faults.CleanupEntering.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    await WaitForDatabaseLockAsync(factory);
                    Assert.IsFalse(cleaning.IsCompleted); faults.ContinueVector.TrySetResult(true);
                }
                else
                {
                    await faults.CleanupLocked!.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    writing = Write(); await faults.VectorEntering.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    await WaitForDatabaseLockAsync(factory);
                    Assert.IsFalse(writing.IsCompleted); faults.ContinueCleanup.TrySetResult(true);
                }
                Assert.AreEqual(writerFirst ? 1 : 0, await writing!);
                using var response = await cleaning; Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            }
            finally
            {
                faults.ContinueVector.TrySetResult(true); faults.ContinueCleanup.TrySetResult(true);
                try { if (writing is not null) await writing; }
                finally
                {
                    try { if (cleaning is not null) (await cleaning).Dispose(); }
                    finally { faults.VectorWritten = null; faults.CleanupLocked = null; faults.VectorEntering = null; faults.CleanupEntering = null; }
                }
            }
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT (SELECT COUNT(*) FROM fn_ai_knowledge_index_vector WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId))+(SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@TaskId)+(SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE DocumentId=@DocumentId)", row));
            Assert.AreEqual("cancelled", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@TaskId", row));
            Assert.AreEqual(paid, JsonSerializer.Serialize(await db.QueryAsync("SELECT * FROM fn_ai_operation_budget WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId)", row)));
            Assert.AreEqual(1, provider.Calls);
            Task<int> Write() => services.GetRequiredService<ICommandTransaction>().ExecuteAsync(
                ct => services.GetRequiredService<ICommandExecutor>().ExecuteAsync(statement, parameters, ct), default);
        }
    }

    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitForDatabaseLockAsync(FullNetApiFactory factory)
    {
        // 只观察本用例的隔离业务库；不靠调度延迟或未完成的 Task 推断 SQL 已经等锁。
        var mysql = factory.Provider == DatabaseProvider.MySql;
        await using DbConnection observer = mysql
            ? new MySqlConnection(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync())
            : new SqlConnection(factory.ConnectionString);
        var sql = mysql
            ? "SELECT COUNT(*) FROM performance_schema.data_lock_waits AS waits INNER JOIN performance_schema.data_locks AS held ON held.ENGINE_LOCK_ID=waits.BLOCKING_ENGINE_LOCK_ID WHERE held.OBJECT_SCHEMA=@Database AND held.OBJECT_NAME='fn_ai_knowledge_index_task'"
            : "SELECT COUNT(*) FROM sys.dm_exec_requests AS waiting CROSS APPLY sys.dm_exec_sql_text(waiting.sql_handle) AS command WHERE waiting.database_id=DB_ID() AND waiting.blocking_session_id>0 AND waiting.wait_type LIKE 'LCK_M_%' AND command.text LIKE '%fn_ai_knowledge_index_task%'";
        var database = mysql ? SharedDatabaseFixture.GetMySqlDatabaseName(factory.ConnectionString) : string.Empty;
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (await observer.QuerySingleAsync<int>(sql, new { Database = database }, commandTimeout: 5) > 0) return;
            await Task.Delay(25);
        }
        Assert.Fail("The competing production SQL did not enter a database lock wait.");
    }
}
