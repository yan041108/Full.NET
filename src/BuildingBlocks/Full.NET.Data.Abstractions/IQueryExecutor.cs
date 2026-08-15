namespace Full.NET.Data.Abstractions;

/// <summary>
/// 数据读路径核心执行器：封装单结果集查询，统一注入 SqlScopeGuard、
/// 命令超时、租户参数与诊断追踪，不暴露底层 Dapper / IDataReader。
/// </summary>
/// <remarks>
/// <para>
/// 设计意图：Repository 层的所有读操作必须通过本接口，确保租户过滤条件被
/// SqlScopeGuard 强制校验。该接口仅支持单结果集；多结果集（例如 JOIN 拆分的
/// 一对多投影）请使用 <see cref="IMultiResultQueryExecutor"/>。
/// </para>
/// <para>
/// 物化策略：默认使用 Dapper 的基于列名映射的无跟踪物化。如需跟踪或复杂投影，
/// 应在上层 Repository 中组合结果，不得在此接口扩展重载。
/// </para>
/// </remarks>
public interface IQueryExecutor
{
    /// <summary>
    /// 查询期望返回 0 或 1 行；超过 1 行时由 Dapper 抛出 InvalidOperationException。
    /// </summary>
    /// <typeparam name="T">行投影的目标类型，通常为 POCO record。</typeparam>
    /// <param name="statement">携带 Scope/Binding 元数据的 SQL 语句包装器。</param>
    /// <param name="parameters">Dapper 参数对象；无参数时为 null。</param>
    /// <param name="cancellationToken">用于在命令执行中取消的令牌。</param>
    /// <returns>唯一行对象；结果集为空时返回 <see langword="default"/>。</returns>
    /// <exception cref="TenantContextMissingException">Scope 要求租户但上下文缺失。</exception>
    /// <exception cref="TenantScopeViolationException">Scope 与 Binding 组合非法或 SQL 遗漏参数。</exception>
    /// <exception cref="HostContextRequiredException">HostOnly 语句在非 Host 上下文执行。</exception>
    Task<T?> QuerySingleOrDefaultAsync<T>(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询并物化全部行，按数据库返回顺序返回只读列表。
    /// </summary>
    /// <typeparam name="T">行投影的目标类型，通常为 POCO record。</typeparam>
    /// <param name="statement">携带 Scope/Binding 元数据的 SQL 语句包装器。</param>
    /// <param name="parameters">Dapper 参数对象；无参数时为 null。</param>
    /// <param name="cancellationToken">用于在命令执行中取消的令牌。</param>
    /// <returns>按数据库返回顺序物化的只读行集合；空结果集返回空列表，不为 null。</returns>
    /// <exception cref="TenantContextMissingException">Scope 要求租户但上下文缺失。</exception>
    /// <exception cref="TenantScopeViolationException">Scope 与 Binding 组合非法或 SQL 遗漏参数。</exception>
    /// <exception cref="HostContextRequiredException">HostOnly 语句在非 Host 上下文执行。</exception>
    Task<IReadOnlyList<T>> QueryAsync<T>(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default);
}
