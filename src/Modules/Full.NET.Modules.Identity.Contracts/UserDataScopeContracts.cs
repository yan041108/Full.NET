namespace Full.NET.Modules.Identity.Contracts;

/// <summary>单个活动角色的数据范围描述。</summary>
/// <param name="RoleId">授予该数据范围的活动角色标识。</param>
/// <param name="DataScopeKind">稳定数据范围种类机器码；参见 <see cref="RoleDataScopeKinds"/>。</param>
public sealed record RoleDataScopeEntry(
    Guid RoleId,
    string DataScopeKind);

/// <summary>用户有效数据范围；并集语义由 <see cref="IDataScopeSqlFilterBuilder"/> 实现。</summary>
/// <param name="IsUnrestricted">是否不受任何数据范围限制；超级管理员或拥有 all 范围角色时为 true。</param>
/// <param name="RoleScopes">用户活动角色中实际生效的数据范围集合；不受限时仍可用于审计日志展示。</param>
public sealed record EffectiveUserDataScope(
    bool IsUnrestricted,
    IReadOnlyList<RoleDataScopeEntry> RoleScopes);

/// <summary>参数化 SQL 过滤片段，供业务查询追加到租户作用域 WHERE 子句。</summary>
/// <param name="Sql">合法的参数化 SQL 片段；必须使用占位符而非字符串拼接列值。</param>
/// <param name="Parameters">与 SQL 占位符对应的匿名对象参数；无参数时为 <see langword="null"/>。</param>
public sealed record DataScopeSqlFilter(
    string Sql,
    object? Parameters);

/// <summary>
/// 为 Identity 数据范围组合器提供 Organization 自有表上的 SQL 投影。
/// </summary>
/// <remarks>
/// 该端口由消费方 Identity 定义、Organization 在同一进程内实现，避免 Identity
/// 直接依赖机构表结构；调用方传入的列名只能来自服务端固定查询，不能接受请求输入。
/// </remarks>
public interface IIdentityOrganizationDataScopeSqlProjection
{
    /// <summary>
    /// 构建当前用户在机构单元列上的受限范围；仅接受 self、organization 和
    /// organization_subtree 三种由 Organization 拥有的范围。
    /// </summary>
    /// <param name="dataScopeKind">稳定数据范围种类机器码。</param>
    /// <param name="unitIdColumn">业务查询中机构单元 Id 列的限定名；只接受调用方白名单。</param>
    /// <param name="currentUserId">当前访问者用户标识；用于 self 范围按人归并。</param>
    /// <returns>可直接追加到 WHERE 的参数化 SQL 过滤片段。</returns>
    DataScopeSqlFilter BuildOrganizationUnitFilter(
        string dataScopeKind,
        string unitIdColumn,
        Guid currentUserId);
}

/// <summary>解析当前用户活动角色的数据范围种类。</summary>
public interface IUserDataScopeResolver
{
    /// <summary>
    /// 加载用户活动 Host 角色数据范围；超级管理员令牌或任一 <c>all</c> 范围视为不受限。
    /// </summary>
    /// <param name="userId">待解析的用户标识。</param>
    /// <param name="isSuperAdministrator">调用方已验证的超级管理员标记；传入 true 时直接返回不受限。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>组合完成的有效数据范围摘要；可直接进入 <see cref="IDataScopeSqlFilterBuilder"/>。</returns>
    Task<EffectiveUserDataScope> ResolveAsync(
        Guid userId,
        bool isSuperAdministrator,
        CancellationToken cancellationToken = default);
}

/// <summary>将有效数据范围转换为机构单元列上的参数化 SQL 条件。</summary>
public interface IDataScopeSqlFilterBuilder
{
    /// <summary>
    /// 构建机构单元 Id 列过滤；返回 <see langword="null"/> 表示不追加限制。
    /// </summary>
    /// <param name="scope">已解析完成的用户有效数据范围。</param>
    /// <param name="unitIdColumn">业务查询中机构单元 Id 列的限定名；只接受调用方白名单。</param>
    /// <param name="currentUserId">当前访问者用户标识；用于 self 范围按人归并。</param>
    /// <returns>可直接追加到 WHERE 的参数化过滤；不受限或无对应范围时返回 <see langword="null"/>。</returns>
    DataScopeSqlFilter? BuildOrganizationUnitFilter(
        EffectiveUserDataScope scope,
        string unitIdColumn,
        Guid currentUserId);
}
