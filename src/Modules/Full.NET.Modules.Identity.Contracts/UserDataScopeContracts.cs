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
