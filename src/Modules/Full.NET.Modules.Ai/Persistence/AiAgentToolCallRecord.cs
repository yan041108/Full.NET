namespace Full.NET.Modules.Ai.Persistence;

/// <summary>Agent Tool 调用审计记录。</summary>
internal sealed class AiAgentToolCallRecord
{
    public Guid Id { get; init; }

    public Guid? TenantId { get; init; }

    public Guid ActorUserId { get; init; }

    public string ToolName { get; init; } = string.Empty;

    public string PermissionCode { get; init; } = string.Empty;

    public string StatusKey { get; init; } = string.Empty;

    public int? DurationMs { get; init; }

    public string InputSummary { get; init; } = string.Empty;

    public string? OutputSummary { get; init; }

    public string? ErrorCode { get; init; }

    public string? TraceId { get; init; }

    public Guid? RunId { get; init; }

    public string? ArgumentsHash { get; init; }

    public Guid? ApprovalId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}
