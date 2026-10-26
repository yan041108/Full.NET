using System.Text.Json.Serialization;

namespace Full.NET.Modules.Organization.Contracts;

/// <summary>机构领导权配置与普通机构编辑分开授权，防止改名权限隐式授予审批分配权。</summary>
public static class OrganizationUnitLeadershipPermissions
{
    public const string Read = "organization.unit_leadership.read";
    public const string Update = "organization.unit_leadership.update";
}

/// <summary>只接受同一可信租户内的活动成员；空值明确清除负责人，两个版本都必须匹配。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetOrganizationUnitLeadershipRequest(Guid? LeaderUserId, int ExpectedUnitVersion, long ExpectedRevision);

/// <summary>不存在配置时修订为零；未配置不能解释为任一普通机构成员。</summary>
public sealed record OrganizationUnitLeadershipResponse(Guid UnitId, int UnitVersion, bool IsUnitActive,
    Guid? LeaderUserId, long Revision, Guid? UpdatedById, DateTimeOffset? UpdatedAtUtc, string? LeaderDisplayName = null);

/// <summary>只投影当前租户内可选的活动成员，不暴露角色和会话资料。</summary>
public sealed record OrganizationUnitLeaderCandidateResponse(Guid Id, string Username, string DisplayName);
