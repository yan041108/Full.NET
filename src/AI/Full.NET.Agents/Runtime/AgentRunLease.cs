namespace Full.NET.Agents.Runtime;

/// <summary>租约代次与行版本共同约束写入；失去租约后不得提交步骤或检查点。</summary>
/// <param name="RunId">Agent 运行实例的稳定标识。</param>
/// <param name="WorkerId">持有租约的 Worker 实例标识；用于续租与冲突检测。</param>
/// <param name="Epoch">租约代次；每次续租递增，旧代次写入将被拒绝。</param>
/// <param name="Version">数据库行版本；用于 CAS 并发控制，防止过期租约覆盖。</param>
/// <param name="ExpiresAtUtc">租约到期时间（UTC）；到期后必须重新领取才能写入。</param>
public sealed record AgentRunLease(Guid RunId, string WorkerId, long Epoch, long Version, DateTimeOffset ExpiresAtUtc);
