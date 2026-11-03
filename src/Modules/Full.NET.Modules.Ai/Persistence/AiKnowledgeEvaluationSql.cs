using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>本模块根锁、不可变修订和最终完整来源复核；Provider 仅改变固定方言，不拼接客户端值。</summary>
internal static class AiKnowledgeEvaluationSql
{
    private const string Columns = "d.Id,d.KnowledgeBaseId,d.TenantId,d.CreatedById,d.RequestKey AS CreateRequestKey,d.CreateHash,d.HeadRevisionId,d.HeadRevisionNo,d.Version,d.FootprintJson,d.FootprintHash,"
        + "a.Id AS ApprovalId,a.DecisionKey AS ApprovalState,r.Id AS RevisionId,r.RevisionNo,r.RequestKey,r.RequestHash,r.ContentJson,r.ContentHash,r.SourceJson,r.SourceHash,r.EditorUserId,r.CreatedAtUtc,r.RestoredFromRevisionId";
    private static string Scope(bool scoped, string alias) => scoped ? alias + ".TenantId=@TenantId" : alias + ".TenantId IS NULL";
    private static string From(bool scoped) => " FROM fn_ai_knowledge_evaluation_set AS d INNER JOIN fn_ai_knowledge_base AS k ON k.Id=d.KnowledgeBaseId"
        + " INNER JOIN fn_ai_knowledge_evaluation_revision AS r ON r.SetId=d.Id"
        + " LEFT JOIN fn_ai_knowledge_evaluation_approval AS a ON a.Id=d.ApprovalId AND a.SetId=d.Id AND a.RevisionId=r.Id"
        + " WHERE d.KnowledgeBaseId=@KnowledgeBaseId AND k.IsEnabled=1 AND " + Scope(scoped, "d") + " AND " + Scope(scoped, "k");

    internal static SqlStatement ReadFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "read");
    internal static SqlStatement PageFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "page");
    internal static SqlStatement RevisionFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "revision");
    internal static SqlStatement ReplayFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "replay");
    internal static SqlStatement CreateReplayFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "create_replay");
    internal static SqlStatement LockFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "lock");
    internal static SqlStatement CurrentFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "current");
    internal static SqlStatement CurrentPageFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "current_page");
    internal static SqlStatement InsertFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "insert");
    internal static SqlStatement AppendFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "append");
    internal static SqlStatement AdvanceFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "advance");
    internal static SqlStatement ReviewFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "review");
    internal static SqlStatement ReviewReplayFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "review_replay");
    internal static SqlStatement ApproveFor(DatabaseProvider provider, bool scoped) => Get(provider, scoped, "approve");

    private static SqlStatement Get(DatabaseProvider provider, bool scoped, string operation) => Statements[(provider == DatabaseProvider.MySql, scoped, operation)];
    private static readonly IReadOnlyDictionary<(bool Mysql, bool Scoped, string Operation), SqlStatement> Statements = Build();
    private static IReadOnlyDictionary<(bool, bool, string), SqlStatement> Build()
    {
        var result = new Dictionary<(bool, bool, string), SqlStatement>();
        foreach (var mysql in new[] { false, true })
        foreach (var scoped in new[] { false, true })
        {
            var source = From(scoped);
            var audience = " AND " + AiKnowledgeFaqReadSql.AudienceForRoot(mysql, scoped);
            Add("read", "SELECT " + Columns + source + audience + " AND d.Id=@SetId AND r.Id=d.HeadRevisionId");
            Add("revision", "SELECT " + Columns + source + audience + " AND d.Id=@SetId AND r.Id=@RevisionId");
            Add("replay", "SELECT " + Columns + source + audience + " AND d.Id=@SetId AND r.RequestKey=@RequestKey");
            Add("create_replay", "SELECT " + Columns + source + audience + " AND d.RequestKey=@RequestKey AND r.RevisionNo=1");
            Add("page", "SELECT " + (mysql ? "" : "TOP (9) ") + Columns + source + audience
                + " AND r.Id=d.HeadRevisionId AND (@AfterId IS NULL OR d.Id>@AfterId) ORDER BY d.Id" + (mysql ? " LIMIT 9" : ""));
            Add("lock", "SELECT k.Id FROM fn_ai_knowledge_base AS k" + (mysql ? "" : " WITH (UPDLOCK,HOLDLOCK)")
                + " WHERE k.Id=@KnowledgeBaseId AND k.IsEnabled=1 AND " + Scope(scoped, "k") + audience + (mysql ? " FOR UPDATE" : ""));
            // 根版本、分类和完整所有权/读者范围在同一条最终 SQL 中绑定；事务内只使用事务前权威端口取得的参数。
            Add("current", "SELECT 1" + source + audience
                + " AND d.Id=@SetId AND d.Version=@ExpectedVersion AND d.HeadRevisionId=@ExpectedRevisionId AND d.FootprintHash=@ExpectedFootprintHash"
                + " AND r.Id=@RevisionId AND r.ContentHash=@ContentHash AND r.SourceHash=@SourceHash"
                + " AND k.Version=@KnowledgeVersion AND k.DataClassification=@Classification AND " + Qualified(mysql, scoped));
            var definitions = new List<string>();
            foreach (var column in new[] { "Id", "HeadRevisionId", "RevisionId" }) definitions.Add(column
                + (mysql ? " CHAR(36) PATH '$." : " uniqueidentifier '$.") + char.ToLowerInvariant(column[0]) + column[1..] + "'");
            definitions.Add(mysql ? "Version INT PATH '$.version'" : "Version int '$.version'");
            foreach (var column in new[] { "FootprintHash", "ContentHash", "SourceHash" }) definitions.Add(column
                + (mysql ? " VARCHAR(64) PATH '$." : " varchar(64) '$.") + char.ToLowerInvariant(column[0]) + column[1..] + "'");
            definitions.Add(mysql ? "FootprintJson LONGTEXT PATH '$.footprintJson'" : "FootprintJson nvarchar(max) '$.footprintJson'");
            var expected = mysql ? "JSON_TABLE(@SetBindingsJson,'$[*]' COLUMNS (" + string.Join(',', definitions) + ")) AS expected"
                : "OPENJSON(@SetBindingsJson) WITH (" + string.Join(',', definitions) + ") AS expected";
            string ExpectedId(string column) => mysql ? "UUID_TO_BIN(expected." + column + ",0)" : "expected." + column;
            Add("current_page", "SELECT d.Id" + source + audience + " AND EXISTS (SELECT 1 FROM " + expected
                + " WHERE d.Id=" + ExpectedId("Id") + " AND d.Version=expected.Version AND d.HeadRevisionId=" + ExpectedId("HeadRevisionId")
                + " AND d.FootprintHash=expected.FootprintHash AND r.Id=" + ExpectedId("RevisionId")
                + " AND r.ContentHash=expected.ContentHash AND r.SourceHash=expected.SourceHash AND "
                + Qualified(mysql, scoped).Replace("@FootprintJson", "expected.FootprintJson", StringComparison.Ordinal) + ")"
                + " AND k.Version=@KnowledgeVersion AND k.DataClassification=@Classification");
            Add("insert", "INSERT INTO fn_ai_knowledge_evaluation_set (Id,KnowledgeBaseId,TenantId,CreatedById,RequestKey,CreateHash,HeadRevisionId,HeadRevisionNo,Version,FootprintJson,FootprintHash,CreatedAtUtc)"
                + " SELECT @SetId,k.Id," + (scoped ? "@TenantId" : "NULL") + ",@Actor,@RequestKey,@RequestHash,@RevisionId,1,1,@FootprintJson,@FootprintHash,@Now"
                + " FROM fn_ai_knowledge_base AS k WHERE k.Id=@KnowledgeBaseId AND k.OwnerUserId=@Actor AND k.IsEnabled=1 AND " + Scope(scoped, "k")
                + " AND k.Version=@KnowledgeVersion AND k.DataClassification=@Classification AND " + Qualified(mysql, scoped));
            Add("append", "INSERT INTO fn_ai_knowledge_evaluation_revision (Id,SetId,RevisionNo,RequestKey,RequestHash,ContentJson,ContentHash,SourceJson,SourceHash,EditorUserId,CreatedAtUtc,RestoredFromRevisionId)"
                + " SELECT @RevisionId,d.Id,@RevisionNo,@RequestKey,@RequestHash,@ContentJson,@ContentHash,@FootprintJson,@FootprintHash,@Actor,@Now,@RestoredFromRevisionId"
                + " FROM fn_ai_knowledge_evaluation_set AS d WHERE d.Id=@SetId AND d.KnowledgeBaseId=@KnowledgeBaseId AND " + Scope(scoped, "d"));
            Add("advance", "UPDATE fn_ai_knowledge_evaluation_set SET HeadRevisionId=@RevisionId,HeadRevisionNo=@RevisionNo,Version=Version+1,FootprintJson=@FootprintJson,FootprintHash=@FootprintHash,ApprovalId=NULL"
                + " WHERE Id=@SetId AND KnowledgeBaseId=@KnowledgeBaseId AND " + (scoped ? "TenantId=@TenantId" : "TenantId IS NULL")
                + " AND Version=@ExpectedVersion AND HeadRevisionId=@ExpectedRevisionId AND FootprintHash=@ExpectedFootprintHash");
            Add("review_replay", "SELECT a.Id,a.RevisionId,a.RequestHash,a.DecisionKey FROM fn_ai_knowledge_evaluation_approval AS a"
                + " INNER JOIN fn_ai_knowledge_evaluation_set AS d ON d.Id=a.SetId WHERE d.Id=@SetId AND d.KnowledgeBaseId=@KnowledgeBaseId AND "
                + Scope(scoped, "d") + " AND a.RequestKey=@RequestKey");
            Add("review", "INSERT INTO fn_ai_knowledge_evaluation_approval (Id,SetId,RevisionId,ReviewerUserId,DecisionKey,RequestKey,RequestHash,CreatedAtUtc)"
                + " SELECT @ApprovalId,d.Id,@RevisionId,@Actor,@Decision,@RequestKey,@RequestHash,@Now FROM fn_ai_knowledge_evaluation_set AS d"
                + " WHERE d.Id=@SetId AND d.KnowledgeBaseId=@KnowledgeBaseId AND " + Scope(scoped, "d")
                + " AND d.Version=@ExpectedVersion AND d.HeadRevisionId=@RevisionId");
            Add("approve", "UPDATE fn_ai_knowledge_evaluation_set SET ApprovalId=@ApprovalId,Version=Version+1"
                + " WHERE Id=@SetId AND KnowledgeBaseId=@KnowledgeBaseId AND " + (scoped ? "TenantId=@TenantId" : "TenantId IS NULL")
                + " AND Version=@ExpectedVersion AND HeadRevisionId=@RevisionId AND FootprintHash=@ExpectedFootprintHash");

            void Add(string purpose, string sql) => result.Add((mysql, scoped, purpose), new("ai.knowledge_evaluation." + purpose
                + (mysql ? "_mysql_" : "_sqlserver_") + (scoped ? "tenant" : "host"), sql,
                scoped ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, scoped ? SqlTenantBinding.CurrentTenantId : SqlTenantBinding.None));
        }
        return result;
    }

    /// <summary>所有累计来源在一个最终查询中核证；移出当前样例的来源也必须继续符合资格。</summary>
    internal static string Qualified(bool mysql, bool scoped)
    {
        var sources = mysql
            ? "JSON_TABLE(@FootprintJson,'$.sources[*]' COLUMNS (SourceJson LONGTEXT PATH '$')) AS f"
            : "OPENJSON(@FootprintJson,'$.sources') WITH (SourceJson nvarchar(max) '$') AS f";
        return "NOT EXISTS (SELECT 1 FROM " + sources + " WHERE CASE WHEN ("
            + AiKnowledgeFaqDraftSql.ReadableLeaves(mysql, scoped) + " AND " + AiKnowledgeFaqParentSql.Current(mysql, scoped)
            + " AND " + AiKnowledgeFaqFeedbackSql.Current(mysql, scoped, "f.SourceJson", "'feedback_derived'", "k.DataClassification") + ") THEN 1 ELSE 0 END<>1)";
    }
}
