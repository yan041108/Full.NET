namespace Full.NET.Modules.Organization.Contracts;

/// <summary>组织关系表单可分配的活动 Host 用户最小投影。</summary>
public sealed record OrganizationAssignableUserResponse(
    Guid Id,
    string Username,
    string DisplayName);
