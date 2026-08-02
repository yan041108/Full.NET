using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.GetAuthorizationTree;

internal sealed class AuthorizationTreeProjector(AuthorizationCatalog catalog)
{
    public IReadOnlyList<AuthorizationTreePageResponse> ProjectHostTree()
    {
        var hostPermissions = catalog.Permissions
            .Where(permission => permission.Scope.HasFlag(AuthorizationScope.Host))
            .Select(permission => permission.Code)
            .ToHashSet(StringComparer.Ordinal);
        var actionsByNavigation = catalog.Actions
            .Where(action => hostPermissions.Contains(action.PermissionCode)
                && !IsNonAssignablePermission(action.PermissionCode))
            .GroupBy(action => action.NavigationId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(action => action.Order)
                    .ThenBy(action => action.Id, StringComparer.Ordinal)
                    .Select(action => new AuthorizationTreeActionResponse(
                        action.Id,
                        action.Name,
                        action.PermissionCode,
                        action.Order))
                    .ToArray(),
                StringComparer.Ordinal);
        var childrenByParent = catalog.Navigation
            .Where(definition => hostPermissions.Contains(definition.RequiredPermission)
                && !IsNonAssignablePermission(definition.RequiredPermission))
            .GroupBy(definition => definition.ParentId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(definition => definition.Order)
                    .ThenBy(definition => definition.Id, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return ProjectPages(string.Empty, childrenByParent, actionsByNavigation);
    }

    private static IReadOnlyList<AuthorizationTreePageResponse> ProjectPages(
        string parentKey,
        IReadOnlyDictionary<string, NavigationDefinition[]> childrenByParent,
        IReadOnlyDictionary<string, AuthorizationTreeActionResponse[]> actionsByNavigation)
    {
        if (!childrenByParent.TryGetValue(parentKey, out var definitions))
        {
            return [];
        }

        return definitions
            .Select(definition => new AuthorizationTreePageResponse(
                definition.Id,
                definition.Title,
                definition.RequiredPermission,
                definition.Order,
                actionsByNavigation.TryGetValue(definition.Id, out var actions)
                    ? actions
                    : [],
                ProjectPages(definition.Id, childrenByParent, actionsByNavigation)))
            .ToArray();
    }

    private static bool IsNonAssignablePermission(string permissionCode) =>
        permissionCode.StartsWith(
            "identity.super_administrators.",
            StringComparison.Ordinal);
}