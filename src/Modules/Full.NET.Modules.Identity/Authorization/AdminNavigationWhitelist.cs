namespace Full.NET.Modules.Identity.Authorization;

/// <summary>
/// 与服务端 <c>packages/client-contracts</c> 导航白名单保持一致的 Host 可发布组件键。
/// </summary>
internal static class AdminNavigationWhitelist
{
    internal sealed record Entry(string RouteName, string Path);

    private static readonly IReadOnlyDictionary<string, Entry> Entries =
        new Dictionary<string, Entry>(StringComparer.Ordinal)
        {
            ["overview"] = new("overview", "/"),
            ["tenant-context"] = new("tenant-context", "/tenant-context"),
            ["users"] = new("users", "/identity/users"),
            ["roles"] = new("roles", "/identity/roles"),
            ["menus"] = new("menus", "/identity/menus"),
            ["org-units"] = new("org-units", "/organization/units"),
            ["super-administrators"] = new(
                "super-administrators",
                "/identity/super-administrators"),
        };

    public static bool TryGetEntry(string componentKey, out Entry entry) =>
        Entries.TryGetValue(componentKey, out entry!);

    public static bool IsReservedRouteName(string routeName) =>
        Entries.Values.Any(entry =>
            string.Equals(entry.RouteName, routeName, StringComparison.Ordinal));
}
