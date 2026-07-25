using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Organization;

internal sealed class OrganizationAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            OrganizationUnitManagementPermissions.Read,
            "查看机构",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUnitManagementPermissions.Write,
            "管理机构",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUserUnitManagementPermissions.Read,
            "查看用户机构隶属",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUserUnitManagementPermissions.Write,
            "管理用户机构隶属",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationPositionManagementPermissions.Read,
            "查看职位",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationPositionManagementPermissions.Write,
            "管理职位",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUserPositionManagementPermissions.Read,
            "查看用户职位隶属",
            AuthorizationScope.Tenant),
        new PermissionDefinition(
            OrganizationUserPositionManagementPermissions.Write,
            "管理用户职位隶属",
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
            "org-user-positions",
            null,
            "org-user-positions",
            "/organization/user-positions",
            "org-user-positions",
            "用户职位隶属",
            "Organization",
            "user",
            48,
            OrganizationUserPositionManagementPermissions.Read),
    ];
}
