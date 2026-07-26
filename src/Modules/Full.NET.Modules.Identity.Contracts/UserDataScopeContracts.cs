namespace Full.NET.Modules.Identity.Contracts;

/// <summary>单个活动角色的数据范围描述。</summary>
public sealed record RoleDataScopeEntry(
    Guid RoleId,
    string DataScopeKind);

/// <summary>用户有效数据范围；并集语义由 <see cref="IDataScopeSqlFilterBuilder"/> 实现。</summary>
public sealed record EffectiveUserDataScope(
    bool IsUnrestricted,
    IReadOnlyList<RoleDataScopeEntry> RoleScopes);

/// <summary>参数化 SQL 过滤片段，供业务查询追加到租户作用域 WHERE 子句。</summary>
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
    DataScopeSqlFilter? BuildOrganizationUnitFilter(
        EffectiveUserDataScope scope,
        string unitIdColumn,
        Guid currentUserId);
}
