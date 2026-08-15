namespace Full.NET.Data.Abstractions;

/// <summary>
/// 定义 SQL 语句在多租户架构下的数据访问边界，由 SqlScopeGuard 在执行前校验。
/// </summary>
/// <remarks>
/// 该枚举是租户隔离的第一道防线，配合 <see cref="SqlTenantBinding"/> 共同决定
/// 执行时是否注入 @TenantId 参数以及允许的上下文类型。任何越权访问都会在
/// SqlScopeGuard 中抛出对应的强类型异常，避免 SQL 文本层面遗漏租户过滤条件。
/// </remarks>
public enum SqlDataScope
{
    /// <summary>
    /// 跨租户全局数据，不允许携带 @TenantId 过滤条件。
    /// </summary>
    /// <remarks>
    /// 适用于系统级配置、Host 级元数据表等共享数据。SqlScopeGuard 会确保
    /// 当前语句未绑定 CurrentTenantId，且 SQL 文本中不包含 @TenantId 参数占位。
    /// </remarks>
    Global,

    /// <summary>
    /// 必须在租户上下文内执行，要求 SQL 文本显式携带 @TenantId 过滤参数。
    /// </summary>
    /// <remarks>
    /// 这是业务数据表的默认 Scope。SqlScopeGuard 会在执行前验证：
    /// 1) 当前存在有效的租户上下文（非 Host 上下文）；
    /// 2) <see cref="SqlTenantBinding"/> 为 CurrentTenantId；
    /// 3) SQL 文本包含 @TenantId 参数占位。
    /// 违反任一不变量均会抛出异常，杜绝跨租户数据泄漏。
    /// </remarks>
    TenantRequired,

    /// <summary>
    /// 仅限 Host（超级管理员）上下文访问，禁止在普通租户上下文中执行。
    /// </summary>
    /// <remarks>
    /// 适用于租户管理、配额配置、全局报表等 Host 专属操作。SqlScopeGuard
    /// 会验证当前处于 Host 上下文，且语句未绑定 CurrentTenantId。
    /// </remarks>
    HostOnly,
}
