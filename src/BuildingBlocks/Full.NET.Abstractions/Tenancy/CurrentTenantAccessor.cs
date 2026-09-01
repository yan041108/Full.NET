namespace Full.NET.Abstractions.Tenancy;

/// <summary>
/// 租户上下文的默认 Scoped 存储实现；通过显式接口实现隐藏写能力，
/// 仅允许经授权的基础设施通过 <see cref="ICurrentTenantContextWriter"/> 修改上下文。
/// </summary>
/// <remarks>
/// 线程安全：该类型按 Scoped 生命周期注册，单个请求/作用域内不会并发写入；
/// 跨作用域克隆上下文时应复制字段值而非复用同一实例，避免子作用域回写污染父级。
/// </remarks>
public sealed class CurrentTenantAccessor : ICurrentTenant, ICurrentTenantContextWriter
{
    private TenantContext? _tenant;

    /// <inheritdoc />
    public bool IsAvailable => IsHost || _tenant is not null;

    /// <inheritdoc />
    public bool IsHost { get; private set; }

    /// <inheritdoc />
    public Guid? Id => _tenant?.Id;

    /// <inheritdoc />
    public string? Identifier => _tenant?.Identifier;

    /// <inheritdoc />
    public string? Name => _tenant?.Name;

    /// <summary>
    /// 内部便捷入口：绑定到已验证的租户上下文，并清除 Host 标记。
    /// </summary>
    internal void SetTenant(TenantContext tenant)
    {
        _tenant = tenant;
        IsHost = false;
    }

    /// <summary>
    /// 内部便捷入口：切换到 Host 上下文，并清除已绑定的租户信息。
    /// </summary>
    internal void SetHost()
    {
        _tenant = null;
        IsHost = true;
    }

    /// <summary>
    /// 内部便捷入口：清除租户与 Host 状态，返回未解析的初始态。
    /// </summary>
    internal void Clear()
    {
        _tenant = null;
        IsHost = false;
    }

    /// <inheritdoc />
    void ICurrentTenantContextWriter.SetTenant(TenantContext tenant) => SetTenant(tenant);

    /// <inheritdoc />
    void ICurrentTenantContextWriter.SetHost() => SetHost();

    /// <inheritdoc />
    void ICurrentTenantContextWriter.Clear() => Clear();
}
