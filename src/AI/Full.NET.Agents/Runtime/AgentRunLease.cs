namespace Full.NET.Agents.Runtime;

/// <summary>租约代次与行版本共同约束写入；失去租约后不得提交步骤或检查点。</summary>
public sealed record AgentRunLease(Guid RunId, string WorkerId, long Epoch, long Version, DateTimeOffset ExpiresAtUtc);
