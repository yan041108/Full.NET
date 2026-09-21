namespace Full.NET.Modules.Identity.Contracts;

/// <summary>用户注册策略与注册方式管理相关稳定常量。</summary>
/// <remarks>字符串常量发布后不可改名或删除；新增常量只能追加，已发布常量不得调整顺序。</remarks>
public static class IdentityRegistrationPolicyConstants
{
    /// <summary>注册策略单例行稳定标识；与迁移种子保持一致。</summary>
    public static readonly Guid PolicyId =
        Guid.Parse("00000000-0000-4000-8000-000000000001");
}

/// <summary>注册策略管理权限码。</summary>
/// <remarks>权限码字符串发布后不可改名或删除；新增权限只能追加，已发布权限码不得调整顺序。</remarks>
public static class IdentityRegistrationPolicyPermissions
{
    /// <summary>读取注册策略。</summary>
    public const string Read = "identity.registration_policy.read";

    /// <summary>更新注册策略。</summary>
    public const string Update = "identity.registration_policy.update";
}

/// <summary>注册方式管理权限码。</summary>
/// <remarks>权限码字符串发布后不可改名或删除；新增权限只能追加，已发布权限码不得调整顺序。</remarks>
public static class IdentityRegistrationWayPermissions
{
    /// <summary>分页查询注册方式。</summary>
    public const string Read = "identity.registration_ways.read";

    /// <summary>创建注册方式。</summary>
    public const string Create = "identity.registration_ways.create";

    /// <summary>更新注册方式。</summary>
    public const string Update = "identity.registration_ways.update";

    /// <summary>删除注册方式。</summary>
    public const string Delete = "identity.registration_ways.delete";
}

/// <summary>注册策略响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">策略稳定标识。</param>
/// <param name="IsPublicRegistrationEnabled">是否允许匿名用户查看启用的注册方式；与 <see cref="RegistrationMode"/> 为 Open 时保持一致。</param>
/// <param name="RegistrationMode">注册三态模式。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record RegistrationPolicyResponse(
    Guid Id,
    bool IsPublicRegistrationEnabled,
    IdentityRegistrationMode RegistrationMode,
    DateTimeOffset UpdatedAtUtc,
    int Version);

/// <summary>更新注册策略请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="IsPublicRegistrationEnabled">是否允许匿名用户查看启用的注册方式；未提供 <see cref="RegistrationMode"/> 时用于推导模式。</param>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
/// <param name="RegistrationMode">显式注册模式；为空时由布尔字段推导。</param>
public sealed record UpdateRegistrationPolicyRequest(
    bool IsPublicRegistrationEnabled,
    int Version,
    IdentityRegistrationMode? RegistrationMode = null);

/// <summary>注册方式响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Code 发布后不可改名。</remarks>
/// <param name="Id">注册方式稳定标识。</param>
/// <param name="TenantId">所属租户标识。</param>
/// <param name="Name">显示名称。</param>
/// <param name="Code">稳定机器码；租户内唯一。</param>
/// <param name="IsEnabled">是否对公开注册入口可见。</param>
/// <param name="RoleId">注册成功后默认授予的角色标识。</param>
/// <param name="OrganizationUnitId">注册成功后默认隶属的机构单元标识。</param>
/// <param name="PositionId">注册成功后默认职位；可为空。</param>
/// <param name="SortOrder">排序序号。</param>
/// <param name="Remark">管理员备注。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）；未更新时为 <see langword="null"/>。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record RegistrationWayResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string Code,
    bool IsEnabled,
    Guid RoleId,
    Guid OrganizationUnitId,
    Guid? PositionId,
    int SortOrder,
    string? Remark,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>公开注册方式列表项；不含内部备注。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">注册方式稳定标识。</param>
/// <param name="Name">显示名称。</param>
/// <param name="Code">稳定机器码。</param>
/// <param name="SortOrder">排序序号。</param>
public sealed record PublicRegistrationWayResponse(
    Guid Id,
    string Name,
    string Code,
    int SortOrder);

/// <summary>创建注册方式请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。 Code 发布后不可改名。</remarks>
/// <param name="TenantId">所属租户标识。</param>
/// <param name="Name">显示名称。</param>
/// <param name="Code">稳定机器码；租户内唯一。</param>
/// <param name="IsEnabled">是否对公开注册入口可见。</param>
/// <param name="RoleId">注册成功后默认授予的角色标识。</param>
/// <param name="OrganizationUnitId">注册成功后默认隶属的机构单元标识。</param>
/// <param name="PositionId">注册成功后默认职位；可为空。</param>
/// <param name="SortOrder">排序序号。</param>
/// <param name="Remark">管理员备注。</param>
public sealed record CreateRegistrationWayRequest(
    Guid TenantId,
    string Name,
    string Code,
    bool IsEnabled,
    Guid RoleId,
    Guid OrganizationUnitId,
    Guid? PositionId,
    int SortOrder,
    string? Remark);

/// <summary>更新注册方式请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Name">显示名称。</param>
/// <param name="Code">稳定机器码；租户内唯一。</param>
/// <param name="IsEnabled">是否对公开注册入口可见。</param>
/// <param name="RoleId">注册成功后默认授予的角色标识。</param>
/// <param name="OrganizationUnitId">注册成功后默认隶属的机构单元标识。</param>
/// <param name="PositionId">注册成功后默认职位；可为空。</param>
/// <param name="SortOrder">排序序号。</param>
/// <param name="Remark">管理员备注。</param>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record UpdateRegistrationWayRequest(
    string Name,
    string Code,
    bool IsEnabled,
    Guid RoleId,
    Guid OrganizationUnitId,
    Guid? PositionId,
    int SortOrder,
    string? Remark,
    int Version);
