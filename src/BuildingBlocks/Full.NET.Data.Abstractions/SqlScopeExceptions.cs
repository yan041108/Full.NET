namespace Full.NET.Data.Abstractions;

/// <summary>
/// 当 <see cref="SqlDataScope.TenantRequired"/> 语句执行时当前调用链缺失有效租户上下文，
/// 由 SqlScopeGuard 抛出。属于配置或调用方错误，不可重试。
/// </summary>
/// <remarks>
/// 典型触发场景：
/// 1) 后台 Job 未正确初始化租户上下文却直接调用租户级 Repository；
/// 2) Host 级用例错误引用了 TenantRequired 的 SQL 语句；
/// 3) 单元测试未 Mock 租户访问器。
/// 捕获该异常后应修正调用上下文，而非降级执行。
/// </remarks>
public sealed class TenantContextMissingException(string statementName)
    : InvalidOperationException($"SQL statement '{statementName}' requires a tenant context.");

/// <summary>
/// 当 SQL 声明的 <see cref="SqlDataScope"/> 与 <see cref="SqlTenantBinding"/> 组合非法，
/// 或 TenantRequired 语句 SQL 文本遗漏 @TenantId 参数占位时，由 SqlScopeGuard 抛出。
/// 属于开发期配置错误，不可重试。
/// </summary>
/// <remarks>
/// <para>该异常对应语义等价于用户描述的 ScopeMismatch，核心不变量如下：</para>
/// <list type="bullet">
/// <item>TenantRequired Scope 必须搭配 CurrentTenantId Binding，反之亦然；</item>
/// <item>Global / HostOnly Scope 必须搭配 None Binding；</item>
/// <item>TenantRequired 的 SQL 文本必须显式包含 @TenantId 参数占位符。</item>
/// </list>
/// <para>一旦抛出即代表 SQL 配置存在安全漏洞，必须在发布前修复。</para>
/// </remarks>
public sealed class TenantScopeViolationException(string statementName)
    : InvalidOperationException(
        $"SQL statement '{statementName}' declares an invalid tenant binding or omits the @TenantId parameter.");

/// <summary>
/// 当 <see cref="SqlDataScope.HostOnly"/> 语句在非 Host 上下文中执行时，由 SqlScopeGuard
/// 抛出。对应语义等价于用户描述的 HostRequired，表示越权调用 Host 专属数据路径。
/// </summary>
/// <remarks>
/// 典型触发场景：普通租户用户被错误路由到 Host 管理接口，或前端权限校验放行后
/// 后端再次触发数据库级兜底拦截。该异常是多租户权限的最后一道防线，捕获后
/// 应统一转换为 403 Forbidden，切勿静默降级。
/// </remarks>
public sealed class HostContextRequiredException(string statementName)
    : InvalidOperationException($"SQL statement '{statementName}' requires the host context.");
