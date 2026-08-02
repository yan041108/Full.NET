using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Organization;

internal sealed class OrganizationAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("organization", "组织机构", 30);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            OrganizationUnitManagementPermissions.Read,
            "查看机构",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUnitManagementPermissions.Create,
            "创建租户机构",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUnitManagementPermissions.Update,
            "更新租户机构",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUnitManagementPermissions.Disable,
            "禁用租户机构",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUserUnitManagementPermissions.Read,
            "查看用户机构隶属",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUserUnitManagementPermissions.Create,
            "分配用户机构隶属",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUserUnitManagementPermissions.Update,
            "设为用户主部门",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUserUnitManagementPermissions.Disable,
            "取消用户机构隶属",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationPositionManagementPermissions.Read,
            "查看职位",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationPositionManagementPermissions.Create,
            "创建租户职位",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationPositionManagementPermissions.Update,
            "更新租户职位",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationPositionManagementPermissions.Disable,
            "禁用租户职位",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationPositionManagementPermissions.AssignUnit,
            "绑定租户职位机构",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationPositionManagementPermissions.AssignPositionLevel,
            "绑定租户职位职级",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationPositionLevelManagementPermissions.Read,
            "查看职级",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationPositionLevelManagementPermissions.Create,
            "创建租户职级",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationPositionLevelManagementPermissions.Update,
            "更新租户职级",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationPositionLevelManagementPermissions.Disable,
            "禁用租户职级",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUserPositionManagementPermissions.Read,
            "查看用户职位隶属",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUserPositionManagementPermissions.Create,
            "分配用户职位隶属",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUserPositionManagementPermissions.Update,
            "设为用户主职位",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUserPositionManagementPermissions.Disable,
            "取消用户职位隶属",
            AuthorizationScope.Tenant),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "org-units",
            null,
            "org-units",
            "/organization/units",
            "org-units",
            "机构管理",
            "Organization",
            "office-building",
            45,
            OrganizationUnitManagementPermissions.Read),
        new NavigationDefinition(
            "org-user-units",
            null,
            "org-user-units",
            "/organization/user-units",
            "org-user-units",
            "用户机构隶属",
            "Organization",
            "user",
            46,
            OrganizationUserUnitManagementPermissions.Read),
        new NavigationDefinition(
            "org-positions",
            null,
            "org-positions",
            "/organization/positions",
            "org-positions",
            "职位管理",
            "Positions",
            "postcard",
            47,
            OrganizationPositionManagementPermissions.Read),
        new NavigationDefinition(
            "org-position-levels",
            null,
            "org-position-levels",
            "/organization/position-levels",
            "org-position-levels",
            "职级管理",
            "Organization",
            "medal",
            48,
            OrganizationPositionLevelManagementPermissions.Read),
        new NavigationDefinition(
            "org-user-positions",
            null,
            "org-user-positions",
            "/organization/user-positions",
            "org-user-positions",
            "用户职位隶属",
            "Organization",
            "user",
            49,
            OrganizationUserPositionManagementPermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "organization.units.create",
            "org-units",
            OrganizationUnitManagementPermissions.Create,
            "创建机构",
            "create",
            10),
        new AuthorizationActionDefinition(
            "organization.units.update",
            "org-units",
            OrganizationUnitManagementPermissions.Update,
            "编辑机构",
            "update",
            20),
        new AuthorizationActionDefinition(
            "organization.units.disable",
            "org-units",
            OrganizationUnitManagementPermissions.Disable,
            "禁用机构",
            "disable",
            30),
        new AuthorizationActionDefinition(
            "organization.user_units.create",
            "org-user-units",
            OrganizationUserUnitManagementPermissions.Create,
            "分配机构隶属",
            "create",
            10),
        new AuthorizationActionDefinition(
            "organization.user_units.update",
            "org-user-units",
            OrganizationUserUnitManagementPermissions.Update,
            "设为主部门",
            "update",
            20),
        new AuthorizationActionDefinition(
            "organization.user_units.disable",
            "org-user-units",
            OrganizationUserUnitManagementPermissions.Disable,
            "取消机构隶属",
            "disable",
            30),
        new AuthorizationActionDefinition(
            "organization.positions.create",
            "org-positions",
            OrganizationPositionManagementPermissions.Create,
            "创建职位",
            "create",
            10),
        new AuthorizationActionDefinition(
            "organization.positions.update",
            "org-positions",
            OrganizationPositionManagementPermissions.Update,
            "编辑职位",
            "update",
            20),
        new AuthorizationActionDefinition(
            "organization.positions.disable",
            "org-positions",
            OrganizationPositionManagementPermissions.Disable,
            "禁用职位",
            "disable",
            30),
        new AuthorizationActionDefinition(
            "organization.positions.assign_unit",
            "org-positions",
            OrganizationPositionManagementPermissions.AssignUnit,
            "绑定机构",
            "assign_unit",
            40),
        new AuthorizationActionDefinition(
            "organization.positions.assign_position_level",
            "org-positions",
            OrganizationPositionManagementPermissions.AssignPositionLevel,
            "绑定职级",
            "assign_position_level",
            50),
        new AuthorizationActionDefinition(
            "organization.position_levels.create",
            "org-position-levels",
            OrganizationPositionLevelManagementPermissions.Create,
            "创建职级",
            "create",
            10),
        new AuthorizationActionDefinition(
            "organization.position_levels.update",
            "org-position-levels",
            OrganizationPositionLevelManagementPermissions.Update,
            "编辑职级",
            "update",
            20),
        new AuthorizationActionDefinition(
            "organization.position_levels.disable",
            "org-position-levels",
            OrganizationPositionLevelManagementPermissions.Disable,
            "禁用职级",
            "disable",
            30),
        new AuthorizationActionDefinition(
            "organization.user_positions.create",
            "org-user-positions",
            OrganizationUserPositionManagementPermissions.Create,
            "分配职位隶属",
            "create",
            10),
        new AuthorizationActionDefinition(
            "organization.user_positions.update",
            "org-user-positions",
            OrganizationUserPositionManagementPermissions.Update,
            "设为主职位",
            "update",
            20),
        new AuthorizationActionDefinition(
            "organization.user_positions.disable",
            "org-user-positions",
            OrganizationUserPositionManagementPermissions.Disable,
            "取消职位隶属",
            "disable",
            30),
    ];
}
