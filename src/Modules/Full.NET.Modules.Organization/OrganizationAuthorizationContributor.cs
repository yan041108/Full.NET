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
    ];
}
