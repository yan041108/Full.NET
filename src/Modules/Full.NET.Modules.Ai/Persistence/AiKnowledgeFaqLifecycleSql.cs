using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>FAQ 专属审核/发布入口；普通文档策略保持关闭，只有真实修订来源进入固定治理语句。</summary>
internal static class AiKnowledgeFaqLifecycleSql
{
    private const string Host = " AND k.TenantId IS NULL AND f.TenantId IS NULL";
    private const string Tenant = " AND k.TenantId=@TenantId AND f.TenantId=@TenantId";
    private const string Root = " FROM fn_ai_knowledge_faq AS f INNER JOIN fn_ai_knowledge_base AS k ON k.Id=f.KnowledgeBaseId WHERE f.Id=@FaqId AND f.KnowledgeBaseId=@KnowledgeBaseId AND f.ReservedDocumentId=@DocumentId AND k.IsEnabled=1";
    private const string ReleaseRoot = " FROM fn_ai_knowledge_faq_release AS faqRelease INNER JOIN fn_ai_knowledge_faq AS f ON f.Id=faqRelease.FaqId INNER JOIN fn_ai_knowledge_base AS k ON k.Id=f.KnowledgeBaseId WHERE f.Id=@FaqId AND f.KnowledgeBaseId=@KnowledgeBaseId AND f.ReservedDocumentId=@DocumentId AND k.IsEnabled=1";
    private const string ReleaseColumns = "faqRelease.Id,faqRelease.FaqId,faqRelease.RevisionId,faqRelease.VersionId,faqRelease.SourceHash,faqRelease.ContentHash,faqRelease.RequestHash,faqRelease.CreatedAtUtc";
    private static string Scope(bool mysql, bool scoped) => (scoped ? Tenant : Host) + " AND " + AiKnowledgeFaqReadSql.AudienceForRoot(mysql, scoped);
    private static SqlStatement RootRevision(bool mysql, bool scoped) => new("ai.knowledge_faq_lifecycle.revision_" + (mysql ? "mysql" : "sqlserver") + (scoped ? "_tenant" : "_host"),
        (scoped ? AiKnowledgeFaqDraftSql.RevisionTenant : AiKnowledgeFaqDraftSql.RevisionHost).Text.Replace("k.OwnerUserId=@OwnerUserId", AiKnowledgeFaqReadSql.AudienceForRoot(mysql, scoped), StringComparison.Ordinal).Replace("r.Id=@RevisionId", "((@RevisionId IS NULL AND r.Id=f.HeadRevisionId) OR r.Id=@RevisionId)", StringComparison.Ordinal),
        scoped ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, scoped ? SqlTenantBinding.CurrentTenantId : SqlTenantBinding.None);
    private static readonly SqlStatement RevisionSqlServerHost = RootRevision(false, false);
    private static readonly SqlStatement RevisionSqlServerTenant = RootRevision(false, true);
    private static readonly SqlStatement RevisionMySqlHost = RootRevision(true, false);
    private static readonly SqlStatement RevisionMySqlTenant = RootRevision(true, true);
    internal static SqlStatement RevisionFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql ? scoped ? RevisionMySqlTenant : RevisionMySqlHost : scoped ? RevisionSqlServerTenant : RevisionSqlServerHost;

    private static SqlStatement FindRelease(bool mysql, bool scoped) => new("ai.knowledge_faq_lifecycle.release_" + (mysql ? "mysql" : "sqlserver") + (scoped ? "_tenant" : "_host"),
        "SELECT " + ReleaseColumns + ReleaseRoot + " AND faqRelease.Id=@Id" + Scope(mysql, scoped), scoped ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, scoped ? SqlTenantBinding.CurrentTenantId : SqlTenantBinding.None);
    private static readonly SqlStatement ReleaseSqlServerHost = FindRelease(false, false);
    private static readonly SqlStatement ReleaseSqlServerTenant = FindRelease(false, true);
    private static readonly SqlStatement ReleaseMySqlHost = FindRelease(true, false);
    private static readonly SqlStatement ReleaseMySqlTenant = FindRelease(true, true);
    internal static SqlStatement ReleaseFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql ? scoped ? ReleaseMySqlTenant : ReleaseMySqlHost : scoped ? ReleaseSqlServerTenant : ReleaseSqlServerHost;

    private const string CurrentDocument = "EXISTS (SELECT 1 FROM fn_ai_knowledge_version AS version WHERE version.Id=@VersionId AND version.DocumentId=document.Id AND version.FaqRevisionId=@RevisionId AND " + AiKnowledgeFaqIndexSql.CurrentOrigin + ")";
    private static SqlStatement Governed(SqlStatement ordinary, string operation) => ordinary with
    {
        Name = "ai.knowledge_faq_publication." + operation,
        Text = ordinary.Text.Replace(AiKnowledgeOrdinaryDocumentSql.Visible, CurrentDocument, StringComparison.Ordinal)
    };
    internal static readonly SqlStatement InsertHost = Governed(AiKnowledgePublicationSql.InsertHost, "insert_host");
    internal static readonly SqlStatement InsertTenant = Governed(AiKnowledgePublicationSql.InsertTenant, "insert_tenant");
    internal static readonly SqlStatement DecideHost = Governed(AiKnowledgePublicationSql.DecideHost, "decide_host");
    internal static readonly SqlStatement DecideTenant = Governed(AiKnowledgePublicationSql.DecideTenant, "decide_tenant");
    internal static readonly SqlStatement PublishHeadHost = Governed(AiKnowledgePublicationSql.PublishHeadHost, "publish_head_host");
    internal static readonly SqlStatement PublishHeadTenant = Governed(AiKnowledgePublicationSql.PublishHeadTenant, "publish_head_tenant");
    private const string InsertRelease = "INSERT INTO fn_ai_knowledge_faq_release (Id,FaqId,RevisionId,VersionId,SourceHash,ContentHash,RequestHash,CreatedAtUtc) SELECT @Id,f.Id,@RevisionId,@VersionId,f.SourceHash,@ContentHash,@RequestHash,@Now" + Root + " AND f.HeadRevisionId=@RevisionId AND f.HeadVersion=@DocumentVersion AND k.OwnerUserId=@ActorUserId";
    internal static readonly SqlStatement BindHost = new("ai.knowledge_faq_publication.bind_host", InsertRelease + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement BindTenant = new("ai.knowledge_faq_publication.bind_tenant", InsertRelease + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string AdvanceRoot = "UPDATE fn_ai_knowledge_faq SET PublicationId=@Id WHERE Id=@FaqId AND KnowledgeBaseId=@KnowledgeBaseId AND ReservedDocumentId=@DocumentId AND HeadRevisionId=@RevisionId AND HeadVersion=@DocumentVersion AND EXISTS (SELECT 1 FROM fn_ai_knowledge_publication_head AS head WHERE head.Id=@DocumentId AND head.PublicationId=@Id AND head.Version=@ExpectedHeadVersion+1) AND ";
    internal static readonly SqlStatement AdvanceHost = new("ai.knowledge_faq_publication.advance_host", AdvanceRoot + "TenantId IS NULL", SqlDataScope.HostOnly);
    internal static readonly SqlStatement AdvanceTenant = new("ai.knowledge_faq_publication.advance_tenant", AdvanceRoot + "TenantId=@TenantId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string WithdrawRoot = "UPDATE fn_ai_knowledge_faq SET PublicationId=NULL WHERE Id=@FaqId AND KnowledgeBaseId=@KnowledgeBaseId AND ReservedDocumentId=@DocumentId AND PublicationId=@Id AND ";
    internal static readonly SqlStatement WithdrawHost = new("ai.knowledge_faq_publication.withdraw_host", WithdrawRoot + "TenantId IS NULL", SqlDataScope.HostOnly);
    internal static readonly SqlStatement WithdrawTenant = new("ai.knowledge_faq_publication.withdraw_tenant", WithdrawRoot + "TenantId=@TenantId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string Published = "SELECT faqRelease.Id,faqRelease.FaqId,faqRelease.RevisionId,faqRelease.VersionId,faqRelease.SourceHash,faqRelease.ContentHash,faqRelease.RequestHash,faqRelease.CreatedAtUtc" + ReleaseRoot
        + " AND f.PublicationId=faqRelease.Id AND EXISTS (SELECT 1 FROM fn_ai_knowledge_publication_head AS head INNER JOIN fn_ai_knowledge_publication AS publication ON publication.Id=head.PublicationId WHERE head.Id=f.ReservedDocumentId AND head.PublicationId=faqRelease.Id AND publication.DocumentId=head.Id AND publication.VersionId=faqRelease.VersionId AND publication.StateKey='published' AND publication.EffectiveFromUtc<=@Now AND publication.EffectiveToUtc>@Now)";
    private static SqlStatement CurrentRelease(bool mysql, bool scoped) => new("ai.knowledge_faq_lifecycle.published_" + (mysql ? "mysql" : "sqlserver") + (scoped ? "_tenant" : "_host"), Published + Scope(mysql, scoped), scoped ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, scoped ? SqlTenantBinding.CurrentTenantId : SqlTenantBinding.None);
    private static readonly SqlStatement PublishedSqlServerHost = CurrentRelease(false, false);
    private static readonly SqlStatement PublishedSqlServerTenant = CurrentRelease(false, true);
    private static readonly SqlStatement PublishedMySqlHost = CurrentRelease(true, false);
    private static readonly SqlStatement PublishedMySqlTenant = CurrentRelease(true, true);
    internal static SqlStatement PublishedFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql ? scoped ? PublishedMySqlTenant : PublishedMySqlHost : scoped ? PublishedSqlServerTenant : PublishedSqlServerHost;

    private const string DeleteQueries = "DELETE FROM fn_ai_knowledge_faq_query WHERE KnowledgeBaseId=@KnowledgeBaseId AND FaqId=@FaqId AND EXISTS (SELECT 1 FROM fn_ai_knowledge_base AS k WHERE k.Id=@KnowledgeBaseId AND k.OwnerUserId=@ActorUserId AND ";
    internal static readonly SqlStatement DeleteQueriesHost = new("ai.knowledge_faq_query.delete_host", DeleteQueries + "k.TenantId IS NULL)", SqlDataScope.HostOnly);
    internal static readonly SqlStatement DeleteQueriesTenant = new("ai.knowledge_faq_query.delete_tenant", DeleteQueries + "k.TenantId=@TenantId)", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string ClaimQuery = "INSERT INTO fn_ai_knowledge_faq_query (Id,KnowledgeBaseId,FaqId,QueryHash,CreatedAtUtc) SELECT @ClaimId,k.Id,f.Id,@QueryHash,@Now" + Root + " AND k.OwnerUserId=@ActorUserId AND f.HeadRevisionId=@RevisionId";
    internal static readonly SqlStatement ClaimQueryHost = new("ai.knowledge_faq_query.claim_host", ClaimQuery + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ClaimQueryTenant = new("ai.knowledge_faq_query.claim_tenant", ClaimQuery + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement PublishedStateHost = Governed(AiKnowledgePublicationSql.PublishedHost, "published_host");
    internal static readonly SqlStatement PublishedStateTenant = Governed(AiKnowledgePublicationSql.PublishedTenant, "published_tenant");
    internal static readonly SqlStatement OwnedDocumentHost = new("ai.knowledge_faq_lifecycle.owned_document_host", "SELECT f.ReservedDocumentId FROM fn_ai_knowledge_faq AS f INNER JOIN fn_ai_knowledge_base AS k ON k.Id=f.KnowledgeBaseId WHERE f.Id=@FaqId AND f.KnowledgeBaseId=@KnowledgeBaseId AND k.OwnerUserId=@ActorUserId AND k.IsEnabled=1" + Host, SqlDataScope.HostOnly);
    internal static readonly SqlStatement OwnedDocumentTenant = new("ai.knowledge_faq_lifecycle.owned_document_tenant", "SELECT f.ReservedDocumentId FROM fn_ai_knowledge_faq AS f INNER JOIN fn_ai_knowledge_base AS k ON k.Id=f.KnowledgeBaseId WHERE f.Id=@FaqId AND f.KnowledgeBaseId=@KnowledgeBaseId AND k.OwnerUserId=@ActorUserId AND k.IsEnabled=1" + Tenant, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string ExpiredQueries = "DELETE FROM fn_ai_knowledge_faq_query WHERE KnowledgeBaseId=@KnowledgeBaseId AND EXISTS (SELECT 1 FROM fn_ai_knowledge_faq AS f INNER JOIN fn_ai_knowledge_base AS k ON k.Id=f.KnowledgeBaseId LEFT JOIN fn_ai_knowledge_publication AS publication ON publication.Id=f.PublicationId WHERE f.Id=fn_ai_knowledge_faq_query.FaqId AND k.Id=@KnowledgeBaseId AND k.OwnerUserId=@ActorUserId AND (publication.Id IS NULL OR publication.StateKey<>'published' OR publication.EffectiveToUtc<=@Now)";
    internal static readonly SqlStatement ExpireQueriesHost = new("ai.knowledge_faq_query.expire_host", ExpiredQueries + Host + ")", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ExpireQueriesTenant = new("ai.knowledge_faq_query.expire_tenant", ExpiredQueries + Tenant + ")", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private const string Pending = "publication.Id FROM fn_ai_knowledge_faq_release AS faqRelease INNER JOIN fn_ai_knowledge_publication AS publication ON publication.Id=faqRelease.Id INNER JOIN fn_ai_knowledge_faq AS f ON f.Id=faqRelease.FaqId INNER JOIN fn_ai_knowledge_base AS k ON k.Id=f.KnowledgeBaseId WHERE f.Id=@FaqId AND f.KnowledgeBaseId=@KnowledgeBaseId AND k.OwnerUserId=@ActorUserId AND publication.StateKey IN ('pending','approved')";
    internal static readonly SqlStatement PendingSqlServerHost = new("ai.knowledge_faq_lifecycle.pending_sqlserver_host", "SELECT TOP (1) " + Pending + Host + " ORDER BY publication.CreatedAtUtc DESC,publication.Id DESC", SqlDataScope.HostOnly);
    internal static readonly SqlStatement PendingSqlServerTenant = new("ai.knowledge_faq_lifecycle.pending_sqlserver_tenant", "SELECT TOP (1) " + Pending + Tenant + " ORDER BY publication.CreatedAtUtc DESC,publication.Id DESC", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement PendingMySqlHost = new("ai.knowledge_faq_lifecycle.pending_mysql_host", "SELECT " + Pending + Host + " ORDER BY publication.CreatedAtUtc DESC,publication.Id DESC LIMIT 1", SqlDataScope.HostOnly);
    internal static readonly SqlStatement PendingMySqlTenant = new("ai.knowledge_faq_lifecycle.pending_mysql_tenant", "SELECT " + Pending + Tenant + " ORDER BY publication.CreatedAtUtc DESC,publication.Id DESC LIMIT 1", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement PendingFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql ? scoped ? PendingMySqlTenant : PendingMySqlHost : scoped ? PendingSqlServerTenant : PendingSqlServerHost;
}
