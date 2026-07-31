namespace Full.NET.Modules.Settings.Catalogs;

/// <summary>服务端与管理端共同发布的稳定 Grid 定义。</summary>
internal sealed record GridPreferenceDefinition(
    string GridKey,
    int SchemaVersion,
    IReadOnlySet<string> ColumnKeys);

/// <summary>
/// Grid 偏好可信目录；远端输入只能引用这里发布的展示键，不能借偏好扩展数据权限。
/// </summary>
internal static class GridPreferenceCatalog
{
    private static readonly IReadOnlyDictionary<string, GridPreferenceDefinition>
        Definitions = new Dictionary<string, GridPreferenceDefinition>(
            StringComparer.Ordinal)
        {
            ["identity.users"] = new(
                "identity.users",
                1,
                new HashSet<string>(
                    ["displayName", "username", "status", "actions"],
                    StringComparer.Ordinal)),
        };

    public static bool TryGet(
        string gridKey,
        out GridPreferenceDefinition definition) =>
        Definitions.TryGetValue(gridKey, out definition!);

    public static GridPreferenceDefinition GetRequired(string gridKey) =>
        Definitions.TryGetValue(gridKey, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown Grid key: {gridKey}");
}
