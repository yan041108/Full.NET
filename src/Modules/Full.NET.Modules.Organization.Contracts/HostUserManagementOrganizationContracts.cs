namespace Full.NET.Modules.Organization.Contracts;

/// <summary>
/// 平台用户管理页在 Host 作用域下读取指定租户机构参考数据。
/// </summary>
public sealed record HostUserManagementOrganizationReferenceResponse(
    IReadOnlyList<OrganizationUnitResponse> Units,
    IReadOnlyList<OrganizationPositionResponse> Positions,
    IReadOnlyList<OrganizationUserUnitResponse> UserUnits,
    IReadOnlyList<OrganizationUserPositionResponse> UserPositions);