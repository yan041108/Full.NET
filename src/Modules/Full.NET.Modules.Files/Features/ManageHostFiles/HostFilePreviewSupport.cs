namespace Full.NET.Modules.Files.Features.ManageHostFiles;

/// <summary>Host 文件安全预览 MIME 白名单；仅允许浏览器内联渲染风险较低的类型。</summary>
internal static class HostFilePreviewSupport
{
    /// <summary>判断内容类型是否允许通过预览端点内联返回。</summary>
    /// <param name="contentType">持久化的 HTTP Content-Type。</param>
    public static bool IsSupportedContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var normalized = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (normalized is "text/html" or "image/svg+xml")
        {
            return false;
        }

        return normalized.StartsWith("text/", StringComparison.Ordinal)
            || normalized.StartsWith("image/", StringComparison.Ordinal)
            || normalized == "application/pdf";
    }
}
