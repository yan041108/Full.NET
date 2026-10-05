using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>人工恢复仅重置可恢复任务，保留所有原会话、收费与派生数据。</summary>
internal static class AiKnowledgeIndexRetrySql
{
    // 会话行不可替换，使用已经逐字段核对的行主键在写入判定点再次约束归属。
    private const string Retry = "UPDATE fn_ai_knowledge_index_task SET StateKey='queued',AttemptCount=0,FailureCode=NULL,LeaseToken=NULL,LeaseExpiresAtUtc=NULL,UpdatedAtUtc=@Now WHERE Id=@TaskId AND (StateKey='failed' OR (StateKey='running' AND AttemptCount=3 AND LeaseExpiresAtUtc<=@Now)) AND ModelConfigId=@ModelConfigId AND ModelConfigVersion=@ModelConfigVersion AND EXISTS (SELECT 1 FROM fn_ai_knowledge_index_session AS binding WHERE binding.Id=@SessionRecordId AND binding.IndexTaskId=fn_ai_knowledge_index_task.Id) AND EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS source WHERE source.Id=fn_ai_knowledge_index_task.VersionId AND source.ContentType IN ('text/plain','text/markdown'))" + AiKnowledgeIndexTaskSql.OwnerExists;
    internal static readonly SqlStatement Host = new("ai.knowledge_index_retry.host", Retry + AiKnowledgeIndexTaskSql.Host + AiKnowledgeIndexTaskSql.Live + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement Tenant = new("ai.knowledge_index_retry.tenant", Retry + AiKnowledgeIndexTaskSql.Tenant + AiKnowledgeIndexTaskSql.Live + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
