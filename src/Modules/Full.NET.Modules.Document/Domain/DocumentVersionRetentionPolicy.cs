namespace Full.NET.Modules.Document.Domain;

/// <summary>文档历史版本保留与删除边界；集中表达不可删除当前版本与最小保留数约束。</summary>
internal static class DocumentVersionRetentionPolicy
{
    /// <summary>判断删除后是否仍满足最小保留版本数。</summary>
    public static bool CanDeleteVersion(
        int totalVersionCount,
        int minimumRetainedVersionsPerItem) =>
        totalVersionCount > Math.Max(minimumRetainedVersionsPerItem, 1);

    /// <summary>计算需要自动裁剪的历史版本超额数量。</summary>
    public static int CountExcessHistoryVersions(
        int totalVersionCount,
        int maximumRetainedHistoryVersions) =>
        maximumRetainedHistoryVersions <= 0
            ? 0
            : Math.Max(0, totalVersionCount - 1 - maximumRetainedHistoryVersions);
}
