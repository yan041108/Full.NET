using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>审核只访问本模块表；范围来自可信上下文，当前来源权威在事务外证明。</summary>
internal static class AiKnowledgeAnswerFeedbackReviewSql
{
    private static string Scope(bool tenant, string alias) => tenant ? alias + ".TenantId=@TenantId" : alias + ".TenantId IS NULL";
    private const string Columns = "feedback.Id AS FeedbackId,feedback.SessionId,feedback.MessageId,feedback.ActorUserId AS OwnerUserId,feedback.RatingKey,feedback.ReasonKey,feedback.CitationIdsJson,feedback.LineageHash,feedback.Version AS FeedbackVersion,message.KnowledgeLineage,review.FeedbackVersion AS ReviewFeedbackVersion,review.LineageHash AS ReviewLineageHash,review.ReviewerUserId,review.DecisionKey,review.Version AS ReviewVersion";
    private static string From(bool tenant) => " FROM fn_ai_knowledge_answer_feedback AS feedback INNER JOIN fn_ai_chat_message AS message ON message.Id=feedback.MessageId AND message.SessionId=feedback.SessionId INNER JOIN fn_ai_chat_session AS session ON session.Id=message.SessionId AND session.OwnerUserId=feedback.ActorUserId LEFT JOIN fn_ai_knowledge_answer_review AS review ON review.FeedbackId=feedback.Id AND " + Scope(tenant, "review") + " WHERE " + Scope(tenant, "feedback") + " AND " + Scope(tenant, "session") + " AND feedback.StatusKey='pending_review' AND message.RoleKey='assistant' AND message.StatusKey='completed' AND message.IsKnowledgeAnswer=1 AND message.KnowledgeLineage IS NOT NULL";
    private static string Page(bool mysql, bool tenant, bool export)
    {
        var count = export ? "65" : "26";
        var predicate = export ? " AND review.DecisionKey='approved' AND review.FeedbackVersion=feedback.Version AND review.LineageHash=feedback.LineageHash AND review.ReviewerUserId<>feedback.ActorUserId"
            : " AND feedback.ActorUserId<>@ReviewerUserId AND (@AfterId IS NULL OR feedback.Id>@AfterId)";
        return "SELECT " + (mysql ? "" : "TOP (" + count + ") ") + Columns + From(tenant) + predicate + " ORDER BY feedback.Id" + (mysql ? " LIMIT " + count : "");
    }

    // 来源与反馈在同一次最终读取中核对，不能在两笔异步查询之间释放旧资格。
    private static string Current(bool mysql, bool tenant)
    {
        var fields = new[] { "KnowledgeBaseId", "DocumentId", "VersionId", "FileId", "TaskId" };
        var strings = new[] { "ContentHash", "ReceiptHash", "ManifestHash", "PlanHash" };
        var definitions = new List<string> { mysql ? "FeedbackId CHAR(36) PATH '$.feedbackId'" : "FeedbackId uniqueidentifier '$.feedbackId'",
            mysql ? "MessageId CHAR(36) PATH '$.messageId'" : "MessageId uniqueidentifier '$.messageId'" };
        foreach (var field in fields) definitions.Add(mysql ? field + " CHAR(36) PATH '$.dependency." + char.ToLowerInvariant(field[0]) + field[1..] + "'"
            : field + " uniqueidentifier '$.dependency." + char.ToLowerInvariant(field[0]) + field[1..] + "'");
        foreach (var field in strings) definitions.Add(mysql ? field + " CHAR(64) PATH '$.dependency." + char.ToLowerInvariant(field[0]) + field[1..] + "'"
            : field + " varchar(64) '$.dependency." + char.ToLowerInvariant(field[0]) + field[1..] + "'");
        foreach (var field in new[] { "DocumentVersion", "KnowledgeBaseVersion" }) definitions.Add(mysql ? field + " INT PATH '$.dependency." + char.ToLowerInvariant(field[0]) + field[1..] + "'"
            : field + " int '$.dependency." + char.ToLowerInvariant(field[0]) + field[1..] + "'");
        var bindings = mysql ? "JSON_TABLE(@ReviewBindingsJson,'$[*]' COLUMNS (" + string.Join(',', definitions) + ")) AS expected"
            : "OPENJSON(@ReviewBindingsJson) WITH (" + string.Join(',', definitions) + ") AS expected";
        string Id(string column) => mysql ? "UUID_TO_BIN(expected." + column + ",0)" : "expected." + column;
        // MySQL 的 JSON 字符串必须转回官方非交换 Binary16，不能比较隐式 UUID 排序或编码。
        var predicate = Id("FeedbackId") + "=feedback.Id AND " + Id("MessageId") + "=message.Id";
        var match = string.Join(" AND ", fields.Select(field => "source." + field + "=" + Id(field)))
            + " AND source.DocumentVersion=expected.DocumentVersion AND source.KnowledgeBaseVersion=expected.KnowledgeBaseVersion AND source.ContentHash=expected.ContentHash"
            // 空租约是状态哨兵，不是持久化标识；使用提供程序常量，避免触发 Guid 参数的已分配标识门禁。
            + " AND source.ActiveTaskId=" + Id("TaskId") + " AND source.ActiveLeaseToken IS NOT NULL AND source.ActiveLeaseToken<>"
            + (mysql ? "X'00000000000000000000000000000000'" : "CONVERT(uniqueidentifier,'00000000-0000-0000-0000-000000000000')")
            + " AND source.ActiveReceiptHash=expected.ReceiptHash AND source.ActiveManifestHash=expected.ManifestHash AND source.ActivePlanHash=expected.PlanHash"
            + " AND source.IsDeleted=0 AND source.CleanupPending=0 AND source.IsCurrent=1 AND source.StateKey='completed' AND source.HasDerivedData=1";
        var sources = AiKnowledgeIndexStatusSql.HistoryFor(mysql ? DatabaseProvider.MySql : DatabaseProvider.SqlServer, tenant).Text;
        return "SELECT " + Columns + From(tenant) + " AND EXISTS (SELECT 1 FROM " + bindings + " WHERE " + predicate + ")"
            + " AND NOT EXISTS (SELECT 1 FROM " + bindings + " WHERE " + predicate + " AND NOT EXISTS (SELECT 1 FROM (" + sources + ") AS source WHERE " + match + "))";
    }

    private static string Eligible(bool tenant) => "SELECT 1 FROM fn_ai_knowledge_answer_feedback AS feedback WHERE feedback.Id=@FeedbackId AND feedback.Version=@FeedbackVersion AND feedback.LineageHash=@LineageHash AND feedback.StatusKey='pending_review' AND feedback.ActorUserId<>@ReviewerUserId AND " + Scope(tenant, "feedback");
    private static string Insert(bool tenant) => "INSERT INTO fn_ai_knowledge_answer_review (Id,TenantId,FeedbackId,FeedbackVersion,LineageHash,ReviewerUserId,DecisionKey,Version,CreatedAtUtc,UpdatedAtUtc) SELECT @Id," + (tenant ? "@TenantId" : "NULL") + ",@FeedbackId,@FeedbackVersion,@LineageHash,@ReviewerUserId,@DecisionKey,1,@Now,@Now WHERE EXISTS (" + Eligible(tenant) + ") AND NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_answer_review WHERE FeedbackId=@FeedbackId)";
    private static string Update(bool tenant) => "UPDATE fn_ai_knowledge_answer_review SET FeedbackVersion=@FeedbackVersion,LineageHash=@LineageHash,ReviewerUserId=@ReviewerUserId,DecisionKey=@DecisionKey,Version=Version+1,UpdatedAtUtc=@Now WHERE FeedbackId=@FeedbackId AND Version=@ReviewVersion AND " + Scope(tenant, "fn_ai_knowledge_answer_review") + " AND EXISTS (" + Eligible(tenant) + ")";
    private static readonly SqlStatement FindHost = new("ai.knowledge_answer_review.find_host", "SELECT " + Columns + From(false) + " AND feedback.Id=@FeedbackId", SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement FindTenant = new("ai.knowledge_answer_review.find_tenant", "SELECT " + Columns + From(true) + " AND feedback.Id=@FeedbackId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement InsertHost = new("ai.knowledge_answer_review.insert_host", Insert(false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement InsertTenant = new("ai.knowledge_answer_review.insert_tenant", Insert(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement UpdateHost = new("ai.knowledge_answer_review.update_host", Update(false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement UpdateTenant = new("ai.knowledge_answer_review.update_tenant", Update(true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement PageSqlServerHost = new("ai.knowledge_answer_review.page_sqlserver_host", Page(false,false,false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement PageSqlServerTenant = new("ai.knowledge_answer_review.page_sqlserver_tenant", Page(false,true,false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement PageMySqlHost = new("ai.knowledge_answer_review.page_mysql_host", Page(true,false,false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement PageMySqlTenant = new("ai.knowledge_answer_review.page_mysql_tenant", Page(true,true,false), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement ExportSqlServerHost = new("ai.knowledge_answer_review.export_sqlserver_host", Page(false,false,true), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement ExportSqlServerTenant = new("ai.knowledge_answer_review.export_sqlserver_tenant", Page(false,true,true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement ExportMySqlHost = new("ai.knowledge_answer_review.export_mysql_host", Page(true,false,true), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement ExportMySqlTenant = new("ai.knowledge_answer_review.export_mysql_tenant", Page(true,true,true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement CurrentSqlServerHost = new("ai.knowledge_answer_review.current_sqlserver_host", Current(false,false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement CurrentSqlServerTenant = new("ai.knowledge_answer_review.current_sqlserver_tenant", Current(false,true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static readonly SqlStatement CurrentMySqlHost = new("ai.knowledge_answer_review.current_mysql_host", Current(true,false), SqlDataScope.HostOnly, SqlTenantBinding.None);
    private static readonly SqlStatement CurrentMySqlTenant = new("ai.knowledge_answer_review.current_mysql_tenant", Current(true,true), SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement FindFor(bool tenant) => tenant ? FindTenant : FindHost;
    internal static SqlStatement InsertFor(bool tenant) => tenant ? InsertTenant : InsertHost;
    internal static SqlStatement UpdateFor(bool tenant) => tenant ? UpdateTenant : UpdateHost;
    internal static SqlStatement PageFor(DatabaseProvider provider, bool tenant, bool export) => export ? (provider == DatabaseProvider.MySql ? tenant ? ExportMySqlTenant : ExportMySqlHost : tenant ? ExportSqlServerTenant : ExportSqlServerHost) : (provider == DatabaseProvider.MySql ? tenant ? PageMySqlTenant : PageMySqlHost : tenant ? PageSqlServerTenant : PageSqlServerHost);
    internal static SqlStatement CurrentFor(DatabaseProvider provider, bool tenant) => provider == DatabaseProvider.MySql ? tenant ? CurrentMySqlTenant : CurrentMySqlHost : tenant ? CurrentSqlServerTenant : CurrentSqlServerHost;

}
