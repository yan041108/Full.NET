namespace Full.NET.Modules.Ai.Domain;

/// <summary>首版来源只接收有界文件流；文件名和内容类型不能控制物理路径或执行解析。</summary>
internal static class AiKnowledgeFilePolicy
{
    internal const long MaxBytes = 8 * 1024 * 1024;
    internal const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    internal static long Limit(string? type) => type == DocxContentType ? 5 * 1024 * 1024 : MaxBytes;

    internal static string? Validate(string? name, string? type, int version, Guid requestKey, long? declaredBytes)
    {
        if (version < 1 || requestKey == Guid.Empty || declaredBytes is <= 0 || declaredBytes > Limit(type))
            return "A positive version, retry key and bounded nonempty content are required.";
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name != name.Trim()
            || name.Any(char.IsControl) || name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or "..")
            return "A safe filename without path components is required.";
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return (extension, type) is (".txt", "text/plain") or (".md", "text/markdown") or (".pdf", "application/pdf") or (".docx", DocxContentType)
            ? null : "Only matching DOCX, PDF, Markdown and plain text content types are accepted.";
    }
}
