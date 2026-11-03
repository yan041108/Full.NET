using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>FAQ 的显式发布分支；普通文件查询及其 FAQ 排除条件保持原样。</summary>
internal static partial class AiKnowledgeRetrievalSql
{
    private const string CandidateProjection = "PublicationId,KnowledgeBaseId,DocumentId,VersionId,OwnerUserId,DocumentVersion,KnowledgeBaseVersion,DataClassification,GenerationModelConfigId,GenerationModelVersion,IsDeleted,CleanupPending,HasDerivedData,TaskId,CorrectionRevisionId,StateKey,IsCurrent,ActiveTaskId,ActiveLeaseToken,ActiveReceiptHash,ActiveManifestHash,ActivePlanHash,FileId,FaqRevisionId,ContentHash,ContentType,ParsingPolicyKey,SizeBytes,ModelConfigId,ModelConfigVersion,ProviderKey,ModelId";
    private const string FaqRelease = "EXISTS (SELECT 1 FROM fn_ai_knowledge_faq AS faq INNER JOIN fn_ai_knowledge_faq_release AS faqRelease ON faqRelease.Id=faq.PublicationId INNER JOIN fn_ai_knowledge_faq_revision AS faqRevision ON faqRevision.Id=faqRelease.RevisionId AND faqRevision.FaqId=faq.Id WHERE faq.KnowledgeBaseId=knowledge.Id AND faq.ReservedDocumentId=document.Id AND faq.PublicationId=publication.Id AND faqRelease.VersionId=version.Id AND faqRelease.RevisionId=version.FaqRevisionId AND faqRelease.SourceHash=faq.SourceHash AND faqRelease.ContentHash=faqRevision.ContentHash)";

    private static string FaqBranch(string ordinary, bool mysql, bool scoped) => ordinary.Replace(
        mysql ? AiKnowledgeReadSql.LayersMySql : AiKnowledgeReadSql.Layers,
        AiKnowledgeFaqReadSql.AudienceForSelection(mysql, scoped) + " AND " + AiKnowledgeFaqIndexSql.Origin + " AND " + FaqRelease
        // ReservedDocumentId 唯一；MySQL 使用单行聚合，避免带私人 JSON 绑定的根关联被 semijoin 改写后误判为空。
        + " AND " + (mysql ? "(SELECT COUNT(*)" : "EXISTS (SELECT 1") + " FROM fn_ai_knowledge_faq AS f WHERE f.ReservedDocumentId=document.Id AND f.KnowledgeBaseId=knowledge.Id AND "
        + (scoped ? "f.TenantId=@TenantId" : "f.TenantId IS NULL") + " AND " + AiKnowledgeFaqDraftSql.ReadableLeaves(mysql, scoped)
        + " AND " + AiKnowledgeFaqParentSql.Current(mysql, scoped)
        + " AND " + AiKnowledgeFaqFeedbackSql.Current(mysql, scoped, "f.SourceJson", "f.OriginKey", "knowledge.DataClassification") + (mysql ? ")=1" : ")"), StringComparison.Ordinal);

    // 统一的上限加一作用于两类候选之和，不能每个分支各取八项后静默丢弃另一类。
    private static string CombinedSources(bool mysql, bool scoped, bool search)
    {
        var ordinary = SourceText(mysql, scoped, search).Replace("TOP (9) ", "", StringComparison.Ordinal)
            .Replace(" ORDER BY document.Id" + (mysql ? " LIMIT 9" : ""), "", StringComparison.Ordinal);
        return "SELECT " + (mysql ? "" : "TOP (9) ") + CandidateProjection + " FROM (" + ordinary + " UNION ALL " + FaqBranch(ordinary, mysql, scoped)
            + ") AS candidates ORDER BY DocumentId" + (mysql ? " LIMIT 9" : "");
    }
    private static readonly SqlStatement CombinedSqlServerHost = new("ai.knowledge_retrieval.combined_sqlserver_host", CombinedSources(false, false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement CombinedSqlServerTenant = new("ai.knowledge_retrieval.combined_sqlserver_tenant", CombinedSources(false, true, false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement CombinedMySqlHost = new("ai.knowledge_retrieval.combined_mysql_host", CombinedSources(true, false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement CombinedMySqlTenant = new("ai.knowledge_retrieval.combined_mysql_tenant", CombinedSources(true, true, false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement CombinedSearchSqlServerHost = new("ai.knowledge_search.combined_sqlserver_host", CombinedSources(false, false, true), SqlDataScope.HostOnly);
    private static readonly SqlStatement CombinedSearchSqlServerTenant = new("ai.knowledge_search.combined_sqlserver_tenant", CombinedSources(false, true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement CombinedSearchMySqlHost = new("ai.knowledge_search.combined_mysql_host", CombinedSources(true, false, true), SqlDataScope.HostOnly);
    private static readonly SqlStatement CombinedSearchMySqlTenant = new("ai.knowledge_search.combined_mysql_tenant", CombinedSources(true, true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement CombinedSourcesFor(DatabaseProvider provider, bool scoped, bool search) => provider == DatabaseProvider.MySql
        ? search ? scoped ? CombinedSearchMySqlTenant : CombinedSearchMySqlHost : scoped ? CombinedMySqlTenant : CombinedMySqlHost
        : search ? scoped ? CombinedSearchSqlServerTenant : CombinedSearchSqlServerHost : scoped ? CombinedSqlServerTenant : CombinedSqlServerHost;

    private static readonly SqlStatement FaqVectorsSqlServerHost = new("ai.knowledge_retrieval.faq_vectors_sqlserver_host", FaqBranch(VectorText(false, false), false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement FaqVectorsSqlServerTenant = new("ai.knowledge_retrieval.faq_vectors_sqlserver_tenant", FaqBranch(VectorText(false, true), false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement FaqVectorsMySqlHost = new("ai.knowledge_retrieval.faq_vectors_mysql_host", FaqBranch(VectorText(true, false), true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement FaqVectorsMySqlTenant = new("ai.knowledge_retrieval.faq_vectors_mysql_tenant", FaqBranch(VectorText(true, true), true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement FaqDocumentsSqlServerHost = new("ai.knowledge_search.faq_documents_sqlserver_host", FaqBranch(SearchDocumentText(false, false), false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement FaqDocumentsSqlServerTenant = new("ai.knowledge_search.faq_documents_sqlserver_tenant", FaqBranch(SearchDocumentText(false, true), false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement FaqDocumentsMySqlHost = new("ai.knowledge_search.faq_documents_mysql_host", FaqBranch(SearchDocumentText(true, false), true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement FaqDocumentsMySqlTenant = new("ai.knowledge_search.faq_documents_mysql_tenant", FaqBranch(SearchDocumentText(true, true), true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static SqlStatement FaqVectorsFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql ? scoped ? FaqVectorsMySqlTenant : FaqVectorsMySqlHost : scoped ? FaqVectorsSqlServerTenant : FaqVectorsSqlServerHost;
    internal static SqlStatement FaqDocumentsFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql ? scoped ? FaqDocumentsMySqlTenant : FaqDocumentsMySqlHost : scoped ? FaqDocumentsSqlServerTenant : FaqDocumentsSqlServerHost;
    private static string CombinedHistory(bool mysql, bool scoped) => AiKnowledgePublishedSourceSql.History(mysql, scoped) + " UNION ALL " + FaqBranch(AiKnowledgePublishedSourceSql.History(mysql, scoped), mysql, scoped);
    private static readonly SqlStatement CombinedHistorySqlServerHost = new("ai.knowledge_history.combined_sqlserver_host", CombinedHistory(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement CombinedHistorySqlServerTenant = new("ai.knowledge_history.combined_sqlserver_tenant", CombinedHistory(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement CombinedHistoryMySqlHost = new("ai.knowledge_history.combined_mysql_host", CombinedHistory(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement CombinedHistoryMySqlTenant = new("ai.knowledge_history.combined_mysql_tenant", CombinedHistory(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement CombinedHistoryFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql ? scoped ? CombinedHistoryMySqlTenant : CombinedHistoryMySqlHost : scoped ? CombinedHistorySqlServerTenant : CombinedHistorySqlServerHost;
    internal static readonly SqlStatement FaqDocumentIdHost = new("ai.knowledge_faq_source.document_identity_host", "SELECT f.Id FROM fn_ai_knowledge_faq AS f WHERE f.ReservedDocumentId=@DocumentId AND f.KnowledgeBaseId=@KnowledgeBaseId AND f.TenantId IS NULL", SqlDataScope.HostOnly);
    internal static readonly SqlStatement FaqDocumentIdTenant = new("ai.knowledge_faq_source.document_identity_tenant", "SELECT f.Id FROM fn_ai_knowledge_faq AS f WHERE f.ReservedDocumentId=@DocumentId AND f.KnowledgeBaseId=@KnowledgeBaseId AND f.TenantId=@TenantId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    private static string FaqFinal(bool mysql, bool scoped) => AiKnowledgeFaqDraftSql.SharedCurrentFor(mysql ? DatabaseProvider.MySql : DatabaseProvider.SqlServer, scoped).Text
        + " AND f.Id=@FaqId AND f.PublicationId=@PublicationId AND EXISTS (SELECT 1 FROM fn_ai_knowledge_faq_release AS faqRelease INNER JOIN fn_ai_knowledge_publication AS publication ON publication.Id=faqRelease.Id INNER JOIN fn_ai_knowledge_publication_head AS head ON head.PublicationId=publication.Id AND head.Id=f.ReservedDocumentId WHERE faqRelease.Id=@PublicationId AND faqRelease.FaqId=f.Id AND faqRelease.RevisionId=@RevisionId AND faqRelease.VersionId=@VersionId AND publication.GenerationId=@TaskId AND publication.StateKey='published' AND publication.EffectiveFromUtc<=@Now AND publication.EffectiveToUtc>@Now)"
        + " AND EXISTS (SELECT 1 FROM fn_ai_model_config AS approved WHERE approved.Id=@FaqModelId AND approved.Version=@FaqModelVersion AND approved.IsEnabled=1"
        + " AND ((approved.TenantId IS NULL AND @FaqModelTenantId IS NULL) OR approved.TenantId=@FaqModelTenantId)"
        + " AND " + Exact(mysql, "approved.EndpointBaseUrl", "FaqEndpoint") + " AND " + Exact(mysql, "approved.OrganizationId", "FaqOrganization")
        + " AND " + Exact(mysql, "approved.ProviderKey", "FaqProvider") + " AND " + Exact(mysql, "approved.ModelId", "FaqModel")
        + " AND " + Exact(mysql, "approved.ApiKeyProtected", "FaqProtectedKey") + ")";
    private static readonly SqlStatement FaqFinalSqlServerHost = new("ai.knowledge_faq_source.final_sqlserver_host", FaqFinal(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement FaqFinalSqlServerTenant = new("ai.knowledge_faq_source.final_sqlserver_tenant", FaqFinal(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement FaqFinalMySqlHost = new("ai.knowledge_faq_source.final_mysql_host", FaqFinal(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement FaqFinalMySqlTenant = new("ai.knowledge_faq_source.final_mysql_tenant", FaqFinal(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement FaqFinalFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql ? scoped ? FaqFinalMySqlTenant : FaqFinalMySqlHost : scoped ? FaqFinalSqlServerTenant : FaqFinalSqlServerHost;
}
