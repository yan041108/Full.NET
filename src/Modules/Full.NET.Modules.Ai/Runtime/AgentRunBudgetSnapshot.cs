namespace Full.NET.Modules.Ai.Runtime;

/// <summary>冻结在运行记录中的预算与执行参数；不保存凭据或完整会话。</summary>
internal sealed record AgentRunBudgetSnapshot(
    Guid ModelConfigId,
    string Prompt,
    long InputTokenLimit,
    long OutputTokenLimit);
