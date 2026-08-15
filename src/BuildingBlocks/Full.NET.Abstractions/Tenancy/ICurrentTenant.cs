namespace Full.NET.Abstractions.Tenancy;

/// <summary>
/// 表示当前请求上下文的租户信息访问器，提供租户边界判定的只读契约。
/// </summary>
/// <remarks>
/// 该接口为 Scoped 生命周期；在请求未解析租户或处于 Host 级别时，
/// <see cref="Id"/> 可能为 <see langword="null"/>，此时应通过 <see cref="IsAvailable"/>
/// 或 <see cref="IsHost"/> 判定业务分支，避免直接访问可空字段。
/// </remarks>
public interface ICurrentTenant
{
    /// <summary>
    /// 获取一个值，指示当前上下文是否已解析出有效租户（非 Host 且租户标识存在）。
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 获取一个值，指示当前请求是否处于 Host（宿主管理）上下文，而非具体租户。
    /// </summary>
    bool IsHost { get; }

    /// <summary>
    /// 获取当前租户的稳定唯一标识；Host 上下文或未解析时为 <see langword="null"/>。
    /// </summary>
    Guid? Id { get; }

    /// <summary>
    /// 获取当前租户的可读标识符（如域名或代码）；未解析时为 <see langword="null"/>。
    /// </summary>
    string? Identifier { get; }

    /// <summary>
    /// 获取当前租户的显示名称；未解析时为 <see langword="null"/>。
    /// </summary>
    string? Name { get; }
}
