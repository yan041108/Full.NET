namespace Full.NET.Modules.Platform.Domain;

/// <summary>
/// 将 semver 风格版本标签编码为可排序的 64 位整数，供列表与“最新未读”查询使用。
/// </summary>
/// <remarks>
/// 支持 1–4 个点分数字段，每段取值 0–999；缺失段视为 0。
/// 例如 <c>1.2.3</c> 编码为 <c>1_002_003_000</c> 基数展开。
/// </remarks>
internal static class ReleaseNoteVersionSortKey
{
    private const int MaxSegmentCount = 4;
    private const int MaxSegmentValue = 999;
    private const long SegmentMultiplier = 1_000L;

    /// <summary>
    /// 尝试将版本标签解析为排序键。
    /// </summary>
    /// <param name="versionLabel">原始版本标签。</param>
    /// <param name="sortKey">解析成功时输出的排序键。</param>
    /// <returns>标签合法时返回 <see langword="true"/>。</returns>
    public static bool TryParse(string? versionLabel, out long sortKey)
    {
        sortKey = 0;
        if (string.IsNullOrWhiteSpace(versionLabel))
        {
            return false;
        }

        var trimmed = versionLabel.Trim();
        if (trimmed.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = trimmed.Split('.', StringSplitOptions.TrimEntries);
        if (segments.Length is < 1 or > MaxSegmentCount || segments.Any(string.IsNullOrEmpty))
        {
            return false;
        }

        var encodedSegments = new long[MaxSegmentCount];
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length is < 1 or > 3
                || !int.TryParse(segment, out var value)
                || value is < 0 or > MaxSegmentValue)
            {
                return false;
            }

            encodedSegments[index] = value;
        }

        sortKey = encodedSegments[0];
        for (var index = 1; index < MaxSegmentCount; index++)
        {
            sortKey = (sortKey * SegmentMultiplier) + encodedSegments[index];
        }

        return true;
    }
}
