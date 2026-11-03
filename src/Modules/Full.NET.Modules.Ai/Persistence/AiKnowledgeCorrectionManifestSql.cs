using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>人工计划复用代次清单的所有权及清理边界；只能写入冻结身份仍有效的人工任务。</summary>
internal static class AiKnowledgeCorrectionManifestSql
{
    private const string Joins = " FROM fn_ai_knowledge_index_task INNER JOIN fn_ai_knowledge_index_session AS binding ON binding.IndexTaskId=fn_ai_knowledge_index_task.Id";
    private const string Held = " WHERE fn_ai_knowledge_index_task.Id=@TaskId AND fn_ai_knowledge_index_task.StateKey='running' AND fn_ai_knowledge_index_task.LeaseToken=@LeaseToken AND fn_ai_knowledge_index_task.LeaseExpiresAtUtc>@Now AND binding.ActorUserId=@OwnerUserId AND binding.SessionId=@SessionId AND binding.SessionKind=@SessionKind AND binding.SecurityStamp=@SecurityStamp AND binding.ActorScope=@ActorScope AND binding.EffectiveScope=@EffectiveScope AND fn_ai_knowledge_index_task.CorrectionSourceId=@CorrectionSourceId AND fn_ai_knowledge_index_task.CorrectionRevisionId=@CorrectionRevisionId AND fn_ai_knowledge_index_task.CorrectionHeadVersion=@CorrectionHeadVersion AND fn_ai_knowledge_index_task.CorrectionSourceHash=@CorrectionSourceHash AND fn_ai_knowledge_index_task.CorrectionPayloadHash=@CorrectionPayloadHash";
    private const string Host = " AND binding.TenantId IS NULL" + AiKnowledgeIndexTaskSql.OwnerExists + AiKnowledgeIndexTaskSql.Host + AiKnowledgeIndexTaskSql.Live + ")";
    private const string Tenant = " AND binding.TenantId=@TenantId" + AiKnowledgeIndexTaskSql.OwnerExists + AiKnowledgeIndexTaskSql.Tenant + AiKnowledgeIndexTaskSql.Live + ")";
    private const string Insert = "INSERT INTO fn_ai_knowledge_document_manifest (Id,FormatKey,PayloadHash,Payload,CreatedAtUtc) SELECT fn_ai_knowledge_index_task.Id,@FormatKey,@PayloadHash,@Payload,@Now" + Joins + Held;
    private static string Read(bool mysql, bool tenant) => "SELECT manifest.Id,manifest.FormatKey,manifest.PayloadHash,CASE WHEN "
        + (mysql ? "OCTET_LENGTH" : "DATALENGTH") + "(manifest.Payload) BETWEEN 8 AND 16777215 THEN manifest.Payload ELSE NULL END AS Payload,manifest.CreatedAtUtc"
        + Joins + " INNER JOIN fn_ai_knowledge_document_manifest AS manifest ON manifest.Id=fn_ai_knowledge_index_task.Id" + Held + (tenant ? Tenant : Host);
    internal static readonly SqlStatement InsertHost = new("ai.knowledge_correction_manifest.insert_host", Insert + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertTenant = new("ai.knowledge_correction_manifest.insert_tenant", Insert + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindSqlServerHost = new("ai.knowledge_correction_manifest.find_sqlserver_host", Read(false, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindSqlServerTenant = new("ai.knowledge_correction_manifest.find_sqlserver_tenant", Read(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindMySqlHost = new("ai.knowledge_correction_manifest.find_mysql_host", Read(true, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindMySqlTenant = new("ai.knowledge_correction_manifest.find_mysql_tenant", Read(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement Find(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql
        ? tenant ? FindMySqlTenant : FindMySqlHost : tenant ? FindSqlServerTenant : FindSqlServerHost;
}
