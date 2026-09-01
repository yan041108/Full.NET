namespace Full.NET.Modules.Identity.Contracts;

/// <summary>角色数据范围稳定机器码。</summary>
public static class RoleDataScopeKinds
{
    /// <summary>全部数据。</summary>
    public const string All = "identity.data_scope.all";

    /// <summary>当前主部门。</summary>
    public const string Organization = "identity.data_scope.org";

    /// <summary>主部门及下级机构。</summary>
    public const string OrganizationSubtree = "identity.data_scope.org_subtree";

    /// <summary>仅本人相关数据。</summary>
    public const string Self = "identity.data_scope.self";

    /// <summary>自定义机构单元集合。</summary>
    public const string Custom = "identity.data_scope.custom";

    /// <summary>全部已发布种类。</summary>
    public static IReadOnlyList<string> AllKinds { get; } =
    [
        All,
        Organization,
        OrganizationSubtree,
        Self,
        Custom,
    ];
}

/// <summary>Host 角色数据范围响应。</summary>
/// <param name="RoleId">目标角色标识。</param>
/// <param name="DataScopeKind">稳定数据范围种类机器码；参见 <see cref="RoleDataScopeKinds"/>。</param>
/// <param name="UnitIds">自定义范围下显式选择的机构单元集合；其他种类为空集合。</param>
/// <param name="Version">乐观并发版本。</param>
public sealed record HostRoleDataScopeResponse(
    Guid RoleId,
    string DataScopeKind,
    IReadOnlyList<Guid> UnitIds,
    int Version);

/// <summary>更新 Host 角色数据范围请求。</summary>
/// <param name="DataScopeKind">更新后的稳定数据范围种类机器码。</param>
/// <param name="UnitIds">自定义范围下显式选择的机构单元集合；其他种类应为 <see langword="null"/> 或空。</param>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
/// <param name="TenantId">自定义范围要求的显式目标租户；Host 作用域自定义范围必须传值。</param>
public sealed record UpdateHostRoleDataScopeRequest(
    string DataScopeKind,
    IReadOnlyList<Guid>? UnitIds,
    int Version,
    Guid? TenantId = null);
