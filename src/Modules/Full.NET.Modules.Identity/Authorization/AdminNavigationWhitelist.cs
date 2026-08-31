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
            ["online-sessions"] = new("online-sessions", "/identity/online-sessions"),
            ["api-keys"] = new("api-keys", "/identity/api-keys"),
            ["modules"] = new("modules", "/identity/modules"),
            ["roles"] = new("roles", "/identity/roles"),
            ["menus"] = new("menus", "/identity/menus"),
            ["org-units"] = new("org-units", "/organization/units"),
            ["org-user-units"] = new("org-user-units", "/organization/user-units"),
            ["org-positions"] = new("org-positions", "/organization/positions"),
            ["org-user-positions"] = new("org-user-positions", "/organization/user-positions"),
            ["super-administrators"] = new(
                "super-administrators",
                "/identity/super-administrators"),
            ["dict-types"] = new("dict-types", "/settings/dict-types"),
            ["config-entries"] = new("config-entries", "/settings/config-entries"),
            ["enum-catalogs"] = new("enum-catalogs", "/settings/enum-catalogs"),
            ["access-logs"] = new("access-logs", "/auditing/access-logs"),
            ["operation-logs"] = new("operation-logs", "/auditing/operation-logs"),
            ["exception-logs"] = new("exception-logs", "/auditing/exception-logs"),
            ["outbound-call-logs"] = new("outbound-call-logs", "/auditing/outbound-call-logs"),
            ["host-files"] = new("host-files", "/files/host-files"),
            ["host-announcements"] = new("host-announcements", "/notifications/host-announcements"),
            ["inbox-messages"] = new("inbox-messages", "/notifications/inbox-messages"),
            ["notification-templates"] = new("notification-templates", "/notifications/templates"),
            ["notification-provider-profiles"] = new(
                "notification-provider-profiles",
                "/notifications/provider-profiles"),
            ["notification-bindings"] = new("notification-bindings", "/notifications/bindings"),
            ["notification-deliveries"] = new("notification-deliveries", "/notifications/deliveries"),
            ["notification-preferences"] = new("notification-preferences", "/notifications/preferences"),
            ["host-jobs"] = new("host-jobs", "/jobs/host-definitions"),
            ["layout"] = new("layout", "/"),
        };

    public static bool TryGetEntry(string componentKey, out Entry entry) =>
        Entries.TryGetValue(componentKey, out entry!);

    public static bool IsReservedRouteName(string routeName) =>
        Entries.Values.Any(entry =>
            string.Equals(entry.RouteName, routeName, StringComparison.Ordinal));
}
