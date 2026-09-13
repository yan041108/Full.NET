namespace Full.NET.Modules.Ai.Contracts;

/// <summary>创建写工具审批；参数由服务端冻结，客户端不能指定审批人。</summary>
public sealed record CreateAiAgentApprovalRequest(
    Guid RunId,
    Guid OperationId,
    string ToolName,
    int ToolVersion,
    string ArgumentsJson);

/// <summary>审批创建响应。</summary>
public sealed record CreateAiAgentApprovalResponse(Guid ApprovalId);

/// <summary>审批决定请求。</summary>
public sealed record DecideAiAgentApprovalRequest(bool Approve, long ExpectedVersion);

/// <summary>可读审批摘要，包含人能理解的动作描述。</summary>
public sealed record AiAgentApprovalResponse(
    Guid Id,
    Guid RunId,
    Guid OperationId,
    string ToolName,
    int ToolVersion,
    string ArgumentsHash,
    string DecisionKey,
    string ActionSummary,
    string TargetSummary,
    string ChangeSummary,
    string ScopeSummary,
    decimal? CostCeiling,
    string? Currency,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    long Version,
    DateTimeOffset CreatedAtUtc);
