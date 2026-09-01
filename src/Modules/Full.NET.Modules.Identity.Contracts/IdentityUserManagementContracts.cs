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
/// <param name="Username">登录名；在 Host 作用域内须保持唯一。</param>
/// <param name="DisplayName">面向管理端展示的名称。</param>
/// <param name="Password">首次创建使用的明文密码；只允许存在于当前请求边界，禁止写入日志或缓存。</param>
/// <param name="AccountType">账号类型机器码；省略时使用默认普通用户类型。</param>
/// <param name="Profile">可选的扩展档案写入内容。</param>
public sealed record CreateHostUserRequest(
    string Username,
    string DisplayName,
    string Password,
    string? AccountType = null,
    HostUserProfileWriteRequest? Profile = null);

/// <summary>更新 Host 用户基础资料请求。</summary>
/// <param name="DisplayName">更新后的展示名称。</param>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
/// <param name="AccountType">更新后的账号类型机器码；<see langword="null"/> 表示不修改。</param>
/// <param name="Profile">可选的扩展档案写入内容。</param>
public sealed record UpdateHostUserRequest(
    string DisplayName,
    int Version,
    string? AccountType = null,
    HostUserProfileWriteRequest? Profile = null);

/// <summary>管理员重置 Host 用户密码请求。</summary>
/// <param name="Password">重置后的明文密码；只允许存在于当前请求边界。</param>
public sealed record ResetHostUserPasswordRequest(
    string Password);

/// <summary>Host 用户扩展档案响应。</summary>
/// <param name="Nickname">昵称。</param>
/// <param name="PhoneNumber">手机号。</param>
/// <param name="Email">邮箱。</param>
/// <param name="EmployeeNumber">工号。</param>
/// <param name="Gender">性别。</param>
/// <param name="JoinDateUtc">入职日期 UTC（ISO 8601 字符串形式）。</param>
/// <param name="SortOrder">排序值。</param>
/// <param name="IdCardType">证件类型。</param>
/// <param name="IdCardNumber">证件号码。</param>
/// <param name="BirthDate">出生日期。</param>
/// <param name="Ethnicity">民族。</param>
/// <param name="Address">联系地址。</param>
/// <param name="GraduatedSchool">毕业院校。</param>
/// <param name="EducationLevel">学历。</param>
/// <param name="PoliticalStatus">政治面貌。</param>
/// <param name="OfficePhone">办公电话。</param>
/// <param name="EmergencyContact">紧急联系人。</param>
/// <param name="EmergencyContactRelation">紧急联系人关系。</param>
/// <param name="EmergencyContactPhone">紧急联系人电话。</param>
/// <param name="EmergencyContactAddress">紧急联系人地址。</param>
/// <param name="Remark">备注。</param>
/// <param name="Version">档案快照的并发版本。</param>
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
/// <param name="FieldKeys">本次显式参与写入的字段键集合；<see langword="null"/> 表示按非空值推断。</param>
/// <param name="Nickname">昵称。</param>
/// <param name="PhoneNumber">手机号。</param>
/// <param name="Email">邮箱。</param>
/// <param name="EmployeeNumber">工号。</param>
/// <param name="Gender">性别。</param>
/// <param name="JoinDateUtc">入职日期 UTC（ISO 8601 字符串形式）。</param>
/// <param name="SortOrder">排序值。</param>
/// <param name="IdCardType">证件类型。</param>
/// <param name="IdCardNumber">证件号码。</param>
/// <param name="BirthDate">出生日期。</param>
/// <param name="Ethnicity">民族。</param>
/// <param name="Address">联系地址。</param>
/// <param name="GraduatedSchool">毕业院校。</param>
/// <param name="EducationLevel">学历。</param>
/// <param name="PoliticalStatus">政治面貌。</param>
/// <param name="OfficePhone">办公电话。</param>
/// <param name="EmergencyContact">紧急联系人。</param>
/// <param name="EmergencyContactRelation">紧急联系人关系。</param>
/// <param name="EmergencyContactPhone">紧急联系人电话。</param>
/// <param name="EmergencyContactAddress">紧急联系人地址。</param>
/// <param name="Remark">备注。</param>
/// <param name="Version">调用方看到的当前版本；<see langword="null"/> 表示首次写入。</param>
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
/// <param name="Id">Host 用户稳定标识。</param>
/// <param name="Username">登录名。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="AccountType">账号类型机器码。</param>
/// <param name="IsActive">是否处于活动状态；禁用用户不可登录。</param>
/// <param name="CreatedAtUtc">账号创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近一次资料更新时间（UTC）；从未更新时为 <see langword="null"/>。</param>
/// <param name="Version">账号快照的并发版本。</param>
/// <param name="ProjectedFields">按当前访问者角色裁剪后的受限投影；无裁剪授权时为 <see langword="null"/>。</param>
/// <param name="Profile">扩展档案详情；无授权读取时为 <see langword="null"/>。</param>
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
/// <param name="EffectiveFieldKeys">服务端按角色授权裁剪后实际生效的字段键。</param>
/// <param name="PreferredLocale">账号首选语言偏好；未设置时为 <see langword="null"/>。</param>
/// <param name="FailedLoginCount">累计失败登录次数；未启用锁定时为 <see langword="null"/>。</param>
/// <param name="LockoutEndUtc">账号临时锁定结束时间（UTC）；未锁定时为 <see langword="null"/>。</param>
public sealed record HostUserProjectedFieldsResponse(
    IReadOnlyList<string> EffectiveFieldKeys,
    string? PreferredLocale,
    int? FailedLoginCount,
    DateTimeOffset? LockoutEndUtc);

/// <summary>批量导入 Host 用户；逐行报告，禁止超级管理员账号类型。</summary>
/// <param name="Rows">按工作簿行顺序提交的创建请求；调用方应限制单次导入行数上限。</param>
public sealed record ImportHostUsersRequest(
    IReadOnlyList<CreateHostUserRequest> Rows);

/// <summary>单行导入结果。</summary>
/// <param name="Line">原始工作簿中的行号（从 1 开始），用于回显错误定位。</param>
/// <param name="Succeeded">本行是否导入成功。</param>
/// <param name="UserId">导入成功时分配的用户标识；失败时为 <see langword="null"/>。</param>
/// <param name="ErrorCode">失败时返回稳定错误码；成功时为 <see langword="null"/>。</param>
/// <param name="Message">失败时的可读说明；成功时为 <see langword="null"/>。</param>
public sealed record ImportHostUserRowResult(
    int Line,
    bool Succeeded,
    Guid? UserId,
    string? ErrorCode,
    string? Message);

/// <summary>导入汇总。</summary>
/// <param name="SucceededCount">实际导入成功的行数。</param>
/// <param name="Results">逐行详细结果；顺序与请求行一致。</param>
public sealed record ImportHostUsersResponse(
    int SucceededCount,
    IReadOnlyList<ImportHostUserRowResult> Results);

/// <summary>批量启用或停用的用户标识列表。</summary>
/// <param name="UserIds">待变更状态的用户标识集合；实现方应保证批量操作的原子性或逐行回滚。</param>
public sealed record BatchHostUserIdsRequest(
    IReadOnlyList<Guid> UserIds);

/// <summary>批量状态变更的单条结果。</summary>
/// <param name="UserId">本条结果对应的用户标识。</param>
/// <param name="Succeeded">本条是否变更成功。</param>
/// <param name="ErrorCode">失败时返回稳定错误码；成功时为 <see langword="null"/>。</param>
/// <param name="Message">失败时的可读说明；成功时为 <see langword="null"/>。</param>
public sealed record BatchHostUserStatusItem(
    Guid UserId,
    bool Succeeded,
    string? ErrorCode,
    string? Message);

/// <summary>批量启用或停用汇总。</summary>
/// <param name="SucceededCount">实际成功变更的用户数。</param>
/// <param name="Results">逐条结果；顺序与请求集合一致。</param>
public sealed record BatchHostUserStatusResponse(
    int SucceededCount,
    IReadOnlyList<BatchHostUserStatusItem> Results);
