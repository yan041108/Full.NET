namespace Full.NET.Modules.Identity.Contracts;

/// <summary>租户成员管理 API 的稳定权限码常量集合。</summary>
/// <remarks>权限码字符串（如 identity.tenant_members.read）发布后不可改名或删除；新增权限只能追加，已发布权限码不得调整顺序。</remarks>
public static class IdentityTenantMembershipPermissions
{
    /// <summary>查询租户成员列表与详情。</summary>
    public const string Read = "identity.tenant_members.read";
    /// <summary>邀请用户加入租户。</summary>
    public const string Invite = "identity.tenant_members.invite";
    /// <summary>更新租户成员角色或状态。</summary>
    public const string Update = "identity.tenant_members.update";
    /// <summary>移除租户成员。</summary>
    public const string Remove = "identity.tenant_members.remove";
    /// <summary>撤销尚未被接受的租户邀请。</summary>
    public const string RevokeInvitation = "identity.tenant_members.revoke_invitation";
    /// <summary>在当前租户内直接创建 Host 用户并加入为活动成员。</summary>
    public const string Provision = "identity.tenant_members.provision";
}

/// <summary>租户成员角色机器码常量集合。</summary>
/// <remarks>角色字符串发布后不可改名或删除；新增角色只能追加，已发布角色不得调整顺序。</remarks>
public static class TenantMemberRoles
{
    /// <summary>租户所有者；具备租户内最高权限，通常不可被移除。</summary>
    public const string Owner = "Owner";
    /// <summary>租户管理员；可管理成员但不可操作所有者。</summary>
    public const string Admin = "Admin";
    /// <summary>普通成员；仅具备业务操作权限。</summary>
    public const string Member = "Member";
}

/// <summary>租户成员状态机器码常量集合。</summary>
/// <remarks>状态字符串发布后不可改名或删除；新增状态只能追加，已发布状态不得调整顺序。</remarks>
public static class TenantMemberStatuses
{
    /// <summary>待激活；尚未完成加入流程。</summary>
    public const string Pending = "Pending";
    /// <summary>活动；可正常访问租户资源。</summary>
    public const string Active = "Active";
    /// <summary>已暂停；暂时失去访问权限但关系保留。</summary>
    public const string Suspended = "Suspended";
    /// <summary>已移除；成员关系终止。</summary>
    public const string Removed = "Removed";
}

/// <summary>租户邀请状态机器码常量集合。</summary>
/// <remarks>状态字符串发布后不可改名或删除；新增状态只能追加，已发布状态不得调整顺序。</remarks>
public static class TenantInvitationStatuses
{
    /// <summary>待接受；邀请仍可被接受或撤销。</summary>
    public const string Pending = "Pending";
    /// <summary>已接受；目标用户已成为租户成员。</summary>
    public const string Accepted = "Accepted";
    /// <summary>已撤销；邀请失效，目标用户不能再据此加入。</summary>
    public const string Revoked = "Revoked";
    /// <summary>已过期；超过有效期后自动失效。</summary>
    public const string Expired = "Expired";
}

/// <summary>租户成员关系投影；用于列表与详情响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持 MemoryPack/JSON 线格式兼容。</remarks>
/// <param name="Id">成员关系稳定标识。</param>
/// <param name="TenantId">所属租户标识。</param>
/// <param name="UserId">用户标识。</param>
/// <param name="Username">用户登录名。</param>
/// <param name="DisplayName">用户展示名称。</param>
/// <param name="MemberRole">成员角色机器码；取值见 <c>TenantMemberRoles</c>。</param>
/// <param name="Status">成员状态机器码；取值见 <c>TenantMemberStatuses</c>。</param>
/// <param name="CreatedAtUtc">成员关系创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近一次更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号；调用方更新时必须回传最新值。</param>
public sealed record TenantMemberResponse(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string Username,
    string DisplayName,
    string MemberRole,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int Version);

/// <summary>租户邀请投影；包含被邀请方、邀请方与状态等完整字段。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾。</remarks>
/// <param name="Id">邀请稳定标识。</param>
/// <param name="TenantId">所属租户标识。</param>
/// <param name="TargetEmail">被邀请邮箱。</param>
/// <param name="TargetUserId">被邀请用户标识；按邮箱预匹配到已存在用户时非空，否则为 <see langword="null"/>。</param>
/// <param name="InvitedByUserId">发起邀请的用户标识。</param>
/// <param name="MemberRole">接受后授予的角色机器码；取值见 <c>TenantMemberRoles</c>。</param>
/// <param name="Status">邀请状态机器码；取值见 <c>TenantInvitationStatuses</c>。</param>
/// <param name="ExpiresAtUtc">邀请过期时间（UTC）；过期后不可被接受。</param>
/// <param name="CreatedAtUtc">邀请创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近一次更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号；调用方撤销或接受时必须回传最新值。</param>
public sealed record TenantInvitationResponse(
    Guid Id,
    Guid TenantId,
    string TargetEmail,
    Guid? TargetUserId,
    Guid InvitedByUserId,
    string MemberRole,
    string Status,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int Version);

/// <summary>创建租户邀请请求。</summary>
/// <remarks>服务端必须校验邀请方具备 Invite 权限，并校验 <c>ExpiresInHours</c> 不超过系统上限。</remarks>
/// <param name="TargetEmail">被邀请邮箱；服务端据此发送邀请邮件。</param>
/// <param name="MemberRole">接受后授予的角色机器码；取值必须为 <c>TenantMemberRoles</c> 中的有效值。</param>
/// <param name="ExpiresInHours">有效期小时数；默认 72 小时，调用方可显式覆盖。</param>
public sealed record CreateTenantInvitationRequest(
    string TargetEmail,
    string MemberRole,
    int ExpiresInHours = 72);

/// <summary>在当前租户内创建 Host 用户并立即加入为活动成员。</summary>
/// <param name="Username">新用户登录名。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="Password">初始密码；创建后通常要求首次登录改密。</param>
/// <param name="MemberRole">租户成员角色；仅允许 Admin 或 Member。</param>
/// <param name="Email">可选资料邮箱，便于后续邀请与组织场景匹配。</param>
public sealed record ProvisionTenantMemberRequest(
    string Username,
    string DisplayName,
    string Password,
    string MemberRole,
    string? Email);

/// <summary>创建租户邀请结果；包含邀请投影与一次性 Token。</summary>
/// <remarks>InvitationToken 仅返回一次，调用方必须立即投递给被邀请方；Token 撤销或过期后不可再用。</remarks>
/// <param name="Invitation">邀请投影。</param>
/// <param name="InvitationToken">用于接受邀请的一次性 Token；只允许出现在当前响应边界，禁止写日志或缓存。</param>
public sealed record CreateTenantInvitationResult(
    TenantInvitationResponse Invitation,
    string InvitationToken);

/// <summary>更新租户成员角色请求；支持乐观并发。</summary>
/// <param name="MemberRole">更新后的角色机器码；取值必须为 <c>TenantMemberRoles</c> 中的有效值。</param>
/// <param name="Version">调用方感知的当前版本；服务端据此拒绝并发覆盖。</param>
public sealed record UpdateTenantMemberRequest(
    string MemberRole,
    int Version);

/// <summary>接受租户邀请请求。</summary>
/// <param name="InvitationToken">来自邀请邮件的一次性 Token；服务端校验有效性与未过期。</param>
public sealed record AcceptTenantInvitationRequest(string InvitationToken);

/// <summary>接受租户邀请结果；返回新建立的成员关系摘要。</summary>
/// <remarks>接受操作必须原子完成成员关系写入与邀请状态迁移；半提交会破坏并发与重放语义。</remarks>
/// <param name="MemberId">新成员关系稳定标识。</param>
/// <param name="TenantId">所属租户标识。</param>
/// <param name="UserId">接受方用户标识。</param>
/// <param name="MemberRole">实际授予的角色机器码。</param>
/// <param name="Status">成员状态机器码；正常流程为 <c>Active</c>。</param>
public sealed record AcceptTenantInvitationResponse(
    Guid MemberId,
    Guid TenantId,
    Guid UserId,
    string MemberRole,
    string Status);

/// <summary>当前登录用户可见的待接受租户邀请摘要。</summary>
/// <param name="Id">邀请稳定标识。</param>
/// <param name="TenantId">目标租户标识。</param>
/// <param name="TenantName">目标租户展示名称。</param>
/// <param name="TargetEmail">邀请目标邮箱。</param>
/// <param name="MemberRole">接受后授予的角色机器码。</param>
/// <param name="ExpiresAtUtc">邀请过期时间（UTC）。</param>
/// <param name="CreatedAtUtc">邀请创建时间（UTC）。</param>
public sealed record MyTenantInvitationResponse(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string TargetEmail,
    string MemberRole,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc);