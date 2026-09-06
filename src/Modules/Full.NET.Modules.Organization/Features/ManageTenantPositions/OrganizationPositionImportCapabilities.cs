namespace Full.NET.Modules.Organization.Features.ManageTenantPositions;

/// <summary>职位导入可选绑定能力，由 Endpoint 根据当前主体权限计算后传入。</summary>
/// <param name="CanAssignUnit">是否允许在导入时绑定机构。</param>
/// <param name="CanAssignPositionLevel">是否允许在导入时绑定职级。</param>
internal sealed record OrganizationPositionImportCapabilities(
    bool CanAssignUnit,
    bool CanAssignPositionLevel);
