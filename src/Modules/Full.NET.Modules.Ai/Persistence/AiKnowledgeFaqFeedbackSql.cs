using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>反馈、人工审核及原消息在同一 SQL 核对；只访问 AI 自有表，不读取原用户问题。</summary>
internal static class AiKnowledgeFaqFeedbackSql
{
    private static string Scope(bool scoped, string alias) => alias + (scoped ? ".TenantId=@TenantId" : ".TenantId IS NULL");
    private static string Joins(bool scoped) => " FROM fn_ai_knowledge_answer_feedback AS feedback"
        + " INNER JOIN fn_ai_knowledge_answer_review AS review ON review.FeedbackId=feedback.Id AND " + Scope(scoped, "review")
        + " INNER JOIN fn_ai_chat_message AS message ON message.Id=feedback.MessageId AND message.SessionId=feedback.SessionId"
        + " INNER JOIN fn_ai_chat_session AS session ON session.Id=message.SessionId AND session.OwnerUserId=feedback.ActorUserId";
    private static string Eligibility(bool scoped) => " WHERE " + Scope(scoped, "feedback") + " AND " + Scope(scoped, "session")
        + " AND feedback.StatusKey='pending_review' AND review.DecisionKey='approved' AND review.FeedbackVersion=feedback.Version"
        + " AND review.LineageHash=feedback.LineageHash AND review.ReviewerUserId<>feedback.ActorUserId"
        + " AND message.RoleKey='assistant' AND message.StatusKey='completed' AND message.IsKnowledgeAnswer=1 AND message.KnowledgeLineage IS NOT NULL";
    private static string From(bool scoped) => Joins(scoped) + Eligibility(scoped);

    /// <summary>安全 JSON 替身只避免未选中坏行抛错；缺字段、未知协议和任何资格变化均不产生有效绑定。</summary>
    internal static string Current(bool mysql, bool scoped, string sourceJson, string origin, string classification)
    {
        var safe = mysql ? "CASE WHEN JSON_VALID(" + sourceJson + ")=1 THEN " + sourceJson + " ELSE '{}' END"
            : "CASE WHEN ISJSON(" + sourceJson + ")=1 THEN " + sourceJson + " ELSE N'{}' END";
        var fields = new List<string>();
        string Field(string name, string sql, string my)
        {
            fields.Add(name + (mysql ? " " + my + " PATH '$." : " " + sql + " '$.") + char.ToLowerInvariant(name[0]) + name[1..] + "'");
            return mysql && sql == "uniqueidentifier" ? "UUID_TO_BIN(expectedFeedback." + name + ",0)" : "expectedFeedback." + name;
        }
        var match = new List<string>();
        foreach (var pair in new[] { ("FeedbackId", "feedback.Id"), ("ReviewId", "review.Id"), ("MessageId", "message.Id"), ("SessionId", "session.Id"), ("OwnerUserId", "session.OwnerUserId") })
            match.Add(pair.Item2 + "=" + Field(pair.Item1, "uniqueidentifier", "CHAR(36)"));
        match.Add("feedback.Version=" + Field("FeedbackVersion", "int", "INT"));
        match.Add("review.Version=" + Field("ReviewVersion", "int", "INT"));
        var tenant = Field("TenantId", "uniqueidentifier", "CHAR(36)"); match.Add(scoped ? tenant + "=@TenantId" : tenant + " IS NULL");
        var lineage = Field("LineageHash", "varchar(64)", "VARCHAR(64)"); match.Add("feedback.LineageHash=" + lineage);
        var lineageSql = Field("LineageSqlServerHash", "varchar(64)", "VARCHAR(64)");
        var answer = Field("AnswerHash", "varchar(64)", "VARCHAR(64)"); var answerSql = Field("AnswerSqlServerHash", "varchar(64)", "VARCHAR(64)");
        match.Add(Hash("message.KnowledgeLineage") + "=" + (mysql ? "UPPER(" + lineage + ")" : lineageSql));
        match.Add(Hash("message.Content") + "=" + (mysql ? answer : answerSql));
        var binding = mysql ? "JSON_TABLE(" + safe + ",'$.feedback' COLUMNS (" + string.Join(',', fields) + ")) AS expectedFeedback"
            : "OPENJSON(" + safe + ",'$.feedback') WITH (" + string.Join(',', fields) + ") AS expectedFeedback";
        var version = mysql ? "JSON_UNQUOTE(JSON_EXTRACT(" + safe + ",'$.version'))" : "JSON_VALUE(" + safe + ",'$.version')";
        var kind = mysql ? "JSON_UNQUOTE(JSON_EXTRACT(" + safe + ",'$.origin'))" : "JSON_VALUE(" + safe + ",'$.origin')";
        var privateSource = mysql ? "JSON_UNQUOTE(JSON_EXTRACT(" + safe + ",'$.feedbackPermission'))" : "JSON_VALUE(" + safe + ",'$.feedbackPermission')";
        var businessOwner = mysql ? "JSON_UNQUOTE(JSON_EXTRACT(" + safe + ",'$.personalSchedules[0].ownerUserId'))" : "JSON_VALUE(" + safe + ",'$.personalSchedules[0].ownerUserId')";
        var ownerId = mysql ? "CASE WHEN IS_UUID(" + businessOwner + ")=1 THEN UUID_TO_BIN(" + businessOwner + ",0) ELSE NULL END"
            : "TRY_CONVERT(uniqueidentifier," + businessOwner + ")";
        var restricted = mysql ? "BINARY " + classification + "=BINARY 'restricted'"
            : "CONVERT(varbinary(max),CONVERT(nvarchar(max)," + classification + "))=CONVERT(varbinary(max),N'restricted')";
        var joined = Joins(scoped) + " CROSS JOIN " + binding + Eligibility(scoped) + " AND " + string.Join(" AND ", match);
        // 本批 MySQL 8.0.46 对关联 EXISTS 的 semijoin 改写产生错误空集；单行聚合保留默认优化器及全部核证条件。
        // 绑定对象与各表主键均唯一，COUNT(*)=1 与准确存在性证明等价；SQL Server 保留已验收的存在性查询。
        var qualified = mysql ? "(SELECT COUNT(*)" + joined + ")=1"
            : "EXISTS (SELECT 1 FROM " + binding + " WHERE EXISTS (SELECT 1" + From(scoped) + " AND " + string.Join(" AND ", match) + "))";
        // 仅用AI不可变来源所属人预筛私人候选，不用此SQL替代Calendar当前会话/对象/版本权威复核。
        var business = "(" + businessOwner + " IS NULL OR (" + version + "='4' AND " + ownerId + "=@OwnerUserId AND " + restricted + "))";
        return "(" + business + " AND (" + privateSource + " IS NULL OR (@FaqFeedbackReadAllowed=1 AND " + restricted + ")) AND (" + origin + "<>'feedback_derived' OR ("
            + restricted + " AND " + privateSource + "='true' AND " + version + " IN ('3','4') AND " + kind + "='feedback_derived'"
            + " AND " + qualified + ")))";
        string Hash(string column) => mysql ? "UPPER(SHA2(CONVERT(" + column + " USING utf8mb4),256))"
            : "CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varbinary(max),CONVERT(nvarchar(max)," + column + "))),2)";
    }
    private static SqlStatement Find(bool scoped) => new("ai.knowledge_faq_feedback.find_" + (scoped ? "tenant" : "host"),
        "SELECT feedback.Id AS FeedbackId,feedback.Version AS FeedbackVersion,review.Id AS ReviewId,review.Version AS ReviewVersion,message.Id AS MessageId,session.Id AS SessionId,session.OwnerUserId,session.TenantId,feedback.LineageHash,message.KnowledgeLineage,message.Content AS Answer"
        + From(scoped) + " AND feedback.Id=@FeedbackId", scoped ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, scoped ? SqlTenantBinding.CurrentTenantId : SqlTenantBinding.None);
    private static readonly SqlStatement FindHost = Find(false), FindTenant = Find(true);
    internal static SqlStatement FindFor(bool scoped) => scoped ? FindTenant : FindHost;
    private static SqlStatement Pending(bool mysql, bool scoped) => new("ai.knowledge_faq_feedback.pending_" + (mysql ? "mysql_" : "sqlserver_") + (scoped ? "tenant" : "host"),
        "SELECT 1 WHERE " + Current(mysql, scoped, "@SourceJson", "@OriginKey", "@TargetClassification"), scoped ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, scoped ? SqlTenantBinding.CurrentTenantId : SqlTenantBinding.None);
    private static readonly SqlStatement PendingSqlServerHost = Pending(false, false), PendingSqlServerTenant = Pending(false, true), PendingMySqlHost = Pending(true, false), PendingMySqlTenant = Pending(true, true);
    internal static SqlStatement PendingFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql ? scoped ? PendingMySqlTenant : PendingMySqlHost : scoped ? PendingSqlServerTenant : PendingSqlServerHost;
    private static SqlStatement PendingGraph(bool mysql, bool scoped) => new("ai.knowledge_faq_feedback.graph_" + (mysql ? "mysql_" : "sqlserver_") + (scoped ? "tenant" : "host"),
        "SELECT 1 FROM fn_ai_knowledge_base AS k CROSS JOIN (SELECT @SourceJson AS SourceJson) AS f WHERE k.Id=@KnowledgeBaseId AND k.OwnerUserId=@OwnerUserId AND k.IsEnabled=1 AND k.Version=@TargetKnowledgeVersion AND k.DataClassification=@TargetClassification AND "
        + Scope(scoped, "k") + " AND " + AiKnowledgeFaqDraftSql.ReadableLeaves(mysql, scoped) + " AND " + AiKnowledgeFaqParentSql.Current(mysql, scoped)
        + " AND " + Current(mysql, scoped, "@SourceJson", "@OriginKey", "k.DataClassification"), scoped ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, scoped ? SqlTenantBinding.CurrentTenantId : SqlTenantBinding.None);
    private static readonly SqlStatement GraphSqlServerHost = PendingGraph(false, false), GraphSqlServerTenant = PendingGraph(false, true), GraphMySqlHost = PendingGraph(true, false), GraphMySqlTenant = PendingGraph(true, true);
    internal static SqlStatement PendingGraphFor(DatabaseProvider provider, bool scoped) => provider == DatabaseProvider.MySql ? scoped ? GraphMySqlTenant : GraphMySqlHost : scoped ? GraphSqlServerTenant : GraphSqlServerHost;
}
