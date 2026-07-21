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
public sealed record HostRoleDataScopeResponse(
    Guid RoleId,
    string DataScopeKind,
    IReadOnlyList<Guid> UnitIds,
    int Version);

/// <summary>更新 Host 角色数据范围请求。</summary>
public sealed record UpdateHostRoleDataScopeRequest(
    string DataScopeKind,
    IReadOnlyList<Guid>? UnitIds,
    int Version);
