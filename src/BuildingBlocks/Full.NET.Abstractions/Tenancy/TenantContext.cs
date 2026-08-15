namespace Full.NET.Abstractions.Tenancy;

/// <summary>
/// 表示已解析的租户上下文，封装唯一标识与显示信息，用于在 Scoped 服务间传递。
/// </summary>
/// <remarks>
/// 该 record 为不可变值语义；创建后代表一次成功的租户解析，字段均不可为 <see langword="null"/> 或空。
/// Host 级别操作不使用该类型，而是通过 <see cref="ICurrentTenant.IsHost"/> 判定。
/// </remarks>
public sealed record TenantContext(
    /// <summary>
    /// 租户的稳定唯一标识，用于数据库过滤和跨服务关联。
    /// </summary>
    Guid Id,
    /// <summary>
    /// 租户的可读标识符，如域名前缀、短代码或外部系统编号，用于路由与展示。
    /// </summary>
    string Identifier,
    /// <summary>
    /// 租户的显示名称，用于 UI 展示和日志输出。
    /// </summary>
    string Name);
