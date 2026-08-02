using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;

namespace Full.NET.Modules.SerialNumbers;

internal sealed class SerialNumbersAuthorizationContributor
    : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("serial-numbers", "序列号", 90);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition(
            SerialNumberRulePermissions.Read,
            "查询流水号规则",
            AuthorizationScope.Host),
        new PermissionDefinition(
            SerialNumberRulePermissions.Write,
            "管理流水号规则",
            AuthorizationScope.Host),
    ];

    // 双管理端页面尚未交付，本切片不发布会指向空路由的导航项。
    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = [];
}
