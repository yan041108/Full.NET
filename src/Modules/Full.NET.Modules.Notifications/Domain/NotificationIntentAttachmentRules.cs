namespace Full.NET.Modules.Notifications.Domain;

/// <summary>邮件 Intent 附件的声明式约束；发布与运行时共用同一组上限。</summary>
internal static class NotificationIntentAttachmentRules
{
    /// <summary>单个 Intent 允许的最大附件数量。</summary>
    public const int MaxAttachmentsPerIntent = 5;

    /// <summary>单个附件允许的最大字节数（25 MiB）。</summary>
    public const long MaxAttachmentSizeBytes = 26_214_400;

    /// <summary>全部附件合计允许的最大字节数（50 MiB）。</summary>
    public const long MaxTotalAttachmentSizeBytes = 52_428_800;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.Ordinal)
    {
        "pdf",
        "doc",
        "docx",
        "xls",
        "xlsx",
        "png",
        "jpg",
        "jpeg",
        "txt",
        "zip",
    };

    /// <summary>判断文件名扩展名是否在允许列表内。</summary>
    public static bool MatchesAllowedExtension(string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 16)
        {
            return false;
        }

        return AllowedExtensions.Contains(extension.TrimStart('.').ToLowerInvariant());
    }

    /// <summary>规范化请求中的附件文件标识列表，去重并保持顺序。</summary>
    public static IReadOnlyList<Guid> NormalizeFileIds(IReadOnlyList<Guid>? attachmentFileIds)
    {
        if (attachmentFileIds is null || attachmentFileIds.Count == 0)
        {
            return [];
        }

        if (attachmentFileIds.Count > MaxAttachmentsPerIntent)
        {
            return [];
        }

        var normalized = new List<Guid>(attachmentFileIds.Count);
        var seen = new HashSet<Guid>();
        foreach (var fileId in attachmentFileIds)
        {
            if (fileId == Guid.Empty || !seen.Add(fileId))
            {
                return [];
            }

            normalized.Add(fileId);
        }

        return normalized;
    }
}
