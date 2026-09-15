using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using OpenIddict.Abstractions;

namespace Full.NET.Modules.Identity.Oidc.Stores;

internal sealed class IdentityOidcScopeStore(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IdentityOidcStoreSqlResolver sqlResolver) : IOpenIddictScopeStore<IdentityOidcScope>
{
    public async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcCountRow>(
            IdentityOidcSql.CountScopes,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return row?.Count ?? 0;
    }

    public ValueTask<long> CountAsync<TResult>(Func<IQueryable<IdentityOidcScope>, IQueryable<TResult>> query, CancellationToken cancellationToken) =>
        throw IdentityOidcStoreSupport.LinqNotSupported();

    public async ValueTask CreateAsync(IdentityOidcScope scope, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        scope.CreatedAtUtc = now;
        scope.UpdatedAtUtc = now;
        if (scope.Version <= 0)
        {
            scope.Version = 1;
        }

        await commandExecutor.ExecuteAsync(
            IdentityOidcSql.InsertScope,
            MapScope(scope),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(IdentityOidcScope scope, CancellationToken cancellationToken)
    {
        var affected = await commandExecutor.ExecuteAsync(
            IdentityOidcSql.DeleteScope,
            IdentitySqlParameters.Create(("Id", scope.Id), ("Version", scope.Version)),
            cancellationToken).ConfigureAwait(false);
        IdentityOidcStoreSupport.EnsureConcurrency(affected);
    }

    public async ValueTask<IdentityOidcScope?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcScopeRow>(
            IdentityOidcSql.FindScopeById,
            IdentitySqlParameters.Create(("Id", IdentityOidcStoreSupport.ParseRequiredId(identifier))),
            cancellationToken).ConfigureAwait(false);
        return row is null ? null : IdentityOidcRecordMapper.ToScope(row);
    }

    public async ValueTask<IdentityOidcScope?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcScopeRow>(
            IdentityOidcSql.FindScopeByName,
            IdentitySqlParameters.Create(("Name", name)),
            cancellationToken).ConfigureAwait(false);
        return row is null ? null : IdentityOidcRecordMapper.ToScope(row);
    }

    public async IAsyncEnumerable<IdentityOidcScope> FindByNamesAsync(
        ImmutableArray<string> names,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (names.IsDefault || names.Length == 0)
        {
            yield break;
        }

        var scopes = new List<IdentityOidcScope>(names.Length);
        foreach (var name in names)
        {
            var scope = await FindByNameAsync(name, cancellationToken).ConfigureAwait(false);
            if (scope is not null)
            {
                scopes.Add(scope);
            }
        }

        foreach (var scope in scopes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return scope;
        }
    }

    public async IAsyncEnumerable<IdentityOidcScope> FindByResourceAsync(
        string resource,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcScopeRow>(
            sqlResolver.FindScopesByResource(),
            IdentitySqlParameters.Create(("Resource", resource)),
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return IdentityOidcRecordMapper.ToScope(row);
        }
    }

    public ValueTask<TResult?> GetAsync<TState, TResult>(Func<IQueryable<IdentityOidcScope>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        throw IdentityOidcStoreSupport.LinqNotSupported();

    public ValueTask<string?> GetDescriptionAsync(IdentityOidcScope scope, CancellationToken cancellationToken) =>
        ValueTask.FromResult(scope.Description);

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDescriptionsAsync(IdentityOidcScope scope, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeDisplayNames(scope.DescriptionsJson));

    public ValueTask<string?> GetDisplayNameAsync(IdentityOidcScope scope, CancellationToken cancellationToken) =>
        ValueTask.FromResult(scope.DisplayName);

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(IdentityOidcScope scope, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeDisplayNames(scope.DisplayNamesJson));

    public ValueTask<string?> GetIdAsync(IdentityOidcScope scope, CancellationToken cancellationToken) =>
        ValueTask.FromResult<string?>(IdentityOidcStoreSupport.FormatId(scope.Id));

    public ValueTask<string?> GetNameAsync(IdentityOidcScope scope, CancellationToken cancellationToken) =>
        ValueTask.FromResult(scope.Name);

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(IdentityOidcScope scope, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeProperties(scope.PropertiesJson));

    public ValueTask<ImmutableArray<string>> GetResourcesAsync(IdentityOidcScope scope, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeStringArray(scope.ResourcesJson));

    public ValueTask<IdentityOidcScope> InstantiateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new IdentityOidcScope { Id = Guid.CreateVersion7(), Version = 1 });

    public async IAsyncEnumerable<IdentityOidcScope> ListAsync(int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcScopeRow>(
            sqlResolver.ListScopes(),
            IdentitySqlParameters.Create(("Offset", offset ?? 0), ("PageSize", count ?? int.MaxValue)),
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); yield return IdentityOidcRecordMapper.ToScope(row); }
    }

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(Func<IQueryable<IdentityOidcScope>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        throw IdentityOidcStoreSupport.LinqNotSupported();

    public ValueTask SetDescriptionAsync(IdentityOidcScope scope, string? description, CancellationToken cancellationToken)
    {
        scope.Description = description;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetDescriptionsAsync(IdentityOidcScope scope, ImmutableDictionary<CultureInfo, string> descriptions, CancellationToken cancellationToken)
    {
        scope.DescriptionsJson = IdentityOidcStoreSupport.SerializeDisplayNames(descriptions);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetDisplayNameAsync(IdentityOidcScope scope, string? name, CancellationToken cancellationToken)
    {
        scope.DisplayName = name;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetDisplayNamesAsync(IdentityOidcScope scope, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)
    {
        scope.DisplayNamesJson = IdentityOidcStoreSupport.SerializeDisplayNames(names);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetNameAsync(IdentityOidcScope scope, string? name, CancellationToken cancellationToken)
    {
        scope.Name = name;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPropertiesAsync(IdentityOidcScope scope, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        scope.PropertiesJson = IdentityOidcStoreSupport.SerializeProperties(properties);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetResourcesAsync(IdentityOidcScope scope, ImmutableArray<string> resources, CancellationToken cancellationToken)
    {
        scope.ResourcesJson = IdentityOidcStoreSupport.SerializeStringArray(resources);
        return ValueTask.CompletedTask;
    }

    public async ValueTask UpdateAsync(IdentityOidcScope scope, CancellationToken cancellationToken)
    {
        scope.UpdatedAtUtc = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
            IdentityOidcSql.UpdateScope,
            MapScope(scope),
            cancellationToken).ConfigureAwait(false);
        IdentityOidcStoreSupport.EnsureConcurrency(affected);
        scope.Version++;
    }

    private static Dictionary<string, object?> MapScope(IdentityOidcScope scope) =>
        IdentitySqlParameters.Create(
            ("Id", scope.Id),
            ("Name", scope.Name),
            ("Description", scope.Description),
            ("DescriptionsJson", scope.DescriptionsJson),
            ("DisplayName", scope.DisplayName),
            ("DisplayNamesJson", scope.DisplayNamesJson),
            ("PropertiesJson", scope.PropertiesJson),
            ("ResourcesJson", scope.ResourcesJson),
            ("Version", scope.Version),
            ("CreatedAtUtc", scope.CreatedAtUtc),
            ("UpdatedAtUtc", scope.UpdatedAtUtc));
}