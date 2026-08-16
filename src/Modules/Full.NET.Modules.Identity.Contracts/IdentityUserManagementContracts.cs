namespace Full.NET.Modules.Identity.Contracts;

/// <summary>
/// Host 作用域用户管理 API 的请求与响应契约（纵向切片 Task 1 冻结）。
/// </summary>
public static class IdentityUserManagementPermissions
{
    /// <summary>分页查询 Host 用户列表与详情。</summary>
    public const string Read = "identity.users.read";

    /// <summary>创建 Host 用户。</summary>
    public const string Create = "identity.users.create";

    /// <summary>更新 Host 用户基础资料。</summary>
    public const string Update = "identity.users.update";

    /// <summary>替换 Host 用户角色绑定。</summary>
    public const string AssignRoles = "identity.users.assign_roles";

    /// <summary>管理员重置 Host 用户密码。</summary>
    public const string ResetPassword = "identity.users.reset_password";

    /// <summary>禁用 Host 用户。</summary>
    public const string Disable = "identity.users.disable";

    /// <summary>启用 Host 用户。</summary>
    public const string Enable = "identity.users.enable";

    /// <summary>按当前字段投影导出 Host 用户。</summary>
    public const string Export = "identity.users.export";

    /// <summary>导入 Host 用户；禁止导入超级管理员。</summary>
    public const string Import = "identity.users.import";

    /// <summary>迁移 054 前遗留的粗粒度写权限；不再进入可分配目录。</summary>
    public const string Write = "identity.users.write";
}

/// <summary>创建 Host 用户请求。</summary>
public sealed record CreateHostUserRequest(
    string Username,
    string DisplayName,
    string Password,
    string? AccountType = null,
    HostUserProfileWriteRequest? Profile = null);

/// <summary>更新 Host 用户基础资料请求。</summary>
public sealed record UpdateHostUserRequest(
    string DisplayName,
    int Version,
    string? AccountType = null,
    HostUserProfileWriteRequest? Profile = null);

/// <summary>管理员重置 Host 用户密码请求。</summary>
public sealed record ResetHostUserPasswordRequest(
    string Password);

/// <summary>Host 用户扩展档案响应。</summary>
public sealed record HostUserProfileResponse(
    string? Nickname,
    string? PhoneNumber,
    string? Email,
    string? EmployeeNumber,
    string? Gender,
    string? JoinDateUtc,
    int? SortOrder,
    string? IdCardType,
    string? IdCardNumber,
    string? BirthDate,
    string? Ethnicity,
    string? Address,
    string? GraduatedSchool,
    string? EducationLevel,
    string? PoliticalStatus,
    string? OfficePhone,
    string? EmergencyContact,
    string? EmergencyContactRelation,
    string? EmergencyContactPhone,
    string? EmergencyContactAddress,
    string? Remark,
    int Version);

/// <summary>Host 用户扩展档案写入请求。</summary>
public sealed record HostUserProfileWriteRequest(
    IReadOnlyList<string>? FieldKeys,
    string? Nickname,
    string? PhoneNumber,
    string? Email,
    string? EmployeeNumber,
    string? Gender,
    string? JoinDateUtc,
    int? SortOrder,
    string? IdCardType,
    string? IdCardNumber,
    string? BirthDate,
    string? Ethnicity,
    string? Address,
    string? GraduatedSchool,
    string? EducationLevel,
    string? PoliticalStatus,
    string? OfficePhone,
    string? EmergencyContact,
    string? EmergencyContactRelation,
    string? EmergencyContactPhone,
    string? EmergencyContactAddress,
    string? Remark,
    int? Version);

/// <summary>Host 用户列表项与详情响应。</summary>
public sealed record HostUserResponse(
    Guid Id,
    string Username,
    string DisplayName,
    string AccountType,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version,
    HostUserProjectedFieldsResponse? ProjectedFields = null,
    HostUserProfileResponse? Profile = null);

/// <summary>
/// Host 用户的受限投影；EffectiveFieldKeys 用于区分无授权与有授权但值为空。
/// </summary>
public sealed record HostUserProjectedFieldsResponse(
    IReadOnlyList<string> EffectiveFieldKeys,
    string? PreferredLocale,
    int? FailedLoginCount,
    DateTimeOffset? LockoutEndUtc);

/// <summary>批量导入 Host 用户；逐行报告，禁止超级管理员账号类型。</summary>
public sealed record ImportHostUsersRequest(
    IReadOnlyList<CreateHostUserRequest> Rows);

/// <summary>单行导入结果。</summary>
public sealed record ImportHostUserRowResult(
    int Line,
    bool Succeeded,
    Guid? UserId,
    string? ErrorCode,
    string? Message);

/// <summary>导入汇总。</summary>
public sealed record ImportHostUsersResponse(
    int SucceededCount,
    IReadOnlyList<ImportHostUserRowResult> Results);

/// <summary>批量启用或停用的用户标识列表。</summary>
public sealed record BatchHostUserIdsRequest(
    IReadOnlyList<Guid> UserIds);

/// <summary>批量状态变更的单条结果。</summary>
public sealed record BatchHostUserStatusItem(
    Guid UserId,
    bool Succeeded,
    string? ErrorCode,
    string? Message);

/// <summary>批量启用或停用汇总。</summary>
public sealed record BatchHostUserStatusResponse(
    int SucceededCount,
    IReadOnlyList<BatchHostUserStatusItem> Results);
