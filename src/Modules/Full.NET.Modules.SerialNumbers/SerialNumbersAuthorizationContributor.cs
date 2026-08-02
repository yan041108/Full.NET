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
            SerialNumberRulePermissions.Create,
            "创建流水号规则",
            AuthorizationScope.Host),
        new PermissionDefinition(
            SerialNumberRulePermissions.Update,
            "更新流水号规则",
            AuthorizationScope.Host),
        new PermissionDefinition(
            SerialNumberRulePermissions.Enable,
            "启用流水号规则",
            AuthorizationScope.Host),
        new PermissionDefinition(
            SerialNumberRulePermissions.Disable,
            "禁用流水号规则",
            AuthorizationScope.Host),
        new PermissionDefinition(
            SerialNumberRulePermissions.Preview,
            "预览流水号规则",
            AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "serial-number-rules",
            null,
            "serial-number-rules",
            "/serial-numbers/rules",
            "serial-number-rules",
            "流水号规则",
            "Serial Number Rules",
            "hash",
            91,
            SerialNumberRulePermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "serial_numbers.rules.create",
            "serial-number-rules",
            SerialNumberRulePermissions.Create,
            "创建规则",
            "create",
            10),
        new AuthorizationActionDefinition(
            "serial_numbers.rules.update",
            "serial-number-rules",
            SerialNumberRulePermissions.Update,
            "编辑规则",
            "update",
            20),
        new AuthorizationActionDefinition(
            "serial_numbers.rules.enable",
            "serial-number-rules",
            SerialNumberRulePermissions.Enable,
            "启用规则",
            "enable",
            30),
        new AuthorizationActionDefinition(
            "serial_numbers.rules.disable",
            "serial-number-rules",
            SerialNumberRulePermissions.Disable,
            "禁用规则",
            "disable",
            40),
        new AuthorizationActionDefinition(
            "serial_numbers.rules.preview",
            "serial-number-rules",
            SerialNumberRulePermissions.Preview,
            "预览流水号",
            "preview",
            50),
    ];
}
