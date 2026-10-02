namespace Full.NET.Modules.Organization.Contracts;

/// <summary>组织关系表单可分配的活动 Host 用户最小投影。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">Host 用户稳定标识。</param>
/// <param name="Username">登录名；用于表单搜索与回显。</param>
/// <param name="DisplayName">展示名称；用于下拉与已选项显示。</param>
public sealed record OrganizationAssignableUserResponse(
    Guid Id,
    string Username,
    string DisplayName);
