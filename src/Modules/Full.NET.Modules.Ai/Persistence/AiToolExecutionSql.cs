using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>显式工具查询与回执只读写 Ai 所有者数据。</summary>
internal static class AiToolExecutionSql
{
    private const string ModelSelect = """
        SELECT Id, Name, ProviderKey, ModelId FROM fn_ai_model_config
        WHERE IsEnabled = 1 AND ((@ScopeTenantId IS NULL AND TenantId IS NULL) OR TenantId = @ScopeTenantId)
        ORDER BY Id
        """;
    public static readonly SqlStatement ListModelsSqlServer = new("ai.tool_list_models.sqlserver",
        ModelSelect + " OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.Global);
    public static readonly SqlStatement ListModelsMySql = new("ai.tool_list_models.mysql",
        ModelSelect + " LIMIT @PageSize OFFSET @Offset", SqlDataScope.Global);
    public static readonly SqlStatement Complete = new("ai.complete_tool_operation", """
        UPDATE fn_ai_agent_tool_call SET StatusKey = @StatusKey, ErrorCode = @ErrorCode,
            OutputSummary = @OutputSummary, DurationMs = @DurationMs
        WHERE Id = @OperationId AND ActorUserId = @ActorUserId AND StatusKey = 'started'
          AND ((@ScopeTenantId IS NULL AND TenantId IS NULL) OR TenantId = @ScopeTenantId)
        """, SqlDataScope.Global);

    public static readonly SqlStatement TransitionDeniedApprovalToStarted = new("ai.transition_denied_approval_to_started", """
        UPDATE fn_ai_agent_tool_call
        SET StatusKey = 'started',
            ErrorCode = NULL,
            InputSummary = @InputSummary,
            ApprovalId = @ApprovalId,
            CreatedAtUtc = @Now
        WHERE Id = @OperationId
          AND ActorUserId = @ActorUserId
          AND StatusKey = 'denied'
          AND ErrorCode = 'ai.tool.approval_required'
          AND ((@ScopeTenantId IS NULL AND TenantId IS NULL) OR TenantId = @ScopeTenantId)
        """, SqlDataScope.Global);

    public static readonly SqlStatement TouchExistingCall = new("ai.touch_existing_tool_call", """
        UPDATE fn_ai_agent_tool_call
        SET StatusKey = StatusKey
        WHERE Id = @OperationId
          AND ActorUserId = @ActorUserId
          AND ((@ScopeTenantId IS NULL AND TenantId IS NULL) OR TenantId = @ScopeTenantId)
        """, SqlDataScope.Global);
}
