using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>重建只写 AI 自有新任务；同文档锁、预期终态及已知费用同时限制代次分配。</summary>
internal static class AiKnowledgeIndexRebuildSql
{
    private const string Owner = "document.Id=@DocumentId AND document.KnowledgeBaseId=@KnowledgeBaseId AND knowledge.OwnerUserId=@OwnerUserId AND ";
    private const string Request = "SELECT " + AiKnowledgeIndexTaskSql.Columns + " FROM fn_ai_knowledge_index_task AS task INNER JOIN fn_ai_knowledge_document AS document ON document.Id=task.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE " + Owner;
    private const string Binding = " AND task.VersionId=@VersionId AND task.DocumentVersion=@DocumentVersion AND task.KnowledgeBaseVersion=@KnowledgeBaseVersion AND task.ModelConfigId=@ModelConfigId AND task.ModelConfigVersion=@ModelConfigVersion AND task.RebuildRequestId=@RebuildRequestId AND task.RebuildPreviousTaskId=@ExpectedTaskId";
    // MySQL 允许 INSERT SELECT 在主查询读取目标表，但禁止目标表子查询；代次与历史账本全部在主查询连接并聚合。
    private const string GenerationJoins = " INNER JOIN fn_ai_knowledge_index_task AS predecessor ON predecessor.Id=@ExpectedTaskId AND predecessor.DocumentId=document.Id AND predecessor.VersionId=version.Id"
        + " LEFT JOIN fn_ai_knowledge_index_task AS newerTask ON newerTask.DocumentId=predecessor.DocumentId AND newerTask.VersionId=predecessor.VersionId AND (newerTask.DocumentVersion>predecessor.DocumentVersion OR (newerTask.DocumentVersion=predecessor.DocumentVersion AND newerTask.KnowledgeBaseVersion>predecessor.KnowledgeBaseVersion) OR (newerTask.DocumentVersion=predecessor.DocumentVersion AND newerTask.KnowledgeBaseVersion=predecessor.KnowledgeBaseVersion AND newerTask.Generation>predecessor.Generation))"
        + " LEFT JOIN fn_ai_knowledge_index_task AS prior ON prior.DocumentId=document.Id LEFT JOIN fn_ai_knowledge_index_batch AS batch ON batch.IndexTaskId=prior.Id LEFT JOIN fn_ai_operation_budget AS ledger ON ledger.Id=batch.Id";
    private const string Previous = " AND predecessor.DocumentVersion=@DocumentVersion AND predecessor.KnowledgeBaseVersion=@KnowledgeBaseVersion AND predecessor.ModelConfigId=@ModelConfigId AND predecessor.ModelConfigVersion=@ModelConfigVersion AND predecessor.StateKey IN ('failed','cancelled','completed') AND predecessor.Generation<2147483647 AND newerTask.Id IS NULL";
    // 已取消任务也可能存在外部未知结果；检查文档全部历史任务，不能换代次释放预留或绕过对账。
    private const string KnownCharges = " GROUP BY document.Id,version.Id,document.Version,knowledge.Version,model.Id,model.Version,predecessor.Generation HAVING MAX(CASE WHEN batch.Id IS NOT NULL AND (ledger.Id IS NULL OR ledger.SettledAtUtc IS NULL OR ledger.UsageStatus<>'known' OR ledger.Outcome NOT IN ('succeeded','failed','cancelled')) THEN 1 ELSE 0 END)=0";
    private static string Create(SqlStatement basis) => basis.Text
        .Replace("ModelConfigVersion, StateKey", "ModelConfigVersion, Generation, RebuildRequestId, RebuildPreviousTaskId, StateKey", StringComparison.Ordinal)
        .Replace("model.Version, 'queued'", "model.Version, CASE WHEN predecessor.Generation<2147483647 THEN predecessor.Generation+1 ELSE NULL END, @RebuildRequestId, @ExpectedTaskId, 'queued'", StringComparison.Ordinal)
        .Replace(" AS model ON model.Id = knowledge.EmbeddingModelConfigId WHERE ", " AS model ON model.Id = knowledge.EmbeddingModelConfigId" + GenerationJoins + " WHERE ", StringComparison.Ordinal)
        + " AND model.Id=@ModelConfigId AND model.Version=@ModelConfigVersion" + Previous + KnownCharges;
    private static string Locked(SqlStatement basis, bool mysql) => mysql ? basis.Text + " FOR UPDATE"
        : basis.Text.Replace(" AS document ", " AS document WITH(UPDLOCK,HOLDLOCK) ", StringComparison.Ordinal)
            .Replace(" AS knowledge ", " AS knowledge WITH(UPDLOCK,HOLDLOCK) ", StringComparison.Ordinal)
            .Replace(" AS version ", " AS version WITH(UPDLOCK,HOLDLOCK) ", StringComparison.Ordinal)
            .Replace(" AS model ", " AS model WITH(UPDLOCK,HOLDLOCK) ", StringComparison.Ordinal);
    internal static readonly SqlStatement CreateHost = new("ai.knowledge_index_rebuild.create_host", Create(AiKnowledgeIndexTaskSql.EnqueueHost), SqlDataScope.HostOnly);
    internal static readonly SqlStatement CreateTenant = new("ai.knowledge_index_rebuild.create_tenant", Create(AiKnowledgeIndexTaskSql.EnqueueTenant), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement RequestHost = new("ai.knowledge_index_rebuild.request_host", Request + AiKnowledgeIndexTaskSql.Host + Binding + AiKnowledgeIndexTaskSql.LatestTask, SqlDataScope.HostOnly);
    internal static readonly SqlStatement RequestTenant = new("ai.knowledge_index_rebuild.request_tenant", Request + AiKnowledgeIndexTaskSql.Tenant + Binding + AiKnowledgeIndexTaskSql.LatestTask, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement SessionHost = new("ai.knowledge_index_rebuild.session_host", AiKnowledgeIndexSessionSql.FindBindingHost.Text + " AND fn_ai_knowledge_index_task.Id=@TaskId", SqlDataScope.HostOnly);
    internal static readonly SqlStatement SessionTenant = new("ai.knowledge_index_rebuild.session_tenant", AiKnowledgeIndexSessionSql.FindBindingTenant.Text + " AND fn_ai_knowledge_index_task.Id=@TaskId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement LockSqlServerHost = new("ai.knowledge_index_rebuild.lock_sqlserver_host", Locked(AiKnowledgeIndexEnqueueSql.ReconciliationSourceHost, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockSqlServerTenant = new("ai.knowledge_index_rebuild.lock_sqlserver_tenant", Locked(AiKnowledgeIndexEnqueueSql.ReconciliationSourceTenant, false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement LockMySqlHost = new("ai.knowledge_index_rebuild.lock_mysql_host", Locked(AiKnowledgeIndexEnqueueSql.ReconciliationSourceHost, true), SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockMySqlTenant = new("ai.knowledge_index_rebuild.lock_mysql_tenant", Locked(AiKnowledgeIndexEnqueueSql.ReconciliationSourceTenant, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement LockSource(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? LockMySqlTenant : LockMySqlHost : tenant ? LockSqlServerTenant : LockSqlServerHost;
}
