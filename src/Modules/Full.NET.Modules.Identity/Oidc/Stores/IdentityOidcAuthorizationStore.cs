using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using OpenIddict.Abstractions;

namespace Full.NET.Modules.Identity.Oidc.Stores;

internal sealed class IdentityOidcAuthorizationStore(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IdentityOidcStoreSqlResolver sqlResolver) : IOpenIddictAuthorizationStore<IdentityOidcAuthorization>
{
    public async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcCountRow>(
            IdentityOidcSql.CountAuthorizations,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return row?.Count ?? 0;
    }

    public ValueTask<long> CountAsync<TResult>(Func<IQueryable<IdentityOidcAuthorization>, IQueryable<TResult>> query, CancellationToken cancellationToken) =>
        throw IdentityOidcStoreSupport.LinqNotSupported();

    public async ValueTask CreateAsync(IdentityOidcAuthorization authorization, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        authorization.CreatedAtUtc = now;
        authorization.UpdatedAtUtc = now;
        if (authorization.Version <= 0)
        {
            authorization.Version = 1;
        }

        await commandExecutor.ExecuteAsync(
            IdentityOidcSql.InsertAuthorization,
            MapAuthorization(authorization),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(IdentityOidcAuthorization authorization, CancellationToken cancellationToken)
    {
        var affected = await commandExecutor.ExecuteAsync(
            IdentityOidcSql.DeleteAuthorization,
            IdentitySqlParameters.Create(("Id", authorization.Id), ("Version", authorization.Version)),
            cancellationToken).ConfigureAwait(false);
        IdentityOidcStoreSupport.EnsureConcurrency(affected);
    }

    public async IAsyncEnumerable<IdentityOidcAuthorization> FindAsync(
        string? subject, string? client, string? status, string? type,
        ImmutableArray<string>? scopes, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcAuthorizationRow>(
            IdentityOidcSql.FindAuthorizationsByFilter,
            IdentitySqlParameters.Create(
                ("Subject", subject),
                ("ApplicationId", IdentityOidcStoreSupport.ParseOptionalId(client)),
                ("Status", status),
                ("Type", type)),
            cancellationToken).ConfigureAwait(false);
        var items = rows.Select(IdentityOidcRecordMapper.ToAuthorization).ToArray();
        if (scopes is { IsDefault: false } && scopes.Value.Length > 0)
        {
            items = items.Where(item =>
            {
                var itemScopes = IdentityOidcStoreSupport.DeserializeStringArray(item.ScopesJson);
                return scopes.Value.All(scope => itemScopes.Contains(scope, StringComparer.Ordinal));
            }).ToArray();
        }

        foreach (var item in items) { cancellationToken.ThrowIfCancellationRequested(); yield return item; }
    }

    public async IAsyncEnumerable<IdentityOidcAuthorization> FindByApplicationIdAsync(string identifier, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcAuthorizationRow>(
            IdentityOidcSql.FindAuthorizationsByApplicationId,
            IdentitySqlParameters.Create(("ApplicationId", IdentityOidcStoreSupport.ParseRequiredId(identifier))),
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); yield return IdentityOidcRecordMapper.ToAuthorization(row); }
    }

    public async ValueTask<IdentityOidcAuthorization?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcAuthorizationRow>(
            IdentityOidcSql.FindAuthorizationById,
            IdentitySqlParameters.Create(("Id", IdentityOidcStoreSupport.ParseRequiredId(identifier))),
            cancellationToken).ConfigureAwait(false);
        return row is null ? null : IdentityOidcRecordMapper.ToAuthorization(row);
    }

    public async IAsyncEnumerable<IdentityOidcAuthorization> FindBySubjectAsync(string subject, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcAuthorizationRow>(
            IdentityOidcSql.FindAuthorizationsBySubject,
            IdentitySqlParameters.Create(("Subject", subject)),
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); yield return IdentityOidcRecordMapper.ToAuthorization(row); }
    }

    public ValueTask<string?> GetApplicationIdAsync(IdentityOidcAuthorization authorization, CancellationToken cancellationToken) =>
        ValueTask.FromResult(authorization.ApplicationId is Guid id ? IdentityOidcStoreSupport.FormatId(id) : null);

    public ValueTask<TResult?> GetAsync<TState, TResult>(Func<IQueryable<IdentityOidcAuthorization>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        throw IdentityOidcStoreSupport.LinqNotSupported();

    public ValueTask<DateTimeOffset?> GetCreationDateAsync(IdentityOidcAuthorization authorization, CancellationToken cancellationToken) =>
        ValueTask.FromResult(authorization.CreationDateUtc);

    public ValueTask<string?> GetIdAsync(IdentityOidcAuthorization authorization, CancellationToken cancellationToken) =>
        ValueTask.FromResult<string?>(IdentityOidcStoreSupport.FormatId(authorization.Id));

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(IdentityOidcAuthorization authorization, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeProperties(authorization.PropertiesJson));

    public ValueTask<ImmutableArray<string>> GetScopesAsync(IdentityOidcAuthorization authorization, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeStringArray(authorization.ScopesJson));

    public ValueTask<string?> GetStatusAsync(IdentityOidcAuthorization authorization, CancellationToken cancellationToken) =>
        ValueTask.FromResult(authorization.Status);

    public ValueTask<string?> GetSubjectAsync(IdentityOidcAuthorization authorization, CancellationToken cancellationToken) =>
        ValueTask.FromResult(authorization.Subject);

    public ValueTask<string?> GetTypeAsync(IdentityOidcAuthorization authorization, CancellationToken cancellationToken) =>
        ValueTask.FromResult(authorization.Type);

    public ValueTask<IdentityOidcAuthorization> InstantiateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new IdentityOidcAuthorization { Id = Guid.CreateVersion7(), Version = 1 });

    public async IAsyncEnumerable<IdentityOidcAuthorization> ListAsync(int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcAuthorizationRow>(
            sqlResolver.ListAuthorizations(),
            IdentitySqlParameters.Create(("Offset", offset ?? 0), ("PageSize", count ?? int.MaxValue)),
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); yield return IdentityOidcRecordMapper.ToAuthorization(row); }
    }

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(Func<IQueryable<IdentityOidcAuthorization>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        throw IdentityOidcStoreSupport.LinqNotSupported();

    public async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
            IdentityOidcSql.PruneAuthorizations,
            IdentitySqlParameters.Create(
                ("Threshold", threshold),
                ("ValidStatus", OpenIddictConstants.Statuses.Valid)),
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<long> RevokeAsync(string? subject, string? client, string? status, string? type, CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
            IdentityOidcSql.RevokeAuthorizationsByFilter,
            IdentitySqlParameters.Create(
                ("Subject", subject),
                ("ApplicationId", IdentityOidcStoreSupport.ParseOptionalId(client)),
                ("StatusFilter", status),
                ("Type", type),
                ("RevokedStatus", OpenIddictConstants.Statuses.Revoked),
                ("UpdatedAtUtc", clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
            IdentityOidcSql.RevokeAuthorizationsByApplicationId,
            IdentitySqlParameters.Create(
                ("ApplicationId", IdentityOidcStoreSupport.ParseRequiredId(identifier)),
                ("RevokedStatus", OpenIddictConstants.Statuses.Revoked),
                ("UpdatedAtUtc", clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
            IdentityOidcSql.RevokeAuthorizationsBySubject,
            IdentitySqlParameters.Create(
                ("Subject", subject),
                ("RevokedStatus", OpenIddictConstants.Statuses.Revoked),
                ("UpdatedAtUtc", clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);

    public ValueTask SetApplicationIdAsync(IdentityOidcAuthorization authorization, string? identifier, CancellationToken cancellationToken)
    {
        authorization.ApplicationId = IdentityOidcStoreSupport.ParseOptionalId(identifier);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetCreationDateAsync(IdentityOidcAuthorization authorization, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        authorization.CreationDateUtc = date;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPropertiesAsync(IdentityOidcAuthorization authorization, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        authorization.PropertiesJson = IdentityOidcStoreSupport.SerializeProperties(properties);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetScopesAsync(IdentityOidcAuthorization authorization, ImmutableArray<string> scopes, CancellationToken cancellationToken)
    {
        authorization.ScopesJson = IdentityOidcStoreSupport.SerializeStringArray(scopes);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetStatusAsync(IdentityOidcAuthorization authorization, string? status, CancellationToken cancellationToken)
    {
        authorization.Status = status;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetSubjectAsync(IdentityOidcAuthorization authorization, string? subject, CancellationToken cancellationToken)
    {
        authorization.Subject = subject;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetTypeAsync(IdentityOidcAuthorization authorization, string? type, CancellationToken cancellationToken)
    {
        authorization.Type = type;
        return ValueTask.CompletedTask;
    }

    public async ValueTask UpdateAsync(IdentityOidcAuthorization authorization, CancellationToken cancellationToken)
    {
        authorization.UpdatedAtUtc = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
            IdentityOidcSql.UpdateAuthorization,
            MapAuthorization(authorization),
            cancellationToken).ConfigureAwait(false);
        IdentityOidcStoreSupport.EnsureConcurrency(affected);
        authorization.Version++;
    }

    private static Dictionary<string, object?> MapAuthorization(IdentityOidcAuthorization authorization) =>
        IdentitySqlParameters.Create(
            ("Id", authorization.Id),
            ("ApplicationId", authorization.ApplicationId),
            ("CreationDateUtc", authorization.CreationDateUtc),
            ("PropertiesJson", authorization.PropertiesJson),
            ("ScopesJson", authorization.ScopesJson),
            ("Status", authorization.Status),
            ("Subject", authorization.Subject),
            ("Type", authorization.Type),
            ("Version", authorization.Version),
            ("CreatedAtUtc", authorization.CreatedAtUtc),
            ("UpdatedAtUtc", authorization.UpdatedAtUtc));
}