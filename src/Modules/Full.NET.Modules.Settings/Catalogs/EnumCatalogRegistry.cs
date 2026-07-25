using System.Text.RegularExpressions;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.Modules.Settings.Catalogs;

/// <summary>
/// 合并各模块枚举/常量 Contributor，启动时校验唯一键与成员完整性。
/// </summary>
internal sealed partial class EnumCatalogRegistry
{
    private readonly IReadOnlyDictionary<string, EnumCatalogDefinition> _catalogsByKey;
    private readonly IReadOnlyList<EnumCatalogDefinition> _orderedCatalogs;

    public EnumCatalogRegistry(IEnumerable<IEnumCatalogContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        var definitions = contributors
            .SelectMany(contributor => contributor.Catalogs)
            .ToArray();

        Validate(definitions);

        _orderedCatalogs = definitions
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        _catalogsByKey = _orderedCatalogs.ToDictionary(
            item => item.Key,
            StringComparer.Ordinal);
    }

    public IReadOnlyList<EnumCatalogDefinition> List() => _orderedCatalogs;

    public EnumCatalogDefinition? FindByKey(string catalogKey)
    {
        var key = catalogKey?.Trim().ToLowerInvariant() ?? string.Empty;
        return _catalogsByKey.TryGetValue(key, out var definition)
            ? definition
            : null;
    }

    private static void Validate(IReadOnlyCollection<EnumCatalogDefinition> definitions)
    {
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var catalog in definitions)
        {
            if (string.IsNullOrWhiteSpace(catalog.Key)
                || !CatalogKeyPattern().IsMatch(catalog.Key))
            {
                throw new InvalidOperationException(
                    "Enum catalog key must be 3-128 lowercase letters, numbers, dots, underscores, or hyphens.");
            }

            if (string.IsNullOrWhiteSpace(catalog.DisplayName))
            {
                throw new InvalidOperationException(
                    $"Enum catalog '{catalog.Key}' is missing a display name.");
            }

            if (catalog.Members is null || catalog.Members.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Enum catalog '{catalog.Key}' must declare at least one member.");
            }

            if (!seenKeys.Add(catalog.Key))
            {
                throw new InvalidOperationException(
                    $"Enum catalog key '{catalog.Key}' is registered more than once.");
            }

            var seenCodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in catalog.Members)
            {
                if (string.IsNullOrWhiteSpace(member.Code)
                    || string.IsNullOrWhiteSpace(member.Label))
                {
                    throw new InvalidOperationException(
                        $"Enum catalog '{catalog.Key}' contains an incomplete member.");
                }

                if (!seenCodes.Add(member.Code))
                {
                    throw new InvalidOperationException(
                        $"Enum catalog '{catalog.Key}' contains duplicate member code '{member.Code}'.");
                }
            }
        }
    }

    [GeneratedRegex(
        "^[a-z][a-z0-9._-]{1,126}[a-z0-9]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CatalogKeyPattern();
}
