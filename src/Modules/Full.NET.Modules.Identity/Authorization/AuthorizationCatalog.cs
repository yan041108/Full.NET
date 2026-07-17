using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Authorization;

internal sealed class AuthorizationCatalog
{
    private AuthorizationCatalog(
        IReadOnlyList<PermissionDefinition> permissions,
        IReadOnlyList<NavigationDefinition> navigation)
    {
        Permissions = permissions;
        Navigation = navigation;
    }

    public IReadOnlyList<PermissionDefinition> Permissions { get; }

    public IReadOnlyList<NavigationDefinition> Navigation { get; }

    public static AuthorizationCatalog Create(
        IEnumerable<IAuthorizationCatalogContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        var materialized = contributors.ToArray();
        var permissions = materialized
            .SelectMany(contributor => contributor.Permissions)
            .ToArray();
        var navigation = materialized
            .SelectMany(contributor => contributor.Navigation)
            .ToArray();

        ValidatePermissions(permissions);
        ValidateNavigation(permissions, navigation);

        return new AuthorizationCatalog(
            permissions.OrderBy(item => item.Code, StringComparer.Ordinal).ToArray(),
            navigation
                .OrderBy(item => item.Order)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray());
    }

    private static void ValidatePermissions(
        IReadOnlyCollection<PermissionDefinition> permissions)
    {
        foreach (var permission in permissions)
        {
            if (string.IsNullOrWhiteSpace(permission.Code)
                || string.IsNullOrWhiteSpace(permission.Name)
                || permission.Scope == 0)
            {
                throw new InvalidOperationException(
                    "Authorization catalog contains an incomplete permission definition.");
            }
        }

        EnsureUnique(
            permissions.Select(item => item.Code),
            "permission code");
    }

    private static void ValidateNavigation(
        IReadOnlyCollection<PermissionDefinition> permissions,
        IReadOnlyCollection<NavigationDefinition> navigation)
    {
        EnsureUnique(
            navigation.Select(item => item.Id),
            "navigation id");
        var permissionCodes = permissions
            .Select(item => item.Code)
            .ToHashSet(StringComparer.Ordinal);
        var navigationIds = navigation
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var item in navigation)
        {
            if (string.IsNullOrWhiteSpace(item.Id)
                || string.IsNullOrWhiteSpace(item.RouteName)
                || string.IsNullOrWhiteSpace(item.Path)
                || string.IsNullOrWhiteSpace(item.ComponentKey)
                || string.IsNullOrWhiteSpace(item.Title)
                || string.IsNullOrWhiteSpace(item.RequiredPermission))
            {
                throw new InvalidOperationException(
                    "Authorization catalog contains an incomplete navigation definition.");
            }

            if (!permissionCodes.Contains(item.RequiredPermission))
            {
                throw new InvalidOperationException(
                    $"Navigation '{item.Id}' requires an unknown permission.");
            }

            if (item.ParentId is not null && !navigationIds.Contains(item.ParentId))
            {
                throw new InvalidOperationException(
                    $"Navigation '{item.Id}' references an unknown parent.");
            }
        }

        EnsureAcyclic(navigation);
    }

    private static void EnsureAcyclic(
        IReadOnlyCollection<NavigationDefinition> navigation)
    {
        var parents = navigation.ToDictionary(
            item => item.Id,
            item => item.ParentId,
            StringComparer.Ordinal);

        foreach (var item in navigation)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? current = item.Id;
            while (current is not null)
            {
                if (!visited.Add(current))
                {
                    throw new InvalidOperationException(
                        $"Navigation '{item.Id}' participates in a parent cycle.");
                }

                current = parents[current];
            }
        }
    }

    private static void EnsureUnique(
        IEnumerable<string> values,
        string definitionName)
    {
        var duplicate = values
            .GroupBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Authorization catalog contains duplicate {definitionName} '{duplicate.Key}'.");
        }
    }
}
