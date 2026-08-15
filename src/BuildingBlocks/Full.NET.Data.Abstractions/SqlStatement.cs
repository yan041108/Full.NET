namespace Full.NET.Data.Abstractions;

/// <summary>
/// 携带 Scope + Binding 元数据的 SQL 语句不可变包装器，是数据访问层的核心执行单元。
/// </summary>
/// <remarks>
/// <para>
/// 设计意图：将原本裸字符串 SQL 提升为包含租户边界声明的强类型对象，配合 SqlScopeGuard
/// 在执行前进行静态安全校验。所有属性在构造时一次性初始化，record 语义保证不可变性，
/// 避免执行途中被修改导致校验失效。
/// </para>
/// <para>
/// 不变量约束：
/// 1) Name 非空且在当前模块内唯一，用于异常定位与性能观测；
/// 2) Text 为有效 SQL，当 Scope = TenantRequired 时必须显式包含 @TenantId 参数；
/// 3) Scope 与 TenantBinding 的组合必须符合 SqlScopeGuard 合法矩阵。
/// </para>
/// </remarks>
public sealed record SqlStatement(
    /// <summary>
    /// 语句唯一标识名称，用于日志追踪、异常报告和性能指标标记。
    /// </summary>
    /// <remarks>
    /// 建议采用 {模块}.{实体}.{操作} 命名规范，例如 "organization.unit.insert"。
    /// 该值不参与 SQL 执行，仅作为可观测性元数据。
    /// </remarks>
    string Name,

    /// <summary>
    /// 原始 SQL 文本（含 Dapper @Param 占位符），由执行器原样传递给数据库 Provider。
    /// </summary>
    /// <remarks>
    /// 当 <see cref="Scope"/> = TenantRequired 时，必须显式包含 @TenantId 参数占位，
    /// 否则 SqlScopeGuard 将拒绝执行。请勿通过字符串拼接构造该属性，所有动态条件
    /// 应通过 parameters 参数传递。
    /// </remarks>
    string Text,

    /// <summary>
    /// 数据访问边界声明，决定该语句允许在何种上下文中执行。
    /// </summary>
    SqlDataScope Scope,

    /// <summary>
    /// 租户参数绑定策略，决定是否自动注入 @TenantId 参数值。
    /// </summary>
    SqlTenantBinding TenantBinding)
{
    /// <summary>
    /// 使用默认 None 绑定构造语句，适用于 Global 或 HostOnly Scope。
    /// </summary>
    /// <param name="Name">语句唯一标识名称。</param>
    /// <param name="Text">原始 SQL 文本。</param>
    /// <param name="Scope">数据访问边界声明。</param>
    public SqlStatement(
        string Name,
        string Text,
        SqlDataScope Scope)
        : this(Name, Text, Scope, SqlTenantBinding.None)
    {
    }

    /// <summary>
    /// 三元解构器，用于不需要 TenantBinding 的场景（如多结果集投影器）。
    /// </summary>
    /// <param name="Name">语句名称输出。</param>
    /// <param name="Text">SQL 文本输出。</param>
    /// <param name="Scope">数据边界输出。</param>
    public void Deconstruct(
        out string Name,
        out string Text,
        out SqlDataScope Scope)
    {
        Name = this.Name;
        Text = this.Text;
        Scope = this.Scope;
    }
}
