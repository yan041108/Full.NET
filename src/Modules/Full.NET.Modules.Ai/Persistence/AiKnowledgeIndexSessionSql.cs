using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>会话归属于 AI 任务；仅创建，不允许用新登录覆盖旧绑定。</summary>
internal static class AiKnowledgeIndexSessionSql
{
    private const string Insert = "INSERT INTO fn_ai_knowledge_index_session (Id,IndexTaskId,ActorUserId,TenantId,SessionId,SessionKind,SecurityStamp,ActorScope,EffectiveScope,CreatedAtUtc) SELECT @SessionRecordId,fn_ai_knowledge_index_task.Id,knowledge.OwnerUserId,knowledge.TenantId,@SessionId,@SessionKind,@SecurityStamp,@ActorScope,@EffectiveScope,@Now FROM fn_ai_knowledge_index_task INNER JOIN fn_ai_knowledge_document AS document ON document.Id=fn_ai_knowledge_index_task.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE fn_ai_knowledge_index_task.Id=@TaskId AND StateKey='queued' AND AttemptCount=0 AND document.Id=@DocumentId AND document.KnowledgeBaseId=@KnowledgeBaseId AND knowledge.OwnerUserId=@OwnerUserId AND ";
    private const string Read = "SELECT binding.Id,binding.IndexTaskId,binding.ActorUserId,binding.TenantId,binding.SessionId,binding.SessionKind,binding.SecurityStamp,binding.ActorScope,binding.EffectiveScope,binding.CreatedAtUtc,fn_ai_knowledge_index_task.LeaseExpiresAtUtc FROM fn_ai_knowledge_index_session AS binding INNER JOIN fn_ai_knowledge_index_task ON fn_ai_knowledge_index_task.Id=binding.IndexTaskId WHERE binding.ActorUserId=@OwnerUserId";
    private const string Held = " AND fn_ai_knowledge_index_task.Id=@TaskId AND fn_ai_knowledge_index_task.StateKey='running' AND fn_ai_knowledge_index_task.LeaseToken=@LeaseToken AND fn_ai_knowledge_index_task.LeaseExpiresAtUtc>@Now";
    private const string Host = AiKnowledgeIndexTaskSql.Host + " AND binding.TenantId IS NULL";
    private const string Tenant = AiKnowledgeIndexTaskSql.Tenant + " AND binding.TenantId=@TenantId";

    internal static readonly SqlStatement InsertHost = new("ai.knowledge_index_session.insert_host", Insert + AiKnowledgeIndexTaskSql.Host + AiKnowledgeIndexTaskSql.Live, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertTenant = new("ai.knowledge_index_session.insert_tenant", Insert + AiKnowledgeIndexTaskSql.Tenant + AiKnowledgeIndexTaskSql.Live, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindBindingHost = new("ai.knowledge_index_session.binding_host", Read + AiKnowledgeIndexTaskSql.OwnerExists + Host + AiKnowledgeIndexTaskSql.Live + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindBindingTenant = new("ai.knowledge_index_session.binding_tenant", Read + AiKnowledgeIndexTaskSql.OwnerExists + Tenant + AiKnowledgeIndexTaskSql.Live + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement HeldHost = new("ai.knowledge_index_session.held_host", Read + Held + AiKnowledgeIndexTaskSql.OwnerExists + Host + AiKnowledgeIndexTaskSql.Live + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement HeldTenant = new("ai.knowledge_index_session.held_tenant", Read + Held + AiKnowledgeIndexTaskSql.OwnerExists + Tenant + AiKnowledgeIndexTaskSql.Live + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
