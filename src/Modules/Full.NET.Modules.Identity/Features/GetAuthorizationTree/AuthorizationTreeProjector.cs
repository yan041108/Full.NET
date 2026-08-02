using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.GetAuthorizationTree;

internal sealed class AuthorizationTreeProjector(AuthorizationCatalog catalog)
{
    public IReadOnlyList<AuthorizationTreeModuleResponse> ProjectHostTree()
    {
        var assignablePermissions = catalog.Permissions
            .Where(IsAssignablePermission)
            .Select(permission => permission.Code)
            .ToHashSet(StringComparer.Ordinal);
        var actionsByNavigation = catalog.Actions
            .Where(action => assignablePermissions.Contains(action.PermissionCode))
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
            .Where(definition => assignablePermissions.Contains(definition.RequiredPermission))
            .GroupBy(definition => definition.ParentId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(definition => definition.Order)
                    .ThenBy(definition => definition.Id, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var pagesByModule = catalog.Modules.ToDictionary(
            module => module.Key,
            _ => new List<AuthorizationTreePageResponse>(),
            StringComparer.Ordinal);

        foreach (var page in ProjectPages(string.Empty, childrenByParent, actionsByNavigation))
        {
            if (!catalog.NavigationModuleKeys.TryGetValue(page.Id, out var moduleKey)
                || !pagesByModule.TryGetValue(moduleKey, out var pages))
            {
                throw new InvalidOperationException(
                    $"Authorization tree page '{page.Id}' is not owned by a known module.");
            }

            pages.Add(page);
        }

        return catalog.Modules
            .Select(module => new AuthorizationTreeModuleResponse(
                module.Key,
                module.Title,
                module.Order,
                pagesByModule.TryGetValue(module.Key, out var pages)
                    ? pages
                    : []))
            .Where(module => module.Pages.Count > 0)
            .ToArray();
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

    private static bool IsAssignablePermission(PermissionDefinition permission) =>
        !permission.Code.StartsWith(
            "identity.super_administrators.",
            StringComparison.Ordinal)
        && (permission.Scope.HasFlag(AuthorizationScope.Host)
            || permission.Scope == AuthorizationScope.Tenant);
}
