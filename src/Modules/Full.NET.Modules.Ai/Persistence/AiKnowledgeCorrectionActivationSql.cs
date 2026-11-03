using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>人工代次共用 K01 根锁和归档，写入核对现行人工头，已完成归档独立于较新草稿。</summary>
internal static class AiKnowledgeCorrectionActivationSql
{
    private const string Identity = " AND task.CorrectionSourceId=@CorrectionSourceId AND task.CorrectionRevisionId=@CorrectionRevisionId AND task.CorrectionHeadVersion=@CorrectionHeadVersion AND task.CorrectionSourceHash=@CorrectionSourceHash AND task.CorrectionPayloadHash=@CorrectionPayloadHash";
    // 与来源准备使用同一完整版本投影；处理状态不参与归档证明，但静态物化器不能缺列或依赖 JIT 默认值。
    private const string Metadata = "SELECT version.Id,version.DocumentId,version.FileId,version.VersionNumber,version.OriginalFileName,version.ContentType,version.ParsingPolicyKey,version.ContentHash,version.SizeBytes,version.UploadedByUserId,version.CreatedAtUtc,'pending_processing' AS ProcessingStatus FROM fn_ai_knowledge_version AS version INNER JOIN fn_ai_knowledge_index_task AS task ON task.VersionId=version.Id AND task.DocumentId=version.DocumentId INNER JOIN fn_ai_knowledge_document AS document ON document.Id=task.DocumentId INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId WHERE task.Id=@TaskId AND task.DocumentId=@DocumentId AND task.VersionId=@VersionId AND document.KnowledgeBaseId=@KnowledgeBaseId AND knowledge.OwnerUserId=@OwnerUserId AND task.CorrectionRevisionId IS NOT NULL";
    private const string Host = " AND knowledge.TenantId IS NULL";
    private const string Tenant = " AND knowledge.TenantId=@TenantId";
    private static string Append(string sql, string guard) => sql.EndsWith(" FOR UPDATE", StringComparison.Ordinal)
        ? sql[..^11] + guard + " FOR UPDATE" : sql + guard;
    private static string Authority(SqlStatement basis, bool mysql)
    {
        var actualOriginal = mysql ? "CAST(UPPER(SHA2(origin.OriginalPayload,256)) AS BINARY)=CAST(@OriginalPayloadHash AS BINARY)"
            : "CONVERT(varchar(64),HASHBYTES('SHA2_256',origin.OriginalPayload),2) COLLATE Latin1_General_100_BIN2=@OriginalPayloadHash";
        var actualInitial = mysql ? "CAST(LOWER(SHA2(origin.InitialPayload,256)) AS BINARY)=CAST(origin.InitialPayloadHash AS BINARY)"
            : "LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',origin.InitialPayload),2)) COLLATE Latin1_General_100_BIN2=origin.InitialPayloadHash";
        var actualRevision = mysql ? "CAST(LOWER(SHA2(revision.Payload,256)) AS BINARY)=CAST(task.CorrectionPayloadHash AS BINARY)"
            : "LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',revision.Payload),2)) COLLATE Latin1_General_100_BIN2=task.CorrectionPayloadHash";
        var size = mysql ? "OCTET_LENGTH" : "DATALENGTH";
        var frozen = " AND EXISTS (SELECT 1 FROM fn_ai_knowledge_correction_source AS origin INNER JOIN fn_ai_knowledge_correction_revision AS revision ON revision.SourceId=origin.Id AND revision.VersionId=origin.VersionId WHERE origin.Id=task.CorrectionSourceId AND origin.VersionId=task.VersionId AND revision.Id=task.CorrectionRevisionId AND origin.ParserRuntimeId=@ParserRuntimeId AND origin.CleanupState=0 AND revision.CleanupState=0 AND "
            + size + "(origin.OriginalPayload) BETWEEN 2 AND 16777216 AND " + size + "(origin.InitialPayload) BETWEEN 2 AND 12582912 AND " + size + "(revision.Payload) BETWEEN 2 AND 12582912 AND " + actualOriginal + " AND " + actualInitial + " AND " + actualRevision + ")";
        var live = AiKnowledgeIndexCorrectionSql.Live.Replace("fn_ai_knowledge_index_task", "task", StringComparison.Ordinal);
        var grant = " AND NOT EXISTS (SELECT 1 FROM fn_ai_index_grant_item AS granted INNER JOIN fn_ai_index_grant AS grant_row ON grant_row.Id=granted.GrantId WHERE granted.TaskId=task.Id AND (grant_row.IsRevoked=1 OR grant_row.ExpiresAtUtc<=@Now))";
        var text = basis.Text.Replace("task.CorrectionRevisionId IS NULL AND", "task.CorrectionRevisionId IS NOT NULL AND", StringComparison.Ordinal)
            .Replace("NULL AS GrantExpiresAtUtc", "(SELECT MIN(ending.ExpiresAtUtc) FROM fn_ai_index_grant_item AS granted INNER JOIN fn_ai_index_grant AS ending ON ending.Id=granted.GrantId WHERE granted.TaskId=task.Id) AS GrantExpiresAtUtc", StringComparison.Ordinal);
        // 完成态只核对冻结身份和归档清单；原文/修订正文以后可按保留边界清理。
        return Append(text, Identity + " AND (@Completed=1 OR (1=1" + live + frozen + grant + "))");
    }
    private static string Manifest(SqlStatement basis, bool mysql) => basis.Text.Replace("manifest.Payload,manifest.CreatedAtUtc",
        "CASE WHEN " + (mysql ? "OCTET_LENGTH" : "DATALENGTH") + "(manifest.Payload) BETWEEN 8 AND 16777215 THEN manifest.Payload ELSE NULL END AS Payload,manifest.CreatedAtUtc", StringComparison.Ordinal);
    private static string LockedManifest(SqlStatement basis, bool mysql)
    {
        var hash = mysql ? "CAST(manifest.PayloadHash AS BINARY)=CAST(@ManifestHash AS BINARY) AND CAST(manifest.PayloadHash AS BINARY)=CAST(UPPER(SHA2(manifest.Payload,256)) AS BINARY)"
            : "manifest.PayloadHash COLLATE Latin1_General_100_BIN2=@ManifestHash AND manifest.PayloadHash COLLATE Latin1_General_100_BIN2=CONVERT(varchar(64),HASHBYTES('SHA2_256',manifest.Payload),2)";
        var from = basis.Text.IndexOf(" FROM ", StringComparison.Ordinal);
        var sql = "SELECT manifest.Id" + basis.Text[from..] + " AND manifest.FormatKey='knowledge-correction-manifest-v1' AND "
            + (mysql ? "OCTET_LENGTH" : "DATALENGTH") + "(manifest.FormatKey)=32 AND " + (mysql ? "OCTET_LENGTH" : "DATALENGTH") + "(manifest.Payload) BETWEEN 8 AND 16777215 AND " + hash;
        return sql + (mysql ? " FOR UPDATE" : "");
    }
    private static string GrantLock(bool mysql, bool tenant) => "SELECT grant_row.Id FROM fn_ai_index_grant AS grant_row"
        + (mysql ? "" : " WITH(UPDLOCK,HOLDLOCK)") + " INNER JOIN fn_ai_index_grant_item AS granted ON granted.GrantId=grant_row.Id WHERE granted.TaskId=@TaskId AND grant_row.KnowledgeBaseId=@KnowledgeBaseId AND grant_row.OwnerUserId=@OwnerUserId AND "
        + (tenant ? "grant_row.TenantId=@TenantId" : "grant_row.TenantId IS NULL") + (mysql ? " FOR UPDATE" : "");
    internal static readonly SqlStatement MetadataHost = new("ai.knowledge_correction_activation.metadata_host", Metadata + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement MetadataTenant = new("ai.knowledge_correction_activation.metadata_tenant", Metadata + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement AuthoritySqlServerHost = new("ai.knowledge_correction_activation.authority_sqlserver_host", Authority(AiKnowledgeIndexDocumentActivationSql.AuthoritySqlServerHost, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement AuthoritySqlServerTenant = new("ai.knowledge_correction_activation.authority_sqlserver_tenant", Authority(AiKnowledgeIndexDocumentActivationSql.AuthoritySqlServerTenant, false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement AuthorityMySqlHost = new("ai.knowledge_correction_activation.authority_mysql_host", Authority(AiKnowledgeIndexDocumentActivationSql.AuthorityMySqlHost, true), SqlDataScope.HostOnly);
    internal static readonly SqlStatement AuthorityMySqlTenant = new("ai.knowledge_correction_activation.authority_mysql_tenant", Authority(AiKnowledgeIndexDocumentActivationSql.AuthorityMySqlTenant, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ManifestSqlServerHost = new("ai.knowledge_correction_activation.manifest_sqlserver_host", Manifest(AiKnowledgeIndexDocumentActivationSql.ManifestHost, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ManifestSqlServerTenant = new("ai.knowledge_correction_activation.manifest_sqlserver_tenant", Manifest(AiKnowledgeIndexDocumentActivationSql.ManifestTenant, false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ManifestMySqlHost = new("ai.knowledge_correction_activation.manifest_mysql_host", Manifest(AiKnowledgeIndexDocumentActivationSql.ManifestHost, true), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ManifestMySqlTenant = new("ai.knowledge_correction_activation.manifest_mysql_tenant", Manifest(AiKnowledgeIndexDocumentActivationSql.ManifestTenant, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ManifestHashSqlServerHost = new("ai.knowledge_correction_activation.manifest_hash_sqlserver_host", LockedManifest(AiKnowledgeIndexDocumentActivationSql.ManifestHost, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ManifestHashSqlServerTenant = new("ai.knowledge_correction_activation.manifest_hash_sqlserver_tenant", LockedManifest(AiKnowledgeIndexDocumentActivationSql.ManifestTenant, false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ManifestHashMySqlHost = new("ai.knowledge_correction_activation.manifest_hash_mysql_host", LockedManifest(AiKnowledgeIndexDocumentActivationSql.ManifestHost, true), SqlDataScope.HostOnly);
    internal static readonly SqlStatement ManifestHashMySqlTenant = new("ai.knowledge_correction_activation.manifest_hash_mysql_tenant", LockedManifest(AiKnowledgeIndexDocumentActivationSql.ManifestTenant, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement GrantSqlServerHost = new("ai.knowledge_correction_activation.grant_sqlserver_host", GrantLock(false, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement GrantSqlServerTenant = new("ai.knowledge_correction_activation.grant_sqlserver_tenant", GrantLock(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement GrantMySqlHost = new("ai.knowledge_correction_activation.grant_mysql_host", GrantLock(true, false), SqlDataScope.HostOnly);
    internal static readonly SqlStatement GrantMySqlTenant = new("ai.knowledge_correction_activation.grant_mysql_tenant", GrantLock(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CompleteHost = new("ai.knowledge_correction_activation.complete_host", AiKnowledgeIndexActivationSql.CompleteHost.Text.Replace("AND CorrectionRevisionId IS NULL AND StateKey", "AND CorrectionRevisionId IS NOT NULL AND StateKey", StringComparison.Ordinal), SqlDataScope.HostOnly);
    internal static readonly SqlStatement CompleteTenant = new("ai.knowledge_correction_activation.complete_tenant", AiKnowledgeIndexActivationSql.CompleteTenant.Text.Replace("AND CorrectionRevisionId IS NULL AND StateKey", "AND CorrectionRevisionId IS NOT NULL AND StateKey", StringComparison.Ordinal), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement MetadataFor(bool tenant) => tenant ? MetadataTenant : MetadataHost;
    internal static SqlStatement AuthorityFor(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql ? tenant ? AuthorityMySqlTenant : AuthorityMySqlHost : tenant ? AuthoritySqlServerTenant : AuthoritySqlServerHost;
    internal static SqlStatement ManifestFor(DatabaseProvider provider, bool tenant, bool hash = false) => provider == DatabaseProvider.MySql
        ? hash ? tenant ? ManifestHashMySqlTenant : ManifestHashMySqlHost : tenant ? ManifestMySqlTenant : ManifestMySqlHost
        : hash ? tenant ? ManifestHashSqlServerTenant : ManifestHashSqlServerHost : tenant ? ManifestSqlServerTenant : ManifestSqlServerHost;
    internal static SqlStatement GrantFor(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql ? tenant ? GrantMySqlTenant : GrantMySqlHost : tenant ? GrantSqlServerTenant : GrantSqlServerHost;
}
