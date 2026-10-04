using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>清单属于任务；读写同时守卫原会话、当前来源、批准、租约与可信租户。</summary>
internal static class AiKnowledgeIndexTextManifestSql
{
    private const string Joins = " FROM fn_ai_knowledge_index_task INNER JOIN fn_ai_knowledge_index_session AS binding ON binding.IndexTaskId=fn_ai_knowledge_index_task.Id";
    private const string Held = " WHERE fn_ai_knowledge_index_task.Id=@TaskId AND fn_ai_knowledge_index_task.StateKey='running' AND fn_ai_knowledge_index_task.LeaseToken=@LeaseToken AND fn_ai_knowledge_index_task.LeaseExpiresAtUtc>@Now AND binding.ActorUserId=@OwnerUserId AND binding.SessionId=@SessionId AND binding.SessionKind=@SessionKind AND binding.SecurityStamp=@SecurityStamp AND binding.ActorScope=@ActorScope AND binding.EffectiveScope=@EffectiveScope AND EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS source WHERE source.Id=fn_ai_knowledge_index_task.VersionId AND source.FileId=@FileId AND source.ContentHash=@ContentHash AND source.ContentType=@ContentType AND source.SizeBytes=@SizeBytes)";
    private const string Insert = "INSERT INTO fn_ai_knowledge_text_manifest (Id,FormatKey,PayloadHash,Payload,CreatedAtUtc) SELECT fn_ai_knowledge_index_task.Id,@FormatKey,@PayloadHash,@Payload,@Now" + Joins + Held;
    private const string Read = "SELECT manifest.Id,manifest.FormatKey,manifest.PayloadHash,manifest.Payload,manifest.CreatedAtUtc" + Joins
        + " INNER JOIN fn_ai_knowledge_text_manifest AS manifest ON manifest.Id=fn_ai_knowledge_index_task.Id" + Held;
    private const string Host = " AND binding.TenantId IS NULL" + AiKnowledgeIndexTaskSql.OwnerExists + AiKnowledgeIndexTaskSql.Host + AiKnowledgeIndexTaskSql.Live + ")";
    private const string Tenant = " AND binding.TenantId=@TenantId" + AiKnowledgeIndexTaskSql.OwnerExists + AiKnowledgeIndexTaskSql.Tenant + AiKnowledgeIndexTaskSql.Live + ")";
    internal static readonly SqlStatement InsertHost = new("ai.knowledge_text_manifest.insert_host", Insert + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertTenant = new("ai.knowledge_text_manifest.insert_tenant", Insert + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindHost = new("ai.knowledge_text_manifest.find_host", Read + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindTenant = new("ai.knowledge_text_manifest.find_tenant", Read + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
