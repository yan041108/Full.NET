namespace Full.NET.Modules.Ocr.Contracts;

/// <summary>首种受支持的 OCR Provider 键。</summary>
public static class OcrProviderKeys
{
    /// <summary>PaddleOCR 身份证识别 HTTP 适配器。</summary>
    public const string PaddleOcrIdCard = "paddle_ocr_id_card";
}

/// <summary>身份证 OCR 任务状态键。</summary>
public static class OcrIdCardTaskStatusKeys
{
    /// <summary>等待识别。</summary>
    public const string Pending = "pending";

    /// <summary>识别成功，待人工确认。</summary>
    public const string Recognized = "recognized";

    /// <summary>识别失败。</summary>
    public const string Failed = "failed";

    /// <summary>已向识别提供程序发出请求，但本地无法确认成败。</summary>
    public const string ProviderUnknown = "provider_unknown";

    /// <summary>人工确认通过。</summary>
    public const string Confirmed = "confirmed";

    /// <summary>人工拒绝。</summary>
    public const string Rejected = "rejected";
}
