using System.Runtime.CompilerServices;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;

namespace Full.NET.Data.Dapper;

/// <summary>
/// SQL 数据范围安全守卫（Security Guard），是多租户权限校验的最后一道防线。
/// 在 <see cref="DapperSqlExecutor"/> 构造 CommandDefinition 时强制调用，
/// 确保每条 SQL 语句的 <see cref="SqlDataScope"/> 与 <see cref="SqlTenantBinding"/>
/// 配置和当前 <see cref="ICurrentTenant"/> 上下文严格匹配。
/// </summary>
/// <remarks>
/// <para><b>安全不变量 Security Invariants：</b></para>
/// <list type="bullet">
/// <item>
/// <term>TenantRequired</term>
/// <description>必须在租户上下文内执行（IsAvailable=true 且 IsHost=false 且 Id!=null）；
/// 必须显式声明 <see cref="SqlTenantBinding.CurrentTenantId"/>；
/// SQL 文本必须在 WHERE、JOIN ON 中把租户身份列与完整参数令牌 <c>@TenantId</c>
/// 做等值比较，或在 INSERT VALUES 中写入该参数，防止注释、字符串、投影或无约束 SET 绕过检查。
/// 违反时抛出 <see cref="TenantContextMissingException"/> 或 <see cref="TenantScopeViolationException"/>。</description>
/// </item>
/// <item>
/// <term>HostOnly</term>
/// <description>必须在宿主上下文内执行（IsAvailable=true 且 IsHost=true）；
/// TenantBinding 必须为 <see cref="SqlTenantBinding.None"/>，禁止自动注入 TenantId，
/// 防止宿主管理操作误限定在某一租户范围。违反时抛出 <see cref="HostContextRequiredException"/> 或
/// <see cref="TenantScopeViolationException"/>。</description>
/// </item>
/// <item>
/// <term>Global</term>
/// <description>允许在任意上下文执行；但 TenantBinding 必须为 <see cref="SqlTenantBinding.None"/>，
/// 防止全局语句意外带上租户过滤条件。违反时抛出 <see cref="TenantScopeViolationException"/>。</description>
/// </item>
/// </list>
/// <para><b>为什么校验 @TenantId 令牌：</b>
/// 仅靠 Dapper 参数注入无法证明 SQL 真正使用了该参数。轻量词法检查会忽略注释、字符串和
/// 引号标识符，并把参数限制在项目允许的约束子句；它不替代数据库权限、SQL 评审或双库测试。</para>
/// <para><b>不可绕过性：</b>该类为 static sealed，Validate 无返回值，校验失败直接抛出异常；
/// DapperSqlExecutor.CreateCommand 在构造 CommandDefinition 之前同步调用，
/// 任何跳过此调用的执行路径都应视为安全漏洞。</para>
/// </remarks>
internal static class SqlScopeGuard
{
    private static readonly ConditionalWeakTable<SqlStatement, TenantSqlValidation>
        TenantSqlValidations = new();

    /// <summary>
    /// 校验 SQL 语句的数据范围声明与当前租户上下文是否一致。
    /// 校验失败立即抛出对应领域异常，阻止 SQL 执行。
    /// </summary>
    /// <param name="statement">待执行的 SQL 语句定义。</param>
    /// <param name="currentTenant">当前租户上下文。</param>
    /// <exception cref="TenantContextMissingException">
    /// 当 TenantRequired 语句在租户上下文缺失时执行抛出。
    /// </exception>
    /// <exception cref="TenantScopeViolationException">
    /// 当 TenantBinding 配置错误、@TenantId 占位符缺失或全局语句错误绑定租户时抛出。
    /// </exception>
    /// <exception cref="HostContextRequiredException">
    /// 当 HostOnly 语句在非宿主上下文执行时抛出。
    /// </exception>
    public static void Validate(SqlStatement statement, ICurrentTenant currentTenant)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(currentTenant);

        switch (statement.Scope)
        {
            case SqlDataScope.TenantRequired:
                if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
                {
                    throw new TenantContextMissingException(statement.Name);
                }

                if (statement.TenantBinding != SqlTenantBinding.CurrentTenantId
                    || !TenantSqlValidations.GetValue(
                        statement,
                        static value => new TenantSqlValidation(
                            TenantSqlParameterUsage.IsUsedInSafeClause(value.Text))).IsValid)
                {
                    throw new TenantScopeViolationException(statement.Name);
                }

                break;

            case SqlDataScope.HostOnly when !currentTenant.IsAvailable || !currentTenant.IsHost:
                throw new HostContextRequiredException(statement.Name);

            case SqlDataScope.Global:
            case SqlDataScope.HostOnly:
                if (statement.TenantBinding != SqlTenantBinding.None)
                {
                    throw new TenantScopeViolationException(statement.Name);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(statement), statement.Scope, "Unknown SQL data scope.");
        }
    }

    private sealed record TenantSqlValidation(bool IsValid);
}
