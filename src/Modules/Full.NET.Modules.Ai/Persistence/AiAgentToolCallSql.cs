using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>Agent Tool 调用审计 SQL。</summary>
internal static class AiAgentToolCallSql
{
    private const string Columns = """
        call.Id,
                  call.TenantId,
                  call.ActorUserId,
                  call.ToolName,
                  call.PermissionCode,
                  call.StatusKey,
                  call.DurationMs,
                  call.InputSummary,
                  call.OutputSummary,
                  call.ErrorCode,
                  call.TraceId,
                  call.CreatedAtUtc
        """;

    private const string ScopeClause = """
        (@ScopeTenantId IS NULL OR call.TenantId = @ScopeTenantId)
          AND (@FilterTenantId IS NULL OR call.TenantId = @FilterTenantId)
          AND (@ToolName IS NULL OR call.ToolName = @ToolName)
          AND (@StatusKey IS NULL OR call.StatusKey = @StatusKey)
        """;

    public static readonly SqlStatement InsertCall = new(
        "ai.insert_agent_tool_call",
        """
        INSERT INTO fn_ai_agent_tool_call
            (Id, TenantId, ActorUserId, ToolName, PermissionCode, StatusKey, DurationMs,
             InputSummary, OutputSummary, ErrorCode, TraceId, CreatedAtUtc)
        VALUES
            (@Id, @TenantId, @ActorUserId, @ToolName, @PermissionCode, @StatusKey, @DurationMs,
             @InputSummary, @OutputSummary, @ErrorCode, @TraceId, @CreatedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly string ListCallsSqlServer = $"""
        SELECT {Columns}
        FROM fn_ai_agent_tool_call AS call
        WHERE {ScopeClause}
        ORDER BY call.CreatedAtUtc DESC, call.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

    public static readonly string CountCallsSqlServer = $"""
        SELECT COUNT(1)
        FROM fn_ai_agent_tool_call AS call
        WHERE {ScopeClause}
        """;

    public static readonly string ListCallsMySql = $"""
        SELECT {Columns}
        FROM fn_ai_agent_tool_call AS call
        WHERE {ScopeClause}
        ORDER BY call.CreatedAtUtc DESC, call.Id DESC
        LIMIT @PageSize OFFSET @Offset
        """;

    public static readonly string CountCallsMySql = $"""
        SELECT COUNT(1)
        FROM fn_ai_agent_tool_call AS call
        WHERE {ScopeClause}
        """;
}
