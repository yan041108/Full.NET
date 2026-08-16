using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 向标准 AuthorizationContributor 集合尾部幂等插入生成片段；非标准形态 fail-closed。
/// </summary>
internal static class AuthorizationContributorIntegrationEditor
{
    /// <summary>若已包含生成标记则跳过，避免重复插入。</summary>
    public static ClientRouteIntegrationEditResult Edit(
        string source,
        string contributorPath,
        string fragment)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(contributorPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragment);

        var marker = ExtractMarker(fragment);
        if (marker is not null
            && source.Contains(marker, StringComparison.Ordinal))
        {
            return ClientRouteIntegrationEditResult.Success(source, source);
        }

        if (!source.Contains(
                "IReadOnlyCollection<PermissionDefinition> Permissions",
                StringComparison.Ordinal)
            || !source.Contains(
                "IReadOnlyCollection<NavigationDefinition> Navigation",
                StringComparison.Ordinal)
            || !source.Contains(
                "IReadOnlyCollection<AuthorizationActionDefinition> Actions",
                StringComparison.Ordinal))
        {
            return ClientRouteIntegrationEditResult.Failure(
                source,
                "AuthorizationContributor 不是标准 Permissions/Navigation/Actions 集合形态。");
        }

        var desired = source.TrimEnd() + "\n\n" + fragment.TrimEnd() + "\n";
        return ClientRouteIntegrationEditResult.Success(source, desired);
    }

    private static string? ExtractMarker(string fragment)
    {
        const string prefix = "// <fullnet-generated ";
        var start = fragment.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var end = fragment.IndexOf('>', start);
        return end < 0 ? null : fragment[start..(end + 1)];
    }
}
