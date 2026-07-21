namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 租户用户-机构隶属管理 API 契约。
/// </summary>
public static class OrganizationUserUnitManagementPermissions
{
    /// <summary>分页查询用户-机构隶属。</summary>
    public const string Read = "organization.user_units.read";

    /// <summary>分配、设主部门与取消隶属。</summary>
    public const string Write = "organization.user_units.write";
}

/// <summary>创建用户-机构隶属请求。</summary>
public sealed record CreateOrganizationUserUnitRequest(
    Guid UserId,
    Guid UnitId,
    bool IsPrimary);

/// <summary>更新用户-机构隶属请求。</summary>
public sealed record UpdateOrganizationUserUnitRequest(
    bool IsPrimary,
    int Version);

/// <summary>用户-机构隶属列表项与详情。</summary>
public sealed record OrganizationUserUnitResponse(
    Guid Id,
    Guid UserId,
    string Username,
    string DisplayName,
    Guid UnitId,
    string UnitCode,
    string UnitName,
    bool IsPrimary,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);
