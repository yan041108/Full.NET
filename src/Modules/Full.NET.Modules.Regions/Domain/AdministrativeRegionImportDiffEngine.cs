using Full.NET.Modules.Regions.Contracts;

namespace Full.NET.Modules.Regions.Domain;

/// <summary>导入差异计算使用的既有区域快照。</summary>
internal sealed record AdministrativeRegionDiffSnapshot(
    string Code,
    string Name,
    string? ShortName,
    string? MergerName,
    string? ZipCode,
    string? CityCode,
    int Level,
    string? RegionType,
    string? PinYin,
    decimal? Longitude,
    decimal? Latitude,
    int DisplayOrder,
    string? ParentCode);

/// <summary>
/// 按稳定 <see cref="AdministrativeRegionDiffSnapshot.Code"/> 计算导入预览差异。
/// </summary>
internal static class AdministrativeRegionImportDiffEngine
{
    /// <summary>
    /// 计算导入预览差异。
    /// </summary>
    /// <param name="existingByCode">既有区域按编码索引的快照。</param>
    /// <param name="normalizedItems">已通过校验的导入项。</param>
    /// <param name="mergeMode">合并模式：<c>merge</c> 或 <c>replace</c>。</param>
    /// <param name="skippedCount">跳过的无效或重复导入项数量。</param>
    public static ImportAdministrativeRegionsPreviewResponse Compute(
        IReadOnlyDictionary<string, AdministrativeRegionDiffSnapshot> existingByCode,
        IReadOnlyList<AdministrativeRegionDiffSnapshot> normalizedItems,
        string mergeMode,
        int skippedCount)
    {
        var importByCode = normalizedItems
            .GroupBy(item => item.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        var added = new List<ImportAdministrativeRegionAddedSummary>();
        var updated = new List<ImportAdministrativeRegionUpdatedSummary>();
        foreach (var item in importByCode.Values.OrderBy(item => item.Code, StringComparer.Ordinal))
        {
            if (!existingByCode.TryGetValue(item.Code, out var existing))
            {
                added.Add(new ImportAdministrativeRegionAddedSummary(item.Code, item.Name, item.Level));
                continue;
            }

            var changedFields = CollectChangedFields(existing, item);
            if (changedFields.Count > 0)
            {
                updated.Add(new ImportAdministrativeRegionUpdatedSummary(item.Code, item.Name, changedFields));
            }
        }

        var removed = new List<ImportAdministrativeRegionRemovedSummary>();
        if (string.Equals(mergeMode, AdministrativeRegionImportMergeModes.Replace, StringComparison.Ordinal))
        {
            foreach (var existing in existingByCode.Values.OrderBy(item => item.Code, StringComparer.Ordinal))
            {
                if (!importByCode.ContainsKey(existing.Code))
                {
                    removed.Add(new ImportAdministrativeRegionRemovedSummary(existing.Code, existing.Name));
                }
            }
        }

        return new ImportAdministrativeRegionsPreviewResponse(added, updated, removed, skippedCount);
    }

    private static List<string> CollectChangedFields(
        AdministrativeRegionDiffSnapshot existing,
        AdministrativeRegionDiffSnapshot incoming)
    {
        var changed = new List<string>();
        if (!string.Equals(existing.Name, incoming.Name, StringComparison.Ordinal))
        {
            changed.Add("name");
        }

        if (!string.Equals(existing.ShortName, incoming.ShortName, StringComparison.Ordinal))
        {
            changed.Add("shortName");
        }

        if (!string.Equals(existing.MergerName, incoming.MergerName, StringComparison.Ordinal))
        {
            changed.Add("mergerName");
        }

        if (!string.Equals(existing.ZipCode, incoming.ZipCode, StringComparison.Ordinal))
        {
            changed.Add("zipCode");
        }

        if (!string.Equals(existing.CityCode, incoming.CityCode, StringComparison.Ordinal))
        {
            changed.Add("cityCode");
        }

        if (existing.Level != incoming.Level)
        {
            changed.Add("level");
        }

        if (!string.Equals(existing.RegionType, incoming.RegionType, StringComparison.Ordinal))
        {
            changed.Add("regionType");
        }

        if (!string.Equals(existing.PinYin, incoming.PinYin, StringComparison.Ordinal))
        {
            changed.Add("pinYin");
        }

        if (existing.Longitude != incoming.Longitude)
        {
            changed.Add("longitude");
        }

        if (existing.Latitude != incoming.Latitude)
        {
            changed.Add("latitude");
        }

        if (existing.DisplayOrder != incoming.DisplayOrder)
        {
            changed.Add("displayOrder");
        }

        if (!string.Equals(existing.ParentCode, incoming.ParentCode, StringComparison.Ordinal))
        {
            changed.Add("parentCode");
        }

        return changed;
    }
}
