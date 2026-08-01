namespace Full.NET.Hosting.Observability;

/// <summary>进程内诊断策略快照存储；跨实例靠版本与短 TTL 缓存收敛，不写 Outbox。</summary>
public interface IDiagnosticPolicyStore
{
    /// <summary>最近一次物化的不可变快照；热路径只读，禁止在请求中同步 IO。</summary>
    DiagnosticPolicySnapshot Current { get; }

    ValueTask<DiagnosticPolicySnapshot> GetCurrentAsync(CancellationToken cancellationToken);

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
