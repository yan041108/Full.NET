using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>当前租户本人运行摘要；只读七列，首尾页均在数据库内完成隔离和稳定排序。</summary>
internal static class AiAgentRunMineSql
{
    internal static readonly SqlStatement FirstSqlServer = new("ai.agent_run.mine.first.sqlserver", """
        SELECT TOP (@Take) Id, StatusKey, DefinitionKey, DefinitionVersion, DeadlineAtUtc, CreatedAtUtc, UpdatedAtUtc
        FROM fn_ai_agent_run
        WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND ActorUserId=@ActorUserId
            AND (@StatusKey IS NULL OR StatusKey=@StatusKey)
        ORDER BY CreatedAtUtc DESC,Id DESC
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement AfterSqlServer = new("ai.agent_run.mine.after.sqlserver", """
        SELECT TOP (@Take) Id, StatusKey, DefinitionKey, DefinitionVersion, DeadlineAtUtc, CreatedAtUtc, UpdatedAtUtc
        FROM fn_ai_agent_run
        WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND ActorUserId=@ActorUserId
            AND (@StatusKey IS NULL OR StatusKey=@StatusKey)
            AND CreatedAtUtc<=@BeforeCreatedAtUtc AND (CreatedAtUtc<@BeforeCreatedAtUtc OR Id<@BeforeId)
        ORDER BY CreatedAtUtc DESC,Id DESC
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement FirstMySql = new("ai.agent_run.mine.first.mysql", """
        SELECT Id, StatusKey, DefinitionKey, DefinitionVersion, DeadlineAtUtc, CreatedAtUtc, UpdatedAtUtc
        FROM fn_ai_agent_run
        WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND ActorUserId=@ActorUserId
            AND (@StatusKey IS NULL OR StatusKey=@StatusKey)
        ORDER BY CreatedAtUtc DESC,Id DESC LIMIT @Take
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    internal static readonly SqlStatement AfterMySql = new("ai.agent_run.mine.after.mysql", """
        SELECT Id, StatusKey, DefinitionKey, DefinitionVersion, DeadlineAtUtc, CreatedAtUtc, UpdatedAtUtc
        FROM fn_ai_agent_run
        WHERE TenantId=@TenantId AND ScopeKey=@ScopeKey AND ActorUserId=@ActorUserId
            AND (@StatusKey IS NULL OR StatusKey=@StatusKey)
            AND CreatedAtUtc<=@BeforeCreatedAtUtc AND (CreatedAtUtc<@BeforeCreatedAtUtc OR Id<@BeforeId)
        ORDER BY CreatedAtUtc DESC,Id DESC LIMIT @Take
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
