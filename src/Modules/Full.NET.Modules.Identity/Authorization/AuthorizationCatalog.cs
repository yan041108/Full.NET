using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Authorization;

internal sealed class AuthorizationCatalog
{
    private AuthorizationCatalog(
        IReadOnlyList<PermissionDefinition> permissions,
        IReadOnlyList<NavigationDefinition> navigation,
        IReadOnlyList<AuthorizationActionDefinition> actions)
    {
        Permissions = permissions;
        Navigation = navigation;
        Actions = actions;
    }

    public IReadOnlyList<PermissionDefinition> Permissions { get; }

    public IReadOnlyList<NavigationDefinition> Navigation { get; }

    public IReadOnlyList<AuthorizationActionDefinition> Actions { get; }

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
        var actions = materialized
            .SelectMany(contributor => contributor.Actions)
            .ToArray();

        ValidatePermissions(permissions);
        ValidateNavigation(permissions, navigation);
        ValidateActions(permissions, navigation, actions);

        var orderedNavigation = navigation
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var navigationOrder = orderedNavigation
            .Select((item, index) => (item.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);

        return new AuthorizationCatalog(
            permissions.OrderBy(item => item.Code, StringComparer.Ordinal).ToArray(),
            orderedNavigation,
            actions
                .OrderBy(item => navigationOrder[item.NavigationId])
                .ThenBy(item => item.Order)
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
        var permissionsByCode = permissions.ToDictionary(
            item => item.Code,
            item => item,
            StringComparer.Ordinal);
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

            if (!permissionsByCode.ContainsKey(item.RequiredPermission))
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

    private static void ValidateActions(
        IReadOnlyCollection<PermissionDefinition> permissions,
        IReadOnlyCollection<NavigationDefinition> navigation,
        IReadOnlyCollection<AuthorizationActionDefinition> actions)
    {
        EnsureUnique(
            actions.Select(item => item.Id),
            "action id");

        var permissionsByCode = permissions.ToDictionary(
            item => item.Code,
            item => item,
            StringComparer.Ordinal);
        var navigationById = navigation.ToDictionary(
            item => item.Id,
            item => item,
            StringComparer.Ordinal);
        var clientActionKeys = new HashSet<string>(StringComparer.Ordinal);
        var actionPermissionCodes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.Id)
                || string.IsNullOrWhiteSpace(action.NavigationId)
                || string.IsNullOrWhiteSpace(action.PermissionCode)
                || string.IsNullOrWhiteSpace(action.Name)
                || string.IsNullOrWhiteSpace(action.ClientActionKey))
            {
                throw new InvalidOperationException(
                    "Authorization catalog contains an incomplete action definition.");
            }

            if (!navigationById.TryGetValue(action.NavigationId, out var navigationItem))
            {
                throw new InvalidOperationException(
                    $"Action '{action.Id}' references an unknown navigation.");
            }

            if (!permissionsByCode.TryGetValue(action.PermissionCode, out var actionPermission))
            {
                throw new InvalidOperationException(
                    $"Action '{action.Id}' requires an unknown permission.");
            }

            var pagePermission = permissionsByCode[navigationItem.RequiredPermission];
            if ((actionPermission.Scope & pagePermission.Scope) != actionPermission.Scope)
            {
                throw new InvalidOperationException(
                    $"Action '{action.Id}' exceeds the parent page authorization scope.");
            }

            if (string.Equals(
                action.PermissionCode,
                navigationItem.RequiredPermission,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Action '{action.Id}' cannot use the page read permission.");
            }

            var clientActionKey = $"{action.NavigationId}:{action.ClientActionKey}";
            if (!clientActionKeys.Add(clientActionKey))
            {
                throw new InvalidOperationException(
                    $"Authorization catalog contains duplicate action client key '{clientActionKey}'.");
            }

            if (actionPermissionCodes.TryGetValue(action.PermissionCode, out var existingActionId)
                && !string.Equals(existingActionId, action.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Permission '{action.PermissionCode}' is already bound to action '{existingActionId}'.");
            }

            actionPermissionCodes[action.PermissionCode] = action.Id;
        }
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
