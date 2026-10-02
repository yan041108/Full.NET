namespace Full.NET.Hosting.Observability;

/// <summary>进程内诊断策略快照存储；跨实例靠版本与短 TTL 缓存收敛，不写 Outbox。</summary>
public interface IDiagnosticPolicyStore
{
    /// <summary>最近一次物化的不可变快照；热路径只读，禁止在请求中同步 IO。</summary>
    DiagnosticPolicySnapshot Current { get; }

    /// <summary>获取当前诊断策略快照，必要时触发一次刷新。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前进程持有的不可变诊断策略快照；永不为 null。</returns>
    ValueTask<DiagnosticPolicySnapshot> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>要求刷新到不低于指定版本的策略快照。</summary>
    /// <param name="minimumVersion">期望的最低策略版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示刷新完成的 ValueTask；若源不可用则保留旧快照并通过异常暴露失败。</returns>
    /// <remarks>跨实例靠版本与短 TTL 缓存收敛，不写 Outbox；刷新非阻塞，调用方不得依赖强一致。</remarks>
    ValueTask RefreshAsync(long minimumVersion, CancellationToken cancellationToken);
}

/// <summary>Hosting 默认安全实现：始终返回生产安全默认值，直到 Settings 替换注册。</summary>
public sealed class DefaultDiagnosticPolicyStore : IDiagnosticPolicyStore
{
    public DiagnosticPolicySnapshot Current =>
        DiagnosticPolicySnapshot.CreateDefault(DateTimeOffset.UtcNow);

    public ValueTask<DiagnosticPolicySnapshot> GetCurrentAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Current);

    public ValueTask RefreshAsync(long minimumVersion, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
