using System.Text.Json.Serialization;

namespace Full.NET.Modules.Organization.Contracts;

/// <summary>直属汇报关系涉及全租户人员，不隐含在机构改名或成员隶属权限中。</summary>
public static class OrganizationReportingLinePermissions
{
    public const string Read = "organization.reporting_lines.read";
    public const string Update = "organization.reporting_lines.update";
}

/// <summary>请求只包含直属上级和已读取的关系修订；空值明确清除。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetOrganizationReportingLineRequest(Guid? ManagerUserId, long ExpectedRevision);

/// <summary>人员没有明确汇报关系时修订为零；显示名称不承担授权语义。</summary>
public sealed record OrganizationReportingLineResponse(Guid EmployeeUserId, Guid? ManagerUserId, long Revision,
    Guid? UpdatedById, DateTimeOffset? UpdatedAtUtc, string? ManagerDisplayName);

/// <summary>当前租户的活动直属上级候选；自身不能成为上级，由写边界最终裁定。</summary>
public sealed record OrganizationReportingCandidate(Guid Id, string Username, string DisplayName);

/// <summary>真实直属关系的最小来源；无记录是正常无人，非法持久数据不能解释为空来源。</summary>
public sealed record WorkflowDirectManagerResolution(bool SourceIsValid, Guid? RelationId, Guid? ManagerUserId, long Revision);
