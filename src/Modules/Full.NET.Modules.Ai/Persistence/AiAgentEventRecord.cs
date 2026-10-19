/// <summary>Ai 持久化行投影；仅供 Dapper 物化，不直接作为 HTTP 契约。</summary>
namespace Full.NET.Modules.Ai.Persistence;

internal sealed class AiAgentEventRecord
{
    public long Sequence { get; init; }
    public string EventType { get; init; } = string.Empty;
    public int PayloadVersion { get; init; }
    public string Payload { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
}

internal sealed class AiAgentEventSequenceBounds
{
    public long MaxSequence { get; init; }
}

internal sealed class AiAgentStepSummaryRecord
{
    public string StepKey { get; init; } = string.Empty;
    public int Attempt { get; init; }
    public string StatusKey { get; init; } = string.Empty;
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public string? ErrorCode { get; init; }
}

internal sealed class AiAgentRunBudgetSummaryRecord
{
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public string UsageStatus { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
}
