namespace Full.NET.Data.Abstractions;

/// <summary>
/// 描述 SQL 语句与当前租户上下文的绑定方式，决定执行时是否自动注入 @TenantId 参数值。
/// </summary>
/// <remarks>
/// 该枚举与 <see cref="SqlDataScope"/> 形成互补：Scope 定义允许的访问边界，
/// Binding 决定参数注入行为。SqlScopeGuard 会联合校验二者的合法组合，例如
/// TenantRequired Scope 必须搭配 CurrentTenantId Binding，否则视为越权配置。
/// </remarks>
public enum SqlTenantBinding
{
    /// <summary>
    /// 不绑定租户上下文，执行时不注入 @TenantId 参数值。
    /// </summary>
    /// <remarks>
    /// 适用于 Global 和 HostOnly Scope 的语句。此时 SQL 文本中若出现 @TenantId
    /// 参数占位将被 SqlScopeGuard 视为配置错误并拒绝执行，避免开发者误将租户过滤
    /// 条件混入全局/Host 级查询。
    /// </remarks>
    None = 0,

    /// <summary>
    /// 绑定当前租户上下文，由执行器在参数集合中自动填充 @TenantId = 当前租户 Id。
    /// </summary>
    /// <remarks>
    /// 仅当 <see cref="SqlDataScope"/> = TenantRequired 时合法。SqlScopeGuard
    /// 会确保当前调用链上存在非空租户上下文，否则抛出 TenantContextMissingException。
    /// 该机制强制业务语句显式携带租户过滤，消除遗漏 WHERE TenantId = @TenantId 的风险。
    /// </remarks>
    CurrentTenantId = 1,
}
