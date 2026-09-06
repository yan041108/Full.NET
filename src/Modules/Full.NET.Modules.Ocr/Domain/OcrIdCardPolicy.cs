namespace Full.NET.Modules.Ocr.Domain;

/// <summary>身份证 OCR 源文件与结果保留边界。</summary>
internal static class OcrIdCardPolicy
{
    /// <summary>允许识别的最大图片字节数。</summary>
    public const long MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>Provider 原始响应 JSON 最大长度。</summary>
    public const int MaxRawResultJsonLength = 64 * 1024;

    /// <summary>允许的图片 Content-Type 前缀。</summary>
    public static readonly string[] AllowedContentTypePrefixes =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
    ];
}
