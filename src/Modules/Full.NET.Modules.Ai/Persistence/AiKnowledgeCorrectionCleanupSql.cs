using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>墓碑所属校对正文按原版本及来源逐项清空，保留身份、摘要、父链和审计；不得清理活文档。</summary>
internal static class AiKnowledgeCorrectionCleanupSql
{
    private const string Root = " FROM fn_ai_knowledge_correction_source AS source INNER JOIN fn_ai_knowledge_version AS version ON version.Id=source.VersionId INNER JOIN fn_ai_knowledge_document AS document ON document.Id=version.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE document.IsDeleted=1";
    private const string Identity = " AND source.Id=@SourceId AND document.Id=@DocumentId AND knowledge.Id=@KnowledgeBaseId AND knowledge.OwnerUserId=@OwnerUserId";
    private const string Host = " AND knowledge.TenantId IS NULL";
    private const string Tenant = " AND knowledge.TenantId=@TenantId";
    // 与代次读取使用同一文档根锁；墓碑已禁止新租约，仍须等待该文件版本的既有读取结束。
    private const string Drained = " AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_generation AS generation INNER JOIN fn_ai_knowledge_read_lease AS held ON held.GenerationId=generation.Id WHERE generation.DocumentId=document.Id AND generation.VersionId=source.VersionId AND held.ExpiresAtUtc>@Now)";
    private const string Pending = " AND (source.CleanupState<>2 OR EXISTS (SELECT 1 FROM fn_ai_knowledge_correction_revision AS revision WHERE revision.SourceId=source.Id AND revision.VersionId=source.VersionId AND revision.CleanupState<>2))";
    internal const string Scan = "SELECT source.Id, 3 AS Kind, source.CreatedAtUtc, knowledge.TenantId, knowledge.Id AS KnowledgeBaseId, document.Id AS DocumentId, knowledge.OwnerUserId" + Root + Pending;
    private static string Owned(bool scoped) => Root + Identity + (scoped ? Tenant : Host) + Drained;
    private static string Lock(bool mysql, bool scoped) => "SELECT source.Id" + (mysql ? Owned(scoped) : Owned(scoped).Replace(" AS source INNER", " AS source WITH(UPDLOCK,HOLDLOCK) INNER", StringComparison.Ordinal)) + Pending + (mysql ? " FOR UPDATE" : "");
    private static string Revisions(bool mysql, bool scoped) => (mysql ? "SELECT revision.Id" : "SELECT TOP (25) revision.Id")
        + " FROM fn_ai_knowledge_correction_revision AS revision WHERE revision.SourceId=@SourceId AND revision.CleanupState<>2"
        + " AND EXISTS (SELECT 1" + Owned(scoped) + " AND source.VersionId=revision.VersionId) ORDER BY revision.CreatedAtUtc,revision.Id" + (mysql ? " LIMIT 25" : "");
    private static string Revision(bool erase, bool scoped) => "UPDATE fn_ai_knowledge_correction_revision SET "
        + (erase ? "Payload=@EmptyPayload,CleanupState=2" : "CleanupState=1")
        + " WHERE Id=@RevisionId AND SourceId=@SourceId AND CleanupState=" + (erase ? "1" : "0")
        + " AND EXISTS (SELECT 1" + Owned(scoped) + " AND source.VersionId=fn_ai_knowledge_correction_revision.VersionId)";
    private static string Source(bool erase, bool scoped) => "UPDATE fn_ai_knowledge_correction_source SET "
        + (erase ? "OriginalPayload=@EmptyPayload,InitialPayload=@EmptyPayload,CleanupState=2" : "CleanupState=1")
        + " WHERE Id=@SourceId AND CleanupState=" + (erase ? "1" : "0")
        + " AND EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS version INNER JOIN fn_ai_knowledge_document AS document ON document.Id=version.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId"
        + " WHERE version.Id=fn_ai_knowledge_correction_source.VersionId AND document.IsDeleted=1 AND document.Id=@DocumentId AND knowledge.Id=@KnowledgeBaseId AND knowledge.OwnerUserId=@OwnerUserId"
        + (scoped ? Tenant : Host) + Drained.Replace("source.VersionId", "version.Id", StringComparison.Ordinal) + ")"
        + (erase ? " AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_correction_revision AS revision WHERE revision.SourceId=@SourceId AND revision.VersionId=fn_ai_knowledge_correction_source.VersionId AND revision.CleanupState<>2)" : "");
    private static readonly SqlStatement LockSqlServerHost = new("ai.knowledge_correction_cleanup.lock_sqlserver_host", Lock(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement LockSqlServerTenant = new("ai.knowledge_correction_cleanup.lock_sqlserver_tenant", Lock(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement LockMySqlHost = new("ai.knowledge_correction_cleanup.lock_mysql_host", Lock(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement LockMySqlTenant = new("ai.knowledge_correction_cleanup.lock_mysql_tenant", Lock(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement RevisionsSqlServerHost = new("ai.knowledge_correction_cleanup.revisions_sqlserver_host", Revisions(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement RevisionsSqlServerTenant = new("ai.knowledge_correction_cleanup.revisions_sqlserver_tenant", Revisions(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement RevisionsMySqlHost = new("ai.knowledge_correction_cleanup.revisions_mysql_host", Revisions(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement RevisionsMySqlTenant = new("ai.knowledge_correction_cleanup.revisions_mysql_tenant", Revisions(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement MarkSourceHost = new("ai.knowledge_correction_cleanup.mark_source_host", Source(false, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement MarkSourceTenant = new("ai.knowledge_correction_cleanup.mark_source_tenant", Source(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement EraseSourceHost = new("ai.knowledge_correction_cleanup.erase_source_host", Source(true, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement EraseSourceTenant = new("ai.knowledge_correction_cleanup.erase_source_tenant", Source(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement MarkRevisionHost = new("ai.knowledge_correction_cleanup.mark_revision_host", Revision(false, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement MarkRevisionTenant = new("ai.knowledge_correction_cleanup.mark_revision_tenant", Revision(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement EraseRevisionHost = new("ai.knowledge_correction_cleanup.erase_revision_host", Revision(true, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement EraseRevisionTenant = new("ai.knowledge_correction_cleanup.erase_revision_tenant", Revision(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement LockFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql
        ? scoped ? LockMySqlTenant : LockMySqlHost : scoped ? LockSqlServerTenant : LockSqlServerHost;
    internal static SqlStatement RevisionsFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql
        ? scoped ? RevisionsMySqlTenant : RevisionsMySqlHost : scoped ? RevisionsSqlServerTenant : RevisionsSqlServerHost;
}
