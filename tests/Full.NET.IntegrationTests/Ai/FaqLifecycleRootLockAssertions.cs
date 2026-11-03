using System.Data;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实数据库强制制造根锁等待，验证预读不会先占住任务行，且取消后事务排空。</summary>
internal static class FaqLifecycleRootLockAssertions
{
    internal static async Task VerifyAsync(DatabaseProvider provider, string connection, IServiceProvider services,
        AiKnowledgeSearchSource source, Guid model)
    {
        var mysql = provider == DatabaseProvider.MySql;
        await using var blocker = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(!mysql, connection); await blocker.OpenAsync();
        var tenantId = await blocker.QuerySingleAsync<Guid?>("SELECT TenantId FROM fn_ai_knowledge_base WHERE Id=@Id", new { Id = source.KnowledgeBaseId });
        var owner = await blocker.QuerySingleAsync<Guid>("SELECT OwnerUserId FROM fn_ai_knowledge_base WHERE Id=@Id", new { Id = source.KnowledgeBaseId });
        var tenant = services.GetRequiredService<CurrentTenantAccessor>();
        if (tenantId is { } id) tenant.SetTenant(new(id, "root-lock", "根锁验证")); else tenant.SetHost();
        var binding = new AiKnowledgeIndexTaskBinding(source.KnowledgeBaseId, source.DocumentId, source.VersionId,
            source.DocumentVersion, source.KnowledgeBaseVersion, owner);
        var p = new Dictionary<string, object?> { ["KnowledgeBaseId"] = source.KnowledgeBaseId, ["DocumentId"] = source.DocumentId,
            ["VersionId"] = source.VersionId, ["TaskId"] = source.GenerationId, ["OwnerUserId"] = owner, ["TenantId"] = tenantId,
            ["ScopeKey"] = tenantId?.ToString("N") ?? "host", ["ModelConfigId"] = model, ["BatchOrdinal"] = 0 };
        await using var held = await blocker.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<AiKnowledgeIndexVectorRecord?>? pending = null;
        try
        {
            Assert.AreEqual(source.DocumentId, await blocker.QuerySingleAsync<Guid>(AiKnowledgePublicationSql.LockFor(provider, tenantId.HasValue).Text, p, held, commandTimeout: 3));
            var session = await blocker.QuerySingleAsync<long>(mysql ? "SELECT CONNECTION_ID()" : "SELECT @@SPID", transaction: held);
            var observerConnection = mysql ? new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync()) { Database = blocker.Database }.ConnectionString : connection;
            await using var observer = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(!mysql, observerConnection); await observer.OpenAsync();
            pending = services.GetRequiredService<AiKnowledgeCorrectionActivationStore>().ReadPayloadUnderRootAsync(binding, p, cancel.Token);
            var waitSql = mysql
                ? "SELECT COUNT(*) FROM performance_schema.data_lock_waits AS waits INNER JOIN performance_schema.data_locks AS locks ON locks.ENGINE_LOCK_ID=waits.BLOCKING_ENGINE_LOCK_ID INNER JOIN performance_schema.threads AS holder ON holder.THREAD_ID=locks.THREAD_ID WHERE locks.OBJECT_SCHEMA=@Database AND locks.OBJECT_NAME='fn_ai_knowledge_document' AND holder.PROCESSLIST_ID=@Session"
                : "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID(@Database) AND blocking_session_id=@Session AND wait_type LIKE 'LCK%'";
            var until = DateTimeOffset.UtcNow.AddSeconds(5); var waiting = false;
            while (!pending.IsCompleted && DateTimeOffset.UtcNow < until)
            {
                waiting = await observer.QuerySingleAsync<int>(waitSql, new { Database = blocker.Database, Session = session }, commandTimeout: 2) > 0;
                if (waiting) break; await Task.Delay(25, cancel.Token);
            }
            Assert.IsTrue(waiting, "必须观察到真实根行等待，不能只靠调度延迟判断锁序。");
            // 旧实现此时已锁住任务行，形成任务→文档与文档→任务的真实死锁；新预读尚未占任务锁。
            var taskLock = mysql ? "SELECT Id FROM fn_ai_knowledge_index_task WHERE Id=@Id FOR UPDATE"
                : "SELECT Id FROM fn_ai_knowledge_index_task WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id";
            Assert.AreEqual(source.GenerationId, await blocker.QuerySingleAsync<Guid>(taskLock, new { Id = source.GenerationId }, held, commandTimeout: 2));
            await cancel.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.IsFalse(services.GetRequiredService<IDataTransactionState>().HasTransaction);
            Console.WriteLine($"FAQ root-lock probe verified: {provider}, {(tenantId.HasValue ? "tenant" : "host")}; task lock remained available and cancelled transaction drained.");
        }
        finally
        {
            await cancel.CancelAsync(); await held.RollbackAsync();
            if (pending is not null) try { await pending.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) { }
            tenant.Clear();
        }
    }
}
