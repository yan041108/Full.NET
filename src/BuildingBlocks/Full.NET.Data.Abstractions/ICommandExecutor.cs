namespace Full.NET.Data.Abstractions;

/// <summary>
/// 数据写路径核心执行器：封装 INSERT / UPDATE / DELETE / DDL 等非查询命令，
/// 统一注入 SqlScopeGuard、命令超时、租户参数与诊断追踪。
/// </summary>
/// <remarks>
/// <para>
/// 设计意图：Repository 层不得直接操作 Dapper / IDbConnection，必须通过本接口
/// 执行写命令，以保证：
/// 1) 所有写操作经过 SqlScopeGuard 校验 Scope + Binding 合法组合；
/// 2) 租户级语句自动注入 @TenantId 参数值；
/// 3) 命令超时、观测打点、异常分类（见 <see cref="DataCommandException"/>）统一处理。
/// </para>
/// <para>
/// 事务语义：该接口自身不管理事务边界，显式事务通过调用方传入的 IDbTransaction
/// 参数（通常由 ICommandTransaction 协调器封装）。默认无事务时每条命令自动提交。
/// </para>
/// </remarks>
public interface ICommandExecutor
{
    /// <summary>
    /// 执行写命令并返回受影响行数。
    /// </summary>
    /// <param name="statement">携带 Scope/Binding 元数据的 SQL 语句包装器。</param>
    /// <param name="parameters">Dapper 参数对象（匿名类 / DynamicParameters）；无参数时为 null。</param>
    /// <param name="cancellationToken">用于在命令执行中取消的令牌；会同步取消已打开的 Reader。</param>
    /// <returns>数据库报告的受影响行数；对于 SET NOCOUNT ON 场景可能为 -1。</returns>
    /// <exception cref="TenantContextMissingException">Scope 要求租户但上下文缺失。</exception>
    /// <exception cref="TenantScopeViolationException">Scope 与 Binding 组合非法或 SQL 遗漏参数。</exception>
    /// <exception cref="HostContextRequiredException">HostOnly 语句在非 Host 上下文执行。</exception>
    /// <exception cref="DataCommandException">
    /// 已识别的稳定失败类别（如唯一约束冲突），业务层可据此决定是否重试。
    /// </exception>
    Task<int> ExecuteAsync(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default);
}
