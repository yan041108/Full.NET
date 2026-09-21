using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.GetNavigation;

internal sealed class NavigationProjector(AuthorizationCatalog catalog)
{
    private readonly Dictionary<string, AuthorizationScope> _permissionScopes =
        catalog.Permissions.ToDictionary(
            permission => permission.Code,
            permission => permission.Scope,
            StringComparer.Ordinal);

    public IReadOnlyList<NavigationNodeResponse> Project(
        IEnumerable<string> permissions,
        bool isHostDataContext,
        IEnumerable<NavigationDefinition>? additionalDefinitions = null)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var granted = permissions.ToHashSet(StringComparer.Ordinal);
        var persisted = (additionalDefinitions ?? []).ToArray();
        var persistedIds = persisted
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var catalogOnly = catalog.Navigation
            .Where(item => !persistedIds.Contains(item.Id));
        var allDefinitions = catalogOnly
            .Concat(persisted)
            .ToArray();
        var childrenByParent = allDefinitions
            .Where(item => item.ParentId is not null)
            .GroupBy(item => item.ParentId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(item => item.Order).ToArray(),
                StringComparer.Ordinal);

        return allDefinitions
            .Where(item => item.ParentId is null)
            .OrderBy(item => item.Order)
            .Select(item => ProjectNode(item, childrenByParent, granted, isHostDataContext))
            .OfType<NavigationNodeResponse>()
            .ToArray();
    }

    private NavigationNodeResponse? ProjectNode(
        NavigationDefinition definition,
        IReadOnlyDictionary<string, NavigationDefinition[]> childrenByParent,
        ISet<string> granted,
        bool isHostDataContext)
    {
        if (!granted.Contains(definition.RequiredPermission)
            || !IsNavigationPermissionVisible(definition.RequiredPermission, isHostDataContext))
        {
            return null;
        }

        var hasDefinedChildren = childrenByParent.TryGetValue(
            definition.Id,
            out var childDefinitions);
        var children = hasDefinedChildren
            ? childDefinitions!
                .Select(child => ProjectNode(child, childrenByParent, granted, isHostDataContext))
                .OfType<NavigationNodeResponse>()
                .ToArray()
            : [];
        if (hasDefinedChildren && children.Length == 0)
        {
            return null;
        }

        return new NavigationNodeResponse(
            definition.Id,
            definition.ParentId,
            definition.RouteName,
            definition.Path,
            definition.ComponentKey,
            definition.Title,
            definition.Caption,
            definition.Icon,
            definition.Order,
            definition.RequiredPermission,
            children,
            definition.MenuType,
            definition.Redirect,
            definition.LinkUrl,
            definition.IsHidden,
            definition.IsKeepAlive,
            definition.IsAffix,
            definition.IsEmbedded);
    }

    /// <summary>
    /// 按当前数据上下文裁剪导航；超级管理员在租户上下文中仍可能携带 Host 权限 Claim，但不得暴露 HostOnly 页面入口。
    /// </summary>
    private bool IsNavigationPermissionVisible(
        string permissionCode,
        bool isHostDataContext)
    {
        if (!_permissionScopes.TryGetValue(permissionCode, out var scope))
        {
            return true;
        }

        var requiredMask = isHostDataContext
            ? AuthorizationScope.Host
            : AuthorizationScope.Tenant;
        return (scope & requiredMask) != 0;
    }
}
