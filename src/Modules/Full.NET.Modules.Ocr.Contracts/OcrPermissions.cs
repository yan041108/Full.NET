namespace Full.NET.Modules.Ocr.Contracts;

/// <summary>OCR Provider 配置权限码。</summary>
public static class OcrProviderPermissions
{
    /// <summary>读取 OCR Provider 配置。</summary>
    public const string Read = "ocr.providers.read";

    /// <summary>更新 OCR Provider 配置。</summary>
    public const string Update = "ocr.providers.update";

    /// <summary>测试 OCR Provider 连通性。</summary>
    public const string Test = "ocr.providers.test";
}

/// <summary>身份证 OCR 任务权限码。</summary>
public static class OcrIdCardTaskPermissions
{
    /// <summary>读取身份证 OCR 任务。</summary>
    public const string Read = "ocr.id_card_tasks.read";

    /// <summary>创建身份证 OCR 识别任务。</summary>
    public const string Create = "ocr.id_card_tasks.create";

    /// <summary>确认身份证 OCR 识别结果。</summary>
    public const string Confirm = "ocr.id_card_tasks.confirm";

    /// <summary>拒绝身份证 OCR 识别结果。</summary>
    public const string Reject = "ocr.id_card_tasks.reject";
}
