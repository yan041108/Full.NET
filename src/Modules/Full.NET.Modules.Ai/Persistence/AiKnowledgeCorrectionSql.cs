using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>校对持久状态只访问 AI 自有表；读取与写入均绑定原文档、所有者和可信租户。</summary>
internal static class AiKnowledgeCorrectionSql
{
    private const string Root = " INNER JOIN fn_ai_knowledge_version AS version ON version.Id=@VersionId INNER JOIN fn_ai_knowledge_document AS document ON document.Id=version.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId";
    private const string Visible = " AND " + AiKnowledgeOrdinaryDocumentSql.Visible + " AND document.Id=@DocumentId AND knowledge.Id=@KnowledgeBaseId AND knowledge.OwnerUserId=@OwnerUserId AND document.IsDeleted=0 AND knowledge.IsEnabled=1";
    private const string Host = " AND knowledge.TenantId IS NULL";
    private const string Tenant = " AND knowledge.TenantId=@TenantId";
    private const string Latest = " AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS newer WHERE newer.DocumentId=document.Id AND newer.VersionNumber>version.VersionNumber)";
    private const string Current = Visible + Latest + " AND document.Version=@DocumentVersion";
    private const string SourceColumns = "source.Id,source.VersionId,source.RequestKey,source.RequestHash,source.FileHash,source.ParserRuntimeId,source.SourceHash,source.OriginalPayload,source.InitialPayload,source.InitialPayloadHash,source.ActorUserId,source.CreatedAtUtc";
    private const string RevisionColumns = "revision.Id,revision.VersionId,revision.SourceId,revision.ParentId,revision.RequestKey,revision.RequestHash,revision.Payload,revision.PayloadHash,revision.ReasonText,revision.ActorUserId,revision.CreatedAtUtc,revision.DocumentVersion,revision.HeadVersion";
    private const string SourceRoot = " FROM fn_ai_knowledge_correction_source AS source" + Root + " WHERE source.VersionId=version.Id AND source.CleanupState=0" + Visible;
    private const string RevisionRoot = " FROM fn_ai_knowledge_correction_revision AS revision" + Root + " WHERE revision.VersionId=version.Id AND revision.CleanupState=0" + Visible;
    private static string Source(bool mysql, string identity) => "SELECT " + SourceColumns + SourceRoot + " AND " + identity
        + (mysql ? " AND OCTET_LENGTH(source.OriginalPayload) BETWEEN 2 AND 16777216 AND OCTET_LENGTH(source.InitialPayload) BETWEEN 2 AND 12582912"
            : " AND DATALENGTH(source.OriginalPayload) BETWEEN 2 AND 16777216 AND DATALENGTH(source.InitialPayload) BETWEEN 2 AND 12582912");
    private static string Revision(bool mysql, string identity) => "SELECT " + RevisionColumns + RevisionRoot + " AND " + identity
        + (mysql ? " AND OCTET_LENGTH(revision.Payload) BETWEEN 2 AND 12582912" : " AND DATALENGTH(revision.Payload) BETWEEN 2 AND 12582912");

    private static readonly SqlStatement SourceSqlServerHost = new("ai.knowledge_correction.source_sqlserver_host", Source(false, "source.Id=@SourceId") + Host, SqlDataScope.HostOnly);
    private static readonly SqlStatement SourceSqlServerTenant = new("ai.knowledge_correction.source_sqlserver_tenant", Source(false, "source.Id=@SourceId") + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement SourceMySqlHost = new("ai.knowledge_correction.source_mysql_host", Source(true, "source.Id=@SourceId") + Host, SqlDataScope.HostOnly);
    private static readonly SqlStatement SourceMySqlTenant = new("ai.knowledge_correction.source_mysql_tenant", Source(true, "source.Id=@SourceId") + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement SourceKeySqlServerHost = new("ai.knowledge_correction.source_key_sqlserver_host", Source(false, "source.RequestKey=@RequestKey") + Host, SqlDataScope.HostOnly);
    private static readonly SqlStatement SourceKeySqlServerTenant = new("ai.knowledge_correction.source_key_sqlserver_tenant", Source(false, "source.RequestKey=@RequestKey") + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement SourceKeyMySqlHost = new("ai.knowledge_correction.source_key_mysql_host", Source(true, "source.RequestKey=@RequestKey") + Host, SqlDataScope.HostOnly);
    private static readonly SqlStatement SourceKeyMySqlTenant = new("ai.knowledge_correction.source_key_mysql_tenant", Source(true, "source.RequestKey=@RequestKey") + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement RevisionSqlServerHost = new("ai.knowledge_correction.revision_sqlserver_host", Revision(false, "revision.Id=@RevisionId") + Host, SqlDataScope.HostOnly);
    private static readonly SqlStatement RevisionSqlServerTenant = new("ai.knowledge_correction.revision_sqlserver_tenant", Revision(false, "revision.Id=@RevisionId") + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement RevisionMySqlHost = new("ai.knowledge_correction.revision_mysql_host", Revision(true, "revision.Id=@RevisionId") + Host, SqlDataScope.HostOnly);
    private static readonly SqlStatement RevisionMySqlTenant = new("ai.knowledge_correction.revision_mysql_tenant", Revision(true, "revision.Id=@RevisionId") + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement RevisionKeySqlServerHost = new("ai.knowledge_correction.revision_key_sqlserver_host", Revision(false, "revision.RequestKey=@RequestKey") + Host, SqlDataScope.HostOnly);
    private static readonly SqlStatement RevisionKeySqlServerTenant = new("ai.knowledge_correction.revision_key_sqlserver_tenant", Revision(false, "revision.RequestKey=@RequestKey") + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement RevisionKeyMySqlHost = new("ai.knowledge_correction.revision_key_mysql_host", Revision(true, "revision.RequestKey=@RequestKey") + Host, SqlDataScope.HostOnly);
    private static readonly SqlStatement RevisionKeyMySqlTenant = new("ai.knowledge_correction.revision_key_mysql_tenant", Revision(true, "revision.RequestKey=@RequestKey") + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string Head = "SELECT head.Id,head.SourceId,head.RevisionId,head.Version FROM fn_ai_knowledge_correction_head AS head" + Root + " WHERE head.Id=version.Id" + Visible;
    internal static readonly SqlStatement HeadHost = new("ai.knowledge_correction.head_host", Head + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement HeadTenant = new("ai.knowledge_correction.head_tenant", Head + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string Version = "SELECT version.Id,version.DocumentId,version.FileId,version.VersionNumber,version.OriginalFileName,version.ContentType,version.ParsingPolicyKey,version.ContentHash,version.SizeBytes,version.UploadedByUserId,version.CreatedAtUtc,'pending_processing' AS ProcessingStatus FROM fn_ai_knowledge_version AS version INNER JOIN fn_ai_knowledge_document AS document ON document.Id=version.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE version.Id=@VersionId" + Current;
    internal static readonly SqlStatement VersionHost = new("ai.knowledge_correction.version_host", Version + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement VersionTenant = new("ai.knowledge_correction.version_tenant", Version + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string InsertSource = "INSERT INTO fn_ai_knowledge_correction_source (Id,VersionId,RequestKey,RequestHash,FileHash,ParserRuntimeId,SourceHash,OriginalPayload,InitialPayload,InitialPayloadHash,ActorUserId,CreatedAtUtc,CleanupState) SELECT @SourceId,version.Id,@RequestKey,@RequestHash,@FileHash,@ParserRuntimeId,@SourceHash,@OriginalPayload,@InitialPayload,@InitialPayloadHash,@OwnerUserId,@Now,0 FROM fn_ai_knowledge_version AS version INNER JOIN fn_ai_knowledge_document AS document ON document.Id=version.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE version.Id=@VersionId AND version.ContentHash=@FileHash" + Current;
    internal static readonly SqlStatement InsertSourceHost = new("ai.knowledge_correction.insert_source_host", InsertSource + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertSourceTenant = new("ai.knowledge_correction.insert_source_tenant", InsertSource + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string Initialize = "INSERT INTO fn_ai_knowledge_correction_head (Id,SourceId,RevisionId,Version,UpdatedAtUtc) SELECT version.Id,source.Id,NULL,0,@Now FROM fn_ai_knowledge_correction_source AS source" + Root + " WHERE source.Id=@SourceId AND source.VersionId=version.Id AND source.CleanupState=0 AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_correction_head WHERE Id=version.Id)" + Current;
    internal static readonly SqlStatement InitializeHost = new("ai.knowledge_correction.initialize_host", Initialize + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InitializeTenant = new("ai.knowledge_correction.initialize_tenant", Initialize + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string InsertRevision = "INSERT INTO fn_ai_knowledge_correction_revision (Id,VersionId,SourceId,ParentId,RequestKey,RequestHash,Payload,PayloadHash,ReasonText,ActorUserId,CreatedAtUtc,DocumentVersion,HeadVersion,CleanupState) SELECT @RevisionId,version.Id,source.Id,head.RevisionId,@RequestKey,@RequestHash,@Payload,@PayloadHash,@ReasonText,@OwnerUserId,@Now,@DocumentVersion+1,@HeadVersion+1,0 FROM fn_ai_knowledge_correction_head AS head INNER JOIN fn_ai_knowledge_correction_source AS source ON source.Id=@SourceId AND source.VersionId=head.Id" + Root + " WHERE head.Id=version.Id AND head.Version=@HeadVersion AND source.CleanupState=0 AND source.SourceHash=@SourceHash" + Current;
    internal static readonly SqlStatement InsertRevisionHost = new("ai.knowledge_correction.insert_revision_host", InsertRevision + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertRevisionTenant = new("ai.knowledge_correction.insert_revision_tenant", InsertRevision + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string Advance = "UPDATE fn_ai_knowledge_correction_head SET SourceId=@SourceId,RevisionId=@RevisionId,Version=Version+1,UpdatedAtUtc=@Now WHERE Id=@VersionId AND Version=@HeadVersion AND EXISTS (SELECT 1 FROM fn_ai_knowledge_correction_revision AS revision" + Root + " WHERE revision.Id=@RevisionId AND revision.VersionId=version.Id AND revision.SourceId=@SourceId AND revision.HeadVersion=@HeadVersion+1 AND revision.DocumentVersion=@DocumentVersion+1" + Current;
    internal static readonly SqlStatement AdvanceHost = new("ai.knowledge_correction.advance_host", Advance + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement AdvanceTenant = new("ai.knowledge_correction.advance_tenant", Advance + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string Touch = "UPDATE fn_ai_knowledge_document SET Version=Version+1,UpdatedAtUtc=@Now WHERE Id=@DocumentId AND Version=@DocumentVersion AND IsDeleted=0 AND EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS version INNER JOIN fn_ai_knowledge_correction_head AS head ON head.Id=version.Id INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=fn_ai_knowledge_document.KnowledgeBaseId WHERE version.Id=@VersionId AND version.DocumentId=fn_ai_knowledge_document.Id AND head.RevisionId=@RevisionId AND head.Version=@HeadVersion+1 AND knowledge.Id=@KnowledgeBaseId AND knowledge.OwnerUserId=@OwnerUserId AND knowledge.IsEnabled=1";
    internal static readonly SqlStatement TouchHost = new("ai.knowledge_correction.touch_host", Touch + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement TouchTenant = new("ai.knowledge_correction.touch_tenant", Touch + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    private const string HistoryColumns = "revision.Id,revision.VersionId,revision.SourceId,revision.ParentId,revision.HeadVersion,revision.DocumentVersion,revision.PayloadHash,revision.ReasonText,revision.CreatedAtUtc";
    private const string HistoryRoot = " FROM fn_ai_knowledge_correction_revision AS revision" + Root + " WHERE revision.VersionId=version.Id AND revision.CleanupState=0 AND revision.HeadVersion<@BeforeHeadVersion" + Visible;
    private static readonly SqlStatement HistorySqlServerHost = new("ai.knowledge_correction.history_sqlserver_host", "SELECT TOP (21) " + HistoryColumns + HistoryRoot + Host + " ORDER BY revision.HeadVersion DESC", SqlDataScope.HostOnly);
    private static readonly SqlStatement HistorySqlServerTenant = new("ai.knowledge_correction.history_sqlserver_tenant", "SELECT TOP (21) " + HistoryColumns + HistoryRoot + Tenant + " ORDER BY revision.HeadVersion DESC", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement HistoryMySqlHost = new("ai.knowledge_correction.history_mysql_host", "SELECT " + HistoryColumns + HistoryRoot + Host + " ORDER BY revision.HeadVersion DESC LIMIT 21", SqlDataScope.HostOnly);
    private static readonly SqlStatement HistoryMySqlTenant = new("ai.knowledge_correction.history_mysql_tenant", "SELECT " + HistoryColumns + HistoryRoot + Tenant + " ORDER BY revision.HeadVersion DESC LIMIT 21", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement HistoryFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql
        ? scoped ? HistoryMySqlTenant : HistoryMySqlHost : scoped ? HistorySqlServerTenant : HistorySqlServerHost;

    internal static SqlStatement SourceFor(DatabaseProvider provider, bool scoped, bool key = false) => provider == DatabaseProvider.MySql
        ? key ? scoped ? SourceKeyMySqlTenant : SourceKeyMySqlHost : scoped ? SourceMySqlTenant : SourceMySqlHost
        : key ? scoped ? SourceKeySqlServerTenant : SourceKeySqlServerHost : scoped ? SourceSqlServerTenant : SourceSqlServerHost;
    internal static SqlStatement RevisionFor(DatabaseProvider provider, bool scoped, bool key = false) => provider == DatabaseProvider.MySql
        ? key ? scoped ? RevisionKeyMySqlTenant : RevisionKeyMySqlHost : scoped ? RevisionMySqlTenant : RevisionMySqlHost
        : key ? scoped ? RevisionKeySqlServerTenant : RevisionKeySqlServerHost : scoped ? RevisionSqlServerTenant : RevisionSqlServerHost;
}
