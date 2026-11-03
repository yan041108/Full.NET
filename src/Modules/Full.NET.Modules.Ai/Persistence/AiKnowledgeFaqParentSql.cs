using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>根的最后一次读取联合核证全部父发布、代次与批准目的地；只读本模块，不调用外部授权或拼接输入。</summary>
internal static class AiKnowledgeFaqParentSql
{
    internal static string Current(bool mysql, bool scoped)
    {
        var definitions = new List<string>();
        string Field(string name, string path, string sqlType, string mysqlType)
        {
            definitions.Add(name + (mysql ? " " + mysqlType + " PATH '$." : " " + sqlType + " '$.") + path + "'");
            return mysql && sqlType == "uniqueidentifier" ? "UUID_TO_BIN(parentBinding." + name + ",0)" : "parentBinding." + name;
        }
        var matches = new List<string>();
        matches.Add("source.IsCurrent=1 AND source.StateKey='completed' AND source.HasDerivedData=1 AND source.IsDeleted=0 AND source.CleanupPending=0 AND source.ActiveTaskId=source.TaskId");
        matches.Add("source.ActiveLeaseToken=" + Field("LeaseToken", "node.status.activeLeaseToken", "uniqueidentifier", "CHAR(36)"));
        foreach (var name in new[] { "KnowledgeBaseId", "DocumentId", "VersionId", "PublicationId", "TaskId" })
            matches.Add("source." + name + "=" + Field(name, "node.status." + char.ToLowerInvariant(name[0]) + name[1..], "uniqueidentifier", "CHAR(36)"));
        foreach (var name in new[] { "DocumentVersion", "KnowledgeBaseVersion" })
            matches.Add("source." + name + "=" + Field(name, "node.status." + char.ToLowerInvariant(name[0]) + name[1..], "int", "INT"));
        foreach (var name in new[] { "ContentHash", "ActiveReceiptHash", "ActiveManifestHash", "ActivePlanHash" })
            matches.Add("source." + name + "=" + Field(name, "node.status." + char.ToLowerInvariant(name[0]) + name[1..], "varchar(64)", "VARCHAR(64)"));
        matches.Add("managed.Id=" + Field("FaqId", "node.source.faqId", "uniqueidentifier", "CHAR(36)"));
        matches.Add("source.FaqRevisionId=" + Field("RevisionId", "node.source.revisionId", "uniqueidentifier", "CHAR(36)"));
        matches.Add("managed.SourceHash=" + Field("SourceHash", "node.source.sourceHash", "varchar(64)", "VARCHAR(64)"));
        // 旧图没有标记时只代表非反馈来源；当前根类型不匹配则拒绝，不能删字段后沿用旧权限集合。
        matches.Add("CASE WHEN managed.OriginKey='feedback_derived' THEN 1 ELSE 0 END=COALESCE(" + Field("FeedbackDerived", "node.source.feedbackDerived", "bit", "INT") + ",0)");
        matches.Add(AiKnowledgeFaqFeedbackSql.Current(mysql, scoped, "managed.SourceJson", "managed.OriginKey", "parentKnowledge.DataClassification"));
        matches.Add("revision.ContentHash=" + Field("RevisionHash", "node.source.contentHash", "varchar(64)", "VARCHAR(64)"));
        matches.Add("parentPublication.Id=" + Field("PublicationIdentity", "publication.id", "uniqueidentifier", "CHAR(36)"));
        matches.Add("parentPublication.Version=" + Field("PublicationVersion", "publication.version", "int", "INT"));
        matches.Add("parentPublication.SnapshotHash=" + Field("PublicationHash", "publication.snapshotHash", "varchar(64)", "VARCHAR(64)"));
        matches.Add("input.ModelFingerprint=" + Field("Fingerprint", "modelFingerprint", "varchar(64)", "VARCHAR(64)"));
        matches.Add("input.DataClassification=" + Field("SourceClassification", "node.status.dataClassification", "varchar(32)", "VARCHAR(32)"));
        matches.Add("approved.Id=" + Field("ModelId", "model.id", "uniqueidentifier", "CHAR(36)"));
        matches.Add("approved.Version=" + Field("ModelVersion", "model.version", "int", "INT"));
        matches.Add("source.ModelConfigId=approved.Id AND source.ModelConfigVersion=approved.Version");
        matches.Add(scoped ? "managed.TenantId=@TenantId" : "managed.TenantId IS NULL");
        matches.Add("parentKnowledge.Version=" + Field("KnowledgeVersion", "knowledge.version", "int", "INT"));
        matches.Add("parentKnowledge.DataClassification=" + Field("Classification", "knowledge.dataClassification", "varchar(32)", "VARCHAR(32)"));
        matches.Add(Exact("approved.TenantId", Field("ModelTenant", "model.tenantId", "uniqueidentifier", "CHAR(36)"), binary: false));
        foreach (var name in new[] { "ProviderKey", "ModelId", "EndpointBaseUrl", "OrganizationId", "ApiKeyProtected" })
            matches.Add(Exact("approved." + name, Field("Approved" + name, "model." + char.ToLowerInvariant(name[0]) + name[1..], "nvarchar(max)", "TEXT"), binary: true));
        var libraryRows = mysql
            ? "JSON_TABLE(@FaqParentBindingsJson,'$[*]' COLUMNS (NESTED PATH '$.libraries[*]' COLUMNS (Id CHAR(36) PATH '$.id',Version INT PATH '$.version',DataClassification VARCHAR(32) PATH '$.dataClassification'))) AS parentLibrary"
            : "OPENJSON(@FaqParentBindingsJson) WITH (Libraries nvarchar(max) '$.libraries' AS JSON) AS parentProof CROSS APPLY OPENJSON(parentProof.Libraries) WITH (Id uniqueidentifier '$.id',Version int '$.version',DataClassification varchar(32) '$.dataClassification') AS parentLibrary";
        var libraryProof = "NOT EXISTS (SELECT 1 FROM " + libraryRows + " WHERE NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_base AS proofLibrary WHERE proofLibrary.Id="
            + (mysql ? "UUID_TO_BIN(parentLibrary.Id,0)" : "parentLibrary.Id") + " AND proofLibrary.Version=parentLibrary.Version AND proofLibrary.DataClassification=parentLibrary.DataClassification AND proofLibrary.IsEnabled=1 AND "
            + (scoped ? "proofLibrary.TenantId=@TenantId" : "proofLibrary.TenantId IS NULL") + "))";
        var json = mysql ? "JSON_TABLE(@FaqParentBindingsJson,'$[*]' COLUMNS (" + string.Join(',', definitions) + ")) AS parentBinding"
            : "OPENJSON(@FaqParentBindingsJson) WITH (" + string.Join(',', definitions) + ") AS parentBinding";
        // 此处只构建节点的当前来源分支；所有祖先已在同一平面集合中，所有文档叶由根既有谓词联合复核。
        var sources = AiKnowledgePublishedSourceSql.Readable(mysql, scoped).Replace(mysql ? AiKnowledgeReadSql.LayersMySql : AiKnowledgeReadSql.Layers,
            AiKnowledgeFaqReadSql.AudienceForSelection(mysql, scoped) + " AND " + AiKnowledgeFaqIndexSql.Origin, StringComparison.Ordinal);
        return "NOT EXISTS (SELECT 1 FROM " + json + " WHERE NOT EXISTS (SELECT 1 FROM (" + sources + ") AS source"
            + " INNER JOIN fn_ai_knowledge_faq AS managed ON managed.ReservedDocumentId=source.DocumentId AND managed.KnowledgeBaseId=source.KnowledgeBaseId AND managed.PublicationId=source.PublicationId"
            + " INNER JOIN fn_ai_knowledge_base AS parentKnowledge ON parentKnowledge.Id=managed.KnowledgeBaseId"
            + " INNER JOIN fn_ai_knowledge_publication AS parentPublication ON parentPublication.Id=source.PublicationId"
            + " INNER JOIN fn_ai_knowledge_faq_release AS released ON released.Id=managed.PublicationId AND released.FaqId=managed.Id AND released.RevisionId=source.FaqRevisionId AND released.VersionId=source.VersionId AND released.SourceHash=managed.SourceHash"
            + " INNER JOIN fn_ai_knowledge_faq_revision AS revision ON revision.Id=released.RevisionId AND revision.FaqId=managed.Id AND revision.ContentHash=released.ContentHash"
            + " INNER JOIN fn_ai_knowledge_faq_source AS input ON input.Id=source.VersionId AND input.FaqId=managed.Id AND input.RevisionId=revision.Id"
            + " INNER JOIN fn_ai_model_config AS approved ON approved.Id=source.ModelConfigId AND approved.Version=source.ModelConfigVersion AND approved.IsEnabled=1"
            + " WHERE " + string.Join(" AND ", matches) + ")) AND " + libraryProof;
        string Exact(string left, string right, bool binary) => "((" + left + " IS NULL AND " + right + " IS NULL) OR "
            // JSON 文本为 Unicode；两侧先统一编码，再精确比较大小写、尾空格和凭据，避免 varchar/nvarchar 的字节差异。
            + (binary ? mysql ? "BINARY " + left + "=BINARY " + right : "CONVERT(varbinary(max),CONVERT(nvarchar(max)," + left + "))=CONVERT(varbinary(max),CONVERT(nvarchar(max)," + right + "))" : left + "=" + right) + ")";
    }
    private static readonly SqlStatement SqlServerHost = new("ai.knowledge_faq_parent.current_sqlserver_host", "SELECT 1 WHERE " + Current(false, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement SqlServerTenant = new("ai.knowledge_faq_parent.current_sqlserver_tenant", "SELECT 1 WHERE " + Current(false, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement MySqlHost = new("ai.knowledge_faq_parent.current_mysql_host", "SELECT 1 WHERE " + Current(true, false), SqlDataScope.HostOnly);
    private static readonly SqlStatement MySqlTenant = new("ai.knowledge_faq_parent.current_mysql_tenant", "SELECT 1 WHERE " + Current(true, true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement For(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql ? scoped ? MySqlTenant : MySqlHost : scoped ? SqlServerTenant : SqlServerHost;
}
