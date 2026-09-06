using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing.Contracts;

namespace Full.NET.Modules.Printing;

/// <summary>向 Identity 授权目录注册 Printing 权限、导航与页面操作。</summary>
internal sealed class PrintingAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("printing", "打印", 117);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(PrintingTemplatePermissions.Read, "读取打印模板", AuthorizationScope.Host),
        new(PrintingTemplatePermissions.Create, "创建打印模板", AuthorizationScope.Host),
        new(PrintingTemplatePermissions.Update, "更新打印模板", AuthorizationScope.Host),
        new(PrintingTemplatePermissions.Publish, "发布打印模板版本", AuthorizationScope.Host),
        new(PrintingTemplatePermissions.Preview, "预览打印模板", AuthorizationScope.Host),
        new(PrintingFormSchemaPermissions.Read, "读取固定表单 Schema 目录", AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "printing-preview",
            null,
            "printing-preview",
            "/printing/preview",
            "printing-preview",
            "打印预览",
            "Printing Preview",
            "printer",
            10,
            PrintingTemplatePermissions.Preview),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "printing.templates.create",
            "printing-preview",
            PrintingTemplatePermissions.Create,
            "创建模板",
            "create",
            10),
        new AuthorizationActionDefinition(
            "printing.templates.update",
            "printing-preview",
            PrintingTemplatePermissions.Update,
            "编辑模板",
            "update",
            20),
        new AuthorizationActionDefinition(
            "printing.templates.publish",
            "printing-preview",
            PrintingTemplatePermissions.Publish,
            "发布版本",
            "publish",
            30),
    ];
}
