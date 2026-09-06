namespace Full.NET.Modules.Document.Domain;

/// <summary>判断源文件是否属于可提交 Office→PDF 预览转换的 MIME 白名单。</summary>
internal static class DocumentOfficePreviewSourcePolicy
{
    private static readonly HashSet<string> OfficeMimeTypes = new(StringComparer.Ordinal)
    {
        "application/msword",
        "application/vnd.ms-excel",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };

    /// <summary>规范化 Content-Type 并判断是否为 Office 文档。</summary>
    /// <param name="contentType">原始 MIME 类型，可包含 charset 等参数。</param>
    public static bool IsOfficeMime(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var normalized = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return OfficeMimeTypes.Contains(normalized);
    }
}
