using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.ManageHostMenus;

internal sealed class HostNavigationCatalogSyncService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    AuthorizationCatalog authorizationCatalog,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const string HostScope = "host";

    public async Task<(int Created, int Skipped, int Reparented)> SyncMissingCatalogEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        var routeNameIndex = await LoadRouteNameIndexAsync(cancellationToken)
            .ConfigureAwait(false);
        var moduleCreated = await SyncModuleDirectoriesAsync(
                routeNameIndex,
                cancellationToken)
            .ConfigureAwait(false);
        var navigationCreated = await SyncMissingNavigationEntriesAsync(
                routeNameIndex,
                cancellationToken)
            .ConfigureAwait(false);
        var buttonCreated = await SyncMissingActionEntriesAsync(
                routeNameIndex,
                cancellationToken)
            .ConfigureAwait(false);
        var reparented = await ReparentRootMenusToModulesAsync(
                routeNameIndex,
                cancellationToken)
            .ConfigureAwait(false);
        var totalDefinitions = authorizationCatalog.Navigation.Count
            + authorizationCatalog.Actions.Count
            + authorizationCatalog.Modules.Count;
        var created = moduleCreated + navigationCreated + buttonCreated;
        return (created, totalDefinitions - navigationCreated - buttonCreated, reparented);
    }

    private async Task<int> SyncModuleDirectoriesAsync(
        Dictionary<string, Guid> routeNameIndex,
        CancellationToken cancellationToken)
    {
        var created = 0;
        var now = clock.UtcNow;

        foreach (var module in authorizationCatalog.Modules)
        {
            var routeName = BuildModuleDirectoryRouteName(module.Key);
            if (routeNameIndex.ContainsKey(routeName))
            {
                continue;
            }

            var menuId = idGenerator.NewId();
            var affectedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.InsertHostMenu,
                    new InsertIdentityNavigation(
                        menuId,
                        null,
                        HostScope,
                        null,
                        routeName,
                        BuildModuleDirectoryPath(module.Key),
                        "layout",
                        module.Title,
                        module.Title,
                        "grid",
                        module.Order * 100,
                        ResolveModuleDirectoryPermission(module.Key),
                        true,
                        true,
                        now,
                        null,
                        1,
                        IdentityHostMenuTypes.Directory,
                        null,
                        null,
                        false,
                        false,
                        false,
                        false,
                        null),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affectedRows != 1)
            {
                throw new InvalidOperationException(
                    $"Host module directory sync insert affected {affectedRows} rows instead of one.");
            }

            routeNameIndex[routeName] = menuId;
            created++;
        }

        return created;
    }

    private async Task<int> SyncMissingNavigationEntriesAsync(
        Dictionary<string, Guid> routeNameIndex,
        CancellationToken cancellationToken)
    {
        var definitions = authorizationCatalog.Navigation.ToArray();
        if (definitions.Length == 0)
        {
            return 0;
        }

        var pending = definitions
            .Where(definition => !routeNameIndex.ContainsKey(definition.RouteName))
            .ToList();
        if (pending.Count == 0)
        {
            return 0;
        }

        var navigationById = definitions.ToDictionary(
            item => item.Id,
            StringComparer.Ordinal);
        var created = 0;
        var now = clock.UtcNow;
        var guard = 0;
        while (pending.Count > 0 && guard < pending.Count + definitions.Length)
        {
            guard++;
            for (var index = pending.Count - 1; index >= 0; index--)
            {
                var definition = pending[index];
                if (definition.ParentId is not null
                    && (!navigationById.TryGetValue(definition.ParentId, out var parentDefinition)
                        || !routeNameIndex.ContainsKey(parentDefinition.RouteName)))
                {
                    continue;
                }

                var parentId = ResolveNavigationParentId(definition, navigationById, routeNameIndex);
                var menuId = idGenerator.NewId();
                var affectedRows = await commandExecutor.ExecuteAsync(
                        IdentitySql.InsertHostMenu,
                        new InsertIdentityNavigation(
                            menuId,
                            null,
                            HostScope,
                            parentId,
                            definition.RouteName,
                            definition.Path,
                            definition.ComponentKey,
                            definition.Title,
                            definition.Caption,
                            definition.Icon,
                            definition.Order,
                            definition.RequiredPermission,
                            true,
                            true,
                            now,
                            null,
                            1,
                            definition.MenuType,
                            definition.Redirect,
                            definition.LinkUrl,
                            definition.IsHidden,
                            definition.IsKeepAlive,
                            definition.IsAffix,
                            definition.IsEmbedded,
                            null),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (affectedRows != 1)
                {
                    throw new InvalidOperationException(
                        $"Host navigation catalog sync insert affected {affectedRows} rows instead of one.");
                }

                routeNameIndex[definition.RouteName] = menuId;
                pending.RemoveAt(index);
                created++;
            }
        }

        if (pending.Count > 0)
        {
            var missingParents = string.Join(
                ", ",
                pending.Select(item => item.RouteName));
            throw new InvalidOperationException(
                $"Host navigation catalog sync could not resolve parents for: {missingParents}.");
        }

        return created;
    }

    private async Task<int> SyncMissingActionEntriesAsync(
        Dictionary<string, Guid> routeNameIndex,
        CancellationToken cancellationToken)
    {
        var navigationById = authorizationCatalog.Navigation.ToDictionary(
            item => item.Id,
            StringComparer.Ordinal);
        var created = 0;
        var now = clock.UtcNow;

        foreach (var action in authorizationCatalog.Actions)
        {
            if (!navigationById.TryGetValue(action.NavigationId, out var navigation))
            {
                continue;
            }

            var routeName = BuildActionRouteName(action.Id);
            if (routeNameIndex.ContainsKey(routeName))
            {
                continue;
            }

            if (!routeNameIndex.TryGetValue(navigation.RouteName, out var parentMenuId))
            {
                continue;
            }

            var menuId = idGenerator.NewId();
            var affectedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.InsertHostMenu,
                    new InsertIdentityNavigation(
                        menuId,
                        null,
                        HostScope,
                        parentMenuId,
                        routeName,
                        string.Empty,
                        action.ClientActionKey,
                        action.Name,
                        action.Name,
                        "key",
                        action.Order,
                        action.PermissionCode,
                        true,
                        true,
                        now,
                        null,
                        1,
                        IdentityHostMenuTypes.Button,
                        null,
                        null,
                        false,
                        false,
                        false,
                        false,
                        null),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affectedRows != 1)
            {
                throw new InvalidOperationException(
                    $"Host action catalog sync insert affected {affectedRows} rows instead of one.");
            }

            routeNameIndex[routeName] = menuId;
            created++;
        }

        return created;
    }

    private async Task<int> ReparentRootMenusToModulesAsync(
        Dictionary<string, Guid> routeNameIndex,
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<HostMenuSyncRow>(
                IdentitySql.ListHostMenuSyncRows,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var navigationByRouteName = authorizationCatalog.Navigation.ToDictionary(
            item => item.RouteName,
            StringComparer.Ordinal);
        var navigationById = authorizationCatalog.Navigation.ToDictionary(
            item => item.Id,
            StringComparer.Ordinal);
        var actionByRouteName = authorizationCatalog.Actions.ToDictionary(
            action => BuildActionRouteName(action.Id),
            StringComparer.Ordinal);
        var reparented = 0;
        var now = clock.UtcNow;

        foreach (var row in rows)
        {
            if (row.ParentId is not null || IsModuleDirectoryRouteName(row.RouteName))
            {
                continue;
            }

            Guid? targetParentId = null;
            if (navigationByRouteName.TryGetValue(row.RouteName, out var navigation))
            {
                targetParentId = ResolveNavigationParentId(
                    navigation,
                    navigationById,
                    routeNameIndex);
            }
            else if (actionByRouteName.TryGetValue(row.RouteName, out var action)
                && navigationById.TryGetValue(action.NavigationId, out var pageNavigation))
            {
                if (routeNameIndex.TryGetValue(pageNavigation.RouteName, out var pageMenuId))
                {
                    targetParentId = pageMenuId;
                }
            }

            if (targetParentId is not Guid parentMenuId || parentMenuId == row.Id)
            {
                continue;
            }

            var affectedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.ReparentHostSystemMenu,
                    IdentitySqlParameters.Create(
                        ("MenuId", row.Id),
                        ("ParentId", parentMenuId),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affectedRows == 1)
            {
                reparented++;
            }
        }

        return reparented;
    }

    private Guid? ResolveNavigationParentId(
        NavigationDefinition definition,
        IReadOnlyDictionary<string, NavigationDefinition> navigationById,
        IReadOnlyDictionary<string, Guid> routeNameIndex)
    {
        if (definition.ParentId is not null
            && navigationById.TryGetValue(definition.ParentId, out var parentDefinition)
            && routeNameIndex.TryGetValue(parentDefinition.RouteName, out var explicitParentId))
        {
            return explicitParentId;
        }

        return ResolveModuleDirectoryId(definition.Id, routeNameIndex);
    }

    private Guid? ResolveModuleDirectoryId(
        string navigationId,
        IReadOnlyDictionary<string, Guid> routeNameIndex)
    {
        if (!authorizationCatalog.NavigationModuleKeys.TryGetValue(navigationId, out var moduleKey))
        {
            return null;
        }

        return routeNameIndex.TryGetValue(
            BuildModuleDirectoryRouteName(moduleKey),
            out var moduleDirectoryId)
            ? moduleDirectoryId
            : null;
    }

    private string ResolveModuleDirectoryPermission(string moduleKey)
    {
        foreach (var navigation in authorizationCatalog.Navigation)
        {
            if (authorizationCatalog.NavigationModuleKeys.TryGetValue(navigation.Id, out var mappedModuleKey)
                && string.Equals(mappedModuleKey, moduleKey, StringComparison.Ordinal))
            {
                return navigation.RequiredPermission;
            }
        }

        return authorizationCatalog.Permissions[0].Code;
    }

    internal static string BuildActionRouteName(string actionId) =>
        actionId.Replace('.', '-');

    internal static string BuildModuleDirectoryRouteName(string moduleKey) =>
        $"module-{moduleKey}";

    internal static string BuildModuleDirectoryPath(string moduleKey) =>
        $"/modules/{moduleKey}";

    internal static bool IsModuleDirectoryRouteName(string routeName) =>
        routeName.StartsWith("module-", StringComparison.Ordinal);

    private async Task<Dictionary<string, Guid>> LoadRouteNameIndexAsync(
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<HostMenuRouteNameRow>(
                IdentitySql.ListHostMenuRouteNames,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(
            row => row.RouteName,
            row => row.Id,
            StringComparer.Ordinal);
    }

    internal sealed class HostMenuRouteNameRow
    {
        public Guid Id { get; set; }

        public string RouteName { get; set; } = string.Empty;
    }

    internal sealed class HostMenuSyncRow
    {
        public Guid Id { get; set; }

        public Guid? ParentId { get; set; }

        public string RouteName { get; set; } = string.Empty;

        public string MenuType { get; set; } = string.Empty;
    }
}
