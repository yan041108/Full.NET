namespace Full.NET.Modules.Organization.TenantUnits;

/// <summary>机构上级链解析规则，供 Workflow 办理人解析与发布校验复用。</summary>
internal static class OrganizationUnitAncestryRules
{
    /// <summary>允许向上解析的最小层级（1 表示主部门的直接上级部门）。</summary>
    public const int MinimumAncestorLevel = 1;

    /// <summary>允许向上解析的最大层级，防止异常组织树导致无界遍历。</summary>
    public const int MaximumAncestorLevel = 20;

    /// <summary>判断上级层级是否在受控闭区间内。</summary>
    /// <param name="ancestorLevel">向上层级。</param>
    /// <returns>层级有效时返回 <see langword="true"/>。</returns>
    public static bool IsValidAncestorLevel(int ancestorLevel) =>
        ancestorLevel is >= MinimumAncestorLevel and <= MaximumAncestorLevel;

    /// <summary>
    /// 从起始机构沿上级链向上解析目标机构；层级不足或检测到环时失败关闭。
    /// </summary>
    /// <param name="startUnitId">起始机构单元标识。</param>
    /// <param name="ancestorLevel">向上层级，1 表示直接上级机构。</param>
    /// <param name="parentLookup">按机构标识查询活动上级机构的回调。</param>
    /// <returns>目标机构标识；无法解析时返回 <see langword="null"/>。</returns>
    public static Guid? ResolveAncestorUnitId(
        Guid startUnitId,
        int ancestorLevel,
        Func<Guid, Guid?> parentLookup)
    {
        if (!IsValidAncestorLevel(ancestorLevel))
        {
            return null;
        }

        var visited = new HashSet<Guid> { startUnitId };
        var current = startUnitId;
        for (var depth = 0; depth < ancestorLevel; depth++)
        {
            var parentId = parentLookup(current);
            if (parentId is null)
            {
                return null;
            }

            if (!visited.Add(parentId.Value))
            {
                return null;
            }

            current = parentId.Value;
        }

        return current;
    }
}
