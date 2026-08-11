using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.GetNavigation;

/// <summary>
/// 从持久化表加载活动 Host 导航（含系统内置项），供 <see cref="NavigationProjector"/> 覆盖代码目录。
/// </summary>
internal sealed class HostNavigationDefinitionLoader(IQueryExecutor queryExecutor)
{
    public async Task<IReadOnlyList<NavigationDefinition>> LoadActiveDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<IdentityNavigationRecord>(
                IdentitySql.ListActiveHostMenus,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var records = rows.ToArray();
        if (records.Length == 0)
        {
            return [];
        }

        var routeNamesById = records.ToDictionary(
            record => record.Id,
            record => record.RouteName,
            comparer: EqualityComparer<Guid>.Default);
        return records
            .Where(record => !string.Equals(
                record.MenuType,
                IdentityHostMenuTypes.Button,
                StringComparison.Ordinal))
            .Select(record => Map(record, routeNamesById))
            .ToArray();
    }

    private static NavigationDefinition Map(
        IdentityNavigationRecord record,
        IReadOnlyDictionary<Guid, string> routeNamesById)
    {
        string? parentId = null;
        if (record.ParentId is Guid parentKey
            && routeNamesById.TryGetValue(parentKey, out var parentRouteName))
        {
            parentId = parentRouteName;
        }

        return new NavigationDefinition(
            record.RouteName,
            parentId,
            record.RouteName,
            record.Path,
            record.ComponentKey,
            record.Title,
            record.Caption,
            record.Icon,
            record.DisplayOrder,
            record.RequiredPermission,
            string.Equals(record.MenuType, IdentityHostMenuTypes.Directory, StringComparison.Ordinal)
                ? IdentityHostMenuTypes.Directory
                : IdentityHostMenuTypes.Menu,
            record.Redirect,
            record.LinkUrl,
            record.IsHidden,
            record.IsKeepAlive,
            record.IsAffix,
            record.IsEmbedded);
    }
}
