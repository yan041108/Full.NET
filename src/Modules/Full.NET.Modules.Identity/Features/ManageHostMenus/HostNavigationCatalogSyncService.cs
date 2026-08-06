using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostMenus;

internal sealed class HostNavigationCatalogSyncService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    AuthorizationCatalog authorizationCatalog,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const string HostScope = "host";

    public async Task<(int Created, int Skipped)> SyncMissingCatalogEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        var definitions = authorizationCatalog.Navigation.ToArray();
        if (definitions.Length == 0)
        {
            return (0, 0);
        }

        var routeNameIndex = await LoadRouteNameIndexAsync(cancellationToken)
            .ConfigureAwait(false);
        var pending = definitions
            .Where(definition => !routeNameIndex.ContainsKey(definition.RouteName))
            .ToList();
        if (pending.Count == 0)
        {
            return (0, definitions.Length);
        }

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
                    && !routeNameIndex.ContainsKey(definition.ParentId))
                {
                    continue;
                }

                Guid? parentId = null;
                if (definition.ParentId is not null)
                {
                    parentId = routeNameIndex[definition.ParentId];
                }

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
                            1),
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

        return (created, definitions.Length - created);
    }

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

    private sealed class HostMenuRouteNameRow
    {
        public Guid Id { get; set; }

        public string RouteName { get; set; } = string.Empty;
    }
}