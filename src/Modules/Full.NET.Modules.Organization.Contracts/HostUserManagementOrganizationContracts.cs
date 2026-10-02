namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 平台用户管理页在 Host 作用域下读取指定租户机构参考数据。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Units">租户下全部活动机构单元投影。</param>
/// <param name="Positions">租户下全部活动职位投影。</param>
/// <param name="UserUnits">用户与机构单元的绑定关系投影。</param>
/// <param name="UserPositions">用户与职位的绑定关系投影。</param>
public sealed record HostUserManagementOrganizationReferenceResponse(
    IReadOnlyList<OrganizationUnitResponse> Units,
    IReadOnlyList<OrganizationPositionResponse> Positions,
    IReadOnlyList<OrganizationUserUnitResponse> UserUnits,
    IReadOnlyList<OrganizationUserPositionResponse> UserPositions);