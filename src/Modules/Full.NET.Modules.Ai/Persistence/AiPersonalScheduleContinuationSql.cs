using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>会话内完整且有界的派生身份；超出窗口拒绝，不只按最近模型消息或展示引用核证。</summary>
internal static class AiPersonalScheduleContinuationSql
{
    private static readonly IReadOnlyDictionary<(bool Mysql, bool Tenant), SqlStatement> Statements = Build();
    internal static SqlStatement For(DatabaseProvider provider, bool tenant) => Statements[(provider == DatabaseProvider.MySql, tenant)];
    private static Dictionary<(bool, bool), SqlStatement> Build()
    {
        var result = new Dictionary<(bool, bool), SqlStatement>();
        foreach (var mysql in new[] { false, true }) foreach (var tenant in new[] { false, true })
        {
            var sql = "SELECT " + (mysql ? "" : "TOP (65) ") + "message.Id,message.SessionId,message.RoleKey,'' AS Content,message.StatusKey,"
                + "message.PromptTokens,message.CompletionTokens,message.CreatedAtUtc,message.IsKnowledgeAnswer,message.KnowledgeLineage "
                + "FROM fn_ai_chat_message AS message INNER JOIN fn_ai_chat_session AS session ON session.Id=message.SessionId "
                + "WHERE session.Id=@SessionId AND session.OwnerUserId=@OwnerUserId AND "
                + (tenant ? "session.TenantId=@TenantId" : "session.TenantId IS NULL")
                + " AND message.RoleKey='assistant' AND message.StatusKey='completed' AND (message.IsKnowledgeAnswer=1 OR message.KnowledgeLineage IS NOT NULL) "
                + "ORDER BY message.CreatedAtUtc DESC,message.Id DESC" + (mysql ? " LIMIT 65" : "");
            result[(mysql, tenant)] = new("ai.personal_schedule_continuation." + (mysql ? "mysql_" : "sqlserver_") + (tenant ? "tenant" : "host"), sql,
                tenant ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, tenant ? SqlTenantBinding.CurrentTenantId : SqlTenantBinding.None);
        }
        return result;
    }
}
