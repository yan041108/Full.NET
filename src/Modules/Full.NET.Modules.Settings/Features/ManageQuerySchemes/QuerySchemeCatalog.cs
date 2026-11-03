using System.Collections.Frozen;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.Modules.Settings.Features.ManageQuerySchemes;

/// <summary>仅收集已装配业务的静态 Schema；重复或不安全目录在目录构建时失败关闭。</summary>
internal sealed class QuerySchemeCatalog
{
    private readonly FrozenDictionary<string, QuerySchemeDefinition> definitions;

    public QuerySchemeCatalog(IEnumerable<IQuerySchemeContributor> contributors)
    {
        var collected = new Dictionary<string, QuerySchemeDefinition>(StringComparer.Ordinal);
        foreach (var definition in contributors.SelectMany(contributor => contributor.Definitions))
        {
            if (!SafeKey(definition.GridKey) || definition.SchemaVersion < 1 || !SafeKey(definition.ReadPermission)
                || !definition.SupportsHost && !definition.SupportsTenant
                || definition.Filters.Count > 8 || definition.ColumnKeys.Count is < 1 or > 32 || definition.SortKeys.Count is < 1 or > 32
                || definition.Filters.Any(field => !SafeKey(field.FieldKey) || field.MaxLength is < 1 or > 512)
                || definition.Filters.Select(field => field.FieldKey).Distinct(StringComparer.Ordinal).Count() != definition.Filters.Count
                || !ValidKeys(definition.SortKeys) || !ValidKeys(definition.ColumnKeys) || !collected.TryAdd(definition.GridKey, definition))
                throw new InvalidOperationException("查询方案业务目录无效或重复。");
        }
        definitions = collected.ToFrozenDictionary(StringComparer.Ordinal);
    }

    internal bool TryGet(string gridKey, out QuerySchemeDefinition definition) => definitions.TryGetValue(gridKey, out definition!);
    private static bool ValidKeys(IReadOnlyList<string> keys) => keys.All(SafeKey) && keys.Distinct(StringComparer.Ordinal).Count() == keys.Count;
    private static bool SafeKey(string key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 128
        && key.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}
