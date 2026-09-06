namespace Full.NET.Modules.Regions.Domain;

/// <summary>
/// 行政区域父子链接，用于环检测与树遍历。
/// </summary>
/// <param name="Id">区域标识。</param>
/// <param name="ParentId">父级区域标识。</param>
internal readonly record struct AdministrativeRegionParentLink(Guid Id, Guid? ParentId);

/// <summary>
/// 行政区域树环检测规则；在更新父级或导入数据集时防止形成环。
/// </summary>
internal static class AdministrativeRegionTreeRules
{
    /// <summary>
    /// 判断将 <paramref name="regionId"/> 的父级设置为 <paramref name="newParentId"/> 是否会形成环。
    /// </summary>
    /// <param name="regionId">待更新区域标识。</param>
    /// <param name="newParentId">新父级区域标识。</param>
    /// <param name="parentLinks">当前全部父子链接快照。</param>
    /// <returns>会形成环时返回 <see langword="true"/>。</returns>
    public static bool WouldCreateParentCycle(
        Guid regionId,
        Guid newParentId,
        IReadOnlyList<AdministrativeRegionParentLink> parentLinks)
    {
        var parentById = parentLinks.ToDictionary(link => link.Id, link => link.ParentId);
        var current = (Guid?)newParentId;
        var seen = new HashSet<Guid>();
        while (current is Guid id)
        {
            if (id == regionId)
            {
                return true;
            }

            if (!seen.Add(id))
            {
                return true;
            }

            if (!parentById.TryGetValue(id, out var parent))
            {
                break;
            }

            current = parent;
        }

        return false;
    }

    /// <summary>
    /// 检测导入项按 <c>parentCode</c> 链接后是否存在环。
    /// </summary>
    /// <param name="itemsByCode">已通过编码校验的导入项映射。</param>
    /// <returns>存在环时返回 <see langword="true"/>。</returns>
    public static bool ImportWouldCreateCycle(
        IReadOnlyDictionary<string, ImportRegionItemSnapshot> itemsByCode)
    {
        foreach (var code in itemsByCode.Keys)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = code;
            while (itemsByCode.TryGetValue(current, out var item))
            {
                if (!seen.Add(current))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(item.ParentCode))
                {
                    break;
                }

                current = item.ParentCode.Trim();
            }
        }

        return false;
    }
}

/// <summary>导入环检测使用的轻量快照。</summary>
/// <param name="Code">区域编码。</param>
/// <param name="ParentCode">父级区域编码。</param>
internal readonly record struct ImportRegionItemSnapshot(string Code, string? ParentCode);
