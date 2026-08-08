using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.GetNavigation;

internal sealed class NavigationProjector(AuthorizationCatalog catalog)
{
    public IReadOnlyList<NavigationNodeResponse> Project(
        IEnumerable<string> permissions,
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
            .Select(item => ProjectNode(item, childrenByParent, granted))
            .OfType<NavigationNodeResponse>()
            .ToArray();
    }

    private static NavigationNodeResponse? ProjectNode(
        NavigationDefinition definition,
        IReadOnlyDictionary<string, NavigationDefinition[]> childrenByParent,
        ISet<string> granted)
    {
        if (!granted.Contains(definition.RequiredPermission))
        {
            return null;
        }

        var hasDefinedChildren = childrenByParent.TryGetValue(
            definition.Id,
            out var childDefinitions);
        var children = hasDefinedChildren
            ? childDefinitions!
                .Select(child => ProjectNode(child, childrenByParent, granted))
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
}
