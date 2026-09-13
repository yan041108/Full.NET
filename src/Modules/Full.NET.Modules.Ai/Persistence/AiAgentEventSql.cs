using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>运行事件只读查询；写入仍由 AiAgentRunSql.InsertEvent 负责。</summary>
internal static class AiAgentEventSql
{
    public static readonly SqlStatement ListAfterSequenceSqlServer = new("ai.agent_event.list_after.sqlserver", """
        SELECT TOP (@Limit) Sequence, EventType, PayloadVersion, Payload, CreatedAtUtc
        FROM fn_ai_agent_event
        WHERE RunId = @RunId AND Sequence > @AfterSequence
        ORDER BY Sequence
        """, SqlDataScope.Global);

    public static readonly SqlStatement ListAfterSequenceMySql = new("ai.agent_event.list_after.mysql", """
        SELECT Sequence, EventType, PayloadVersion, Payload, CreatedAtUtc
        FROM fn_ai_agent_event
        WHERE RunId = @RunId AND Sequence > @AfterSequence
        ORDER BY Sequence
        LIMIT @Limit
        """, SqlDataScope.Global);

    public static readonly SqlStatement MaxSequence = new("ai.agent_event.max_sequence", """
        SELECT COALESCE(MAX(Sequence), 0) AS MaxSequence
        FROM fn_ai_agent_event
        WHERE RunId = @RunId
        """, SqlDataScope.Global);

    public static readonly SqlStatement ListSteps = new("ai.agent_step.list_by_run", """
        SELECT StepKey, Attempt, StatusKey, InputTokens, OutputTokens, ErrorCode
        FROM fn_ai_agent_step
        WHERE RunId = @RunId
        ORDER BY CreatedAtUtc, Id
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindBudgetByRunSqlServer = new("ai.operation_budget.find_by_run.sqlserver", """
        SELECT TOP (1) InputTokens, OutputTokens, UsageStatus, Outcome
        FROM fn_ai_operation_budget
        WHERE RunId = @RunId AND ScopeKey = @ScopeKey
        ORDER BY CreatedAtUtc DESC
        """, SqlDataScope.Global);

    public static readonly SqlStatement FindBudgetByRunMySql = new("ai.operation_budget.find_by_run.mysql", """
        SELECT InputTokens, OutputTokens, UsageStatus, Outcome
        FROM fn_ai_operation_budget
        WHERE RunId = @RunId AND ScopeKey = @ScopeKey
        ORDER BY CreatedAtUtc DESC
        LIMIT 1
        """, SqlDataScope.Global);
}
