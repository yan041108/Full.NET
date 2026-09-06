using System.Text.RegularExpressions;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageHostDictItems;
using Full.NET.Modules.Settings.Features.ManageHostDictTypes;

namespace Full.NET.Modules.Settings.Features.QueryHostEnumCatalogs;

/// <summary>
/// 根据枚举目录与现有 Host 字典计算生成计划；不访问数据库，供预览与执行共用。
/// </summary>
internal static partial class HostEnumCatalogDictGenerationPlanner
{
    /// <summary>计算枚举目录到 Host 字典的生成预览。</summary>
    public static EnumCatalogDictGenerationPreview Plan(
        EnumCatalogDefinition catalog,
        DictTypeIdentityRecord? existingType,
        IReadOnlyList<DictItemIdentityRecord> existingItems)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var itemsByValue = (existingItems ?? [])
            .GroupBy(item => item.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var catalogValues = new HashSet<string>(StringComparer.Ordinal);

        var itemPreviews = catalog.Members
            .OrderBy(member => member.DisplayOrder)
            .ThenBy(member => member.Code, StringComparer.Ordinal)
            .Select(member =>
            {
                catalogValues.Add(member.Code);
                var value = member.Code.Trim();
                if (!IsValidDictItemValue(value))
                {
                    return new EnumCatalogDictGenerationItemPreview(
                        value,
                        member.Label,
                        itemsByValue.GetValueOrDefault(value)?.Label,
                        member.DisplayOrder,
                        EnumCatalogDictGenerationItemActions.InvalidValue);
                }

                if (!itemsByValue.TryGetValue(value, out var existing))
                {
                    return new EnumCatalogDictGenerationItemPreview(
                        value,
                        member.Label,
                        null,
                        member.DisplayOrder,
                        EnumCatalogDictGenerationItemActions.Create);
                }

                if (string.Equals(existing.Label, member.Label, StringComparison.Ordinal))
                {
                    return new EnumCatalogDictGenerationItemPreview(
                        value,
                        member.Label,
                        existing.Label,
                        member.DisplayOrder,
                        EnumCatalogDictGenerationItemActions.SkipExists);
                }

                return new EnumCatalogDictGenerationItemPreview(
                    value,
                    member.Label,
                    existing.Label,
                    member.DisplayOrder,
                    EnumCatalogDictGenerationItemActions.ConflictLabel);
            })
            .ToArray();

        var unmanagedItems = (existingItems ?? [])
            .Where(item => !catalogValues.Contains(item.Value))
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .Select(item => new EnumCatalogDictGenerationUnmanagedItem(
                item.Value,
                item.Label,
                item.IsActive))
            .ToArray();

        return new EnumCatalogDictGenerationPreview(
            catalog.Key,
            catalog.Key,
            catalog.DisplayName,
            existingType is not null,
            existingType is null,
            itemPreviews,
            unmanagedItems);
    }

    private static bool IsValidDictItemValue(string value) =>
        !string.IsNullOrWhiteSpace(value) && DictItemValuePattern().IsMatch(value);

    [GeneratedRegex(
        "^[a-z][a-z0-9_-]{0,126}[a-z0-9]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DictItemValuePattern();
}
