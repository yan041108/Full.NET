using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Ocr.Contracts;

namespace Full.NET.Modules.Ocr;

/// <summary>向 Identity 授权目录注册 OCR 权限、导航与页面操作。</summary>
internal sealed class OcrAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } =
        new("ocr", "OCR", 122);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(OcrProviderPermissions.Read, "读取 OCR Provider 配置", AuthorizationScope.Host),
        new(OcrProviderPermissions.Update, "更新 OCR Provider 配置", AuthorizationScope.Host),
        new(OcrProviderPermissions.Test, "测试 OCR Provider 连通性", AuthorizationScope.Host),
        new(OcrIdCardTaskPermissions.Read, "读取身份证 OCR 任务", AuthorizationScope.Host),
        new(OcrIdCardTaskPermissions.Create, "创建身份证 OCR 任务", AuthorizationScope.Host),
        new(OcrIdCardTaskPermissions.Confirm, "确认身份证 OCR 结果", AuthorizationScope.Host),
        new(OcrIdCardTaskPermissions.Reject, "拒绝身份证 OCR 结果", AuthorizationScope.Host),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        new NavigationDefinition(
            "ocr-provider-config",
            null,
            "ocr-provider-config",
            "/ocr/provider-config",
            "ocr-provider-config",
            "OCR Provider",
            "OCR Provider",
            "setting",
            10,
            OcrProviderPermissions.Read),
        new NavigationDefinition(
            "ocr-id-card-tasks",
            null,
            "ocr-id-card-tasks",
            "/ocr/id-card-tasks",
            "ocr-id-card-tasks",
            "身份证 OCR",
            "ID Card OCR",
            "postcard",
            20,
            OcrIdCardTaskPermissions.Read),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        new AuthorizationActionDefinition(
            "ocr.providers.update",
            "ocr-provider-config",
            OcrProviderPermissions.Update,
            "保存 Provider",
            "update",
            10),
        new AuthorizationActionDefinition(
            "ocr.providers.test",
            "ocr-provider-config",
            OcrProviderPermissions.Test,
            "测试 Provider",
            "test",
            20),
        new AuthorizationActionDefinition(
            "ocr.id_card_tasks.create",
            "ocr-id-card-tasks",
            OcrIdCardTaskPermissions.Create,
            "上传识别",
            "create",
            10),
        new AuthorizationActionDefinition(
            "ocr.id_card_tasks.confirm",
            "ocr-id-card-tasks",
            OcrIdCardTaskPermissions.Confirm,
            "确认结果",
            "confirm",
            20),
        new AuthorizationActionDefinition(
            "ocr.id_card_tasks.reject",
            "ocr-id-card-tasks",
            OcrIdCardTaskPermissions.Reject,
            "拒绝结果",
            "reject",
            30),
    ];
}
