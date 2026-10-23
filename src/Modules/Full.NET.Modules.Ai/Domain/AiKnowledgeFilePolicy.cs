namespace Full.NET.Modules.Ai.Domain;

/// <summary>首版来源只接收有界文件流；文件名和内容类型不能控制物理路径或执行解析。</summary>
internal static class AiKnowledgeFilePolicy
{
    internal const long MaxBytes = 8 * 1024 * 1024;
    internal const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    internal const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    internal const string PptxContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    // 白名单只决定受理资格；实际结构和解码结果仍必须在隔离工具内全量验证。
    private static readonly (string Extension, string ContentType)[] Formats =
    [
        (".txt", "text/plain"), (".md", "text/markdown"), (".pdf", "application/pdf"), (".docx", DocxContentType),
        (".doc", "application/msword"), (".csv", "text/csv"), (".html", "text/html"), (".htm", "text/html"),
        (".xls", "application/vnd.ms-excel"), (".xlsx", XlsxContentType), (".ppt", "application/vnd.ms-powerpoint"), (".pptx", PptxContentType),
        (".jpg", "image/jpeg"), (".jpeg", "image/jpeg"), (".jfif", "image/jpeg"), (".png", "image/png"), (".apng", "image/apng"),
        (".webp", "image/webp"), (".bmp", "image/bmp"), (".tif", "image/tiff"), (".tiff", "image/tiff"), (".gif", "image/gif"),
        (".heic", "image/heic"), (".heif", "image/heif"), (".avif", "image/avif"), (".jp2", "image/jp2"), (".j2k", "image/jp2"), (".jxl", "image/jxl"),
        (".ico", "image/vnd.microsoft.icon"), (".tga", "image/x-tga"), (".pcx", "image/vnd.zbrush.pcx"),
        (".pnm", "image/x-portable-anymap"), (".pbm", "image/x-portable-bitmap"), (".pgm", "image/x-portable-graymap"), (".ppm", "image/x-portable-pixmap"),
        (".svg", "image/svg+xml"), (".svgz", "image/svg+xml"), (".wmf", "image/wmf"), (".emf", "image/emf"), (".psd", "image/vnd.adobe.photoshop")
    ];
    internal static readonly IReadOnlyList<string> ContentTypes = Array.AsReadOnly(Formats.Select(f => f.ContentType).Distinct(StringComparer.Ordinal).ToArray());
    internal static string? InputFormatFor(string contentType) => Formats.FirstOrDefault(f => f.ContentType == contentType).Extension?[1..];
    internal static string? ContentTypeFor(string inputFormat) => Formats.FirstOrDefault(f => f.Extension == "." + inputFormat).ContentType;
    internal static long Limit(string? type) => type == DocxContentType ? 5 * 1024 * 1024 : MaxBytes;

    internal static string? Validate(string? name, string? type, int version, Guid requestKey, long? declaredBytes)
    {
        if (version < 1 || requestKey == Guid.Empty || declaredBytes is <= 0 || declaredBytes > Limit(type))
            return "A positive version, retry key and bounded nonempty content are required.";
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name != name.Trim()
            || name.Any(char.IsControl) || name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or "..")
            return "A safe filename without path components is required.";
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return type is not null && Formats.Contains((extension, type)) ? null : "The filename and content type must match a supported knowledge format.";
    }
}
