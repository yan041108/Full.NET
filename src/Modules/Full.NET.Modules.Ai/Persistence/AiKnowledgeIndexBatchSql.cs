using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>批次建立及冲突恢复均持有当前租约；仅访问 AI 自有表，不在事务中调用预算或外部服务。</summary>
internal static class AiKnowledgeIndexBatchSql
{
    private const string Held = "fn_ai_knowledge_index_task.Id = @TaskId AND fn_ai_knowledge_index_task.StateKey = 'running' AND fn_ai_knowledge_index_task.LeaseToken = @LeaseToken AND fn_ai_knowledge_index_task.LeaseExpiresAtUtc > @Now" + AiKnowledgeIndexTaskSql.OwnerExists;
    private const string Prepare = "INSERT INTO fn_ai_knowledge_index_batch (Id, IndexTaskId, BatchOrdinal, RequestHash, InputCount, CreatedAtUtc) SELECT @OperationId, fn_ai_knowledge_index_task.Id, @BatchOrdinal, @RequestHash, @InputCount, @Now FROM fn_ai_knowledge_index_task WHERE " + Held;
    private const string Find = "SELECT batch.Id, batch.IndexTaskId, batch.BatchOrdinal, batch.RequestHash, batch.InputCount, batch.CreatedAtUtc FROM fn_ai_knowledge_index_batch AS batch INNER JOIN fn_ai_knowledge_index_task ON fn_ai_knowledge_index_task.Id = batch.IndexTaskId WHERE batch.BatchOrdinal = @BatchOrdinal AND " + Held;
    // MySQL 的 ReadCommitted INSERT SELECT 默认不锁来源；显式锁定当前任务后才能插入。
    internal static readonly SqlStatement PrepareHost = new("ai.knowledge_index_batch.prepare_host", Prepare + AiKnowledgeIndexTaskSql.Host + AiKnowledgeIndexTaskSql.Live + ") FOR SHARE", SqlDataScope.HostOnly);
    internal static readonly SqlStatement PrepareTenant = new("ai.knowledge_index_batch.prepare_tenant", Prepare + AiKnowledgeIndexTaskSql.Tenant + AiKnowledgeIndexTaskSql.Live + ") FOR SHARE", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement PrepareSqlServerHost = new("ai.knowledge_index_batch.prepare_sqlserver_host", Prepare.Replace("FROM fn_ai_knowledge_index_task WHERE", "FROM fn_ai_knowledge_index_task WITH(UPDLOCK,HOLDLOCK) WHERE") + AiKnowledgeIndexTaskSql.Host + AiKnowledgeIndexTaskSql.Live + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement PrepareSqlServerTenant = new("ai.knowledge_index_batch.prepare_sqlserver_tenant", Prepare.Replace("FROM fn_ai_knowledge_index_task WHERE", "FROM fn_ai_knowledge_index_task WITH(UPDLOCK,HOLDLOCK) WHERE") + AiKnowledgeIndexTaskSql.Tenant + AiKnowledgeIndexTaskSql.Live + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindHost = new("ai.knowledge_index_batch.find_host", Find + AiKnowledgeIndexTaskSql.Host + AiKnowledgeIndexTaskSql.Live + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindTenant = new("ai.knowledge_index_batch.find_tenant", Find + AiKnowledgeIndexTaskSql.Tenant + AiKnowledgeIndexTaskSql.Live + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
