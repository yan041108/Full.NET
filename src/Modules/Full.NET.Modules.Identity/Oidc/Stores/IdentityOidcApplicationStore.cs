using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;

namespace Full.NET.Modules.Identity.Oidc.Stores;

internal sealed class IdentityOidcApplicationStore(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IdentityOidcStoreSqlResolver sqlResolver) : IOpenIddictApplicationStore<IdentityOidcApplication>
{
    public async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcCountRow>(
            IdentityOidcSql.CountApplications,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return row?.Count ?? 0;
    }

    public ValueTask<long> CountAsync<TResult>(Func<IQueryable<IdentityOidcApplication>, IQueryable<TResult>> query, CancellationToken cancellationToken) =>
        throw IdentityOidcStoreSupport.LinqNotSupported();

    public async ValueTask CreateAsync(IdentityOidcApplication application, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        application.CreatedAtUtc = now;
        application.UpdatedAtUtc = now;
        if (application.Version <= 0)
        {
            application.Version = 1;
        }

        await commandExecutor.ExecuteAsync(
            IdentityOidcSql.InsertApplication,
            MapApplication(application),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(IdentityOidcApplication application, CancellationToken cancellationToken)
    {
        var affected = await commandExecutor.ExecuteAsync(
            IdentityOidcSql.DeleteApplication,
            IdentitySqlParameters.Create(("Id", application.Id), ("Version", application.Version)),
            cancellationToken).ConfigureAwait(false);
        IdentityOidcStoreSupport.EnsureConcurrency(affected);
    }

    public async ValueTask<IdentityOidcApplication?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationRow>(
            IdentityOidcSql.FindApplicationById,
            IdentitySqlParameters.Create(("Id", IdentityOidcStoreSupport.ParseRequiredId(identifier))),
            cancellationToken).ConfigureAwait(false);
        return row is null ? null : IdentityOidcRecordMapper.ToApplication(row);
    }

    public async ValueTask<IdentityOidcApplication?> FindByClientIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationRow>(
            IdentityOidcSql.FindApplicationByClientId,
            IdentitySqlParameters.Create(("ClientId", identifier)),
            cancellationToken).ConfigureAwait(false);
        return row is null ? null : IdentityOidcRecordMapper.ToApplication(row);
    }

    public async IAsyncEnumerable<IdentityOidcApplication> FindByPostLogoutRedirectUriAsync(string uri, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcApplicationRow>(
            sqlResolver.FindApplicationsByPostLogoutRedirectUri(),
            IdentitySqlParameters.Create(("Uri", uri)),
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); yield return IdentityOidcRecordMapper.ToApplication(row); }
    }

    public async IAsyncEnumerable<IdentityOidcApplication> FindByRedirectUriAsync(string uri, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcApplicationRow>(
            sqlResolver.FindApplicationsByRedirectUri(),
            IdentitySqlParameters.Create(("Uri", uri)),
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); yield return IdentityOidcRecordMapper.ToApplication(row); }
    }

    public ValueTask<string?> GetApplicationTypeAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(application.ApplicationType);

    public ValueTask<TResult?> GetAsync<TState, TResult>(Func<IQueryable<IdentityOidcApplication>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        throw IdentityOidcStoreSupport.LinqNotSupported();

    public ValueTask<string?> GetClientIdAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(application.ClientId);

    public ValueTask<string?> GetClientSecretAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(application.ClientSecret);

    public ValueTask<string?> GetClientTypeAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(application.ClientType);

    public ValueTask<string?> GetConsentTypeAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(application.ConsentType);

    public ValueTask<string?> GetDisplayNameAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(application.DisplayName);

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeDisplayNames(application.DisplayNamesJson));

    public ValueTask<string?> GetIdAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult<string?>(IdentityOidcStoreSupport.FormatId(application.Id));

    public ValueTask<JsonWebKeySet?> GetJsonWebKeySetAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeJsonWebKeySet(application.JsonWebKeySetJson));

    public ValueTask<ImmutableArray<string>> GetPermissionsAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeStringArray(application.PermissionsJson));

    public ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeStringArray(application.PostLogoutRedirectUrisJson));

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeProperties(application.PropertiesJson));

    public ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeStringArray(application.RedirectUrisJson));

    public ValueTask<ImmutableArray<string>> GetRequirementsAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeStringArray(application.RequirementsJson));

    public ValueTask<ImmutableDictionary<string, string>> GetSettingsAsync(IdentityOidcApplication application, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeSettings(application.SettingsJson));

    public ValueTask<IdentityOidcApplication> InstantiateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new IdentityOidcApplication { Id = Guid.CreateVersion7(), Version = 1 });

    public async IAsyncEnumerable<IdentityOidcApplication> ListAsync(int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcApplicationRow>(
            sqlResolver.ListApplications(),
            IdentitySqlParameters.Create(("Offset", offset ?? 0), ("PageSize", count ?? int.MaxValue)),
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); yield return IdentityOidcRecordMapper.ToApplication(row); }
    }

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(Func<IQueryable<IdentityOidcApplication>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        throw IdentityOidcStoreSupport.LinqNotSupported();

    public ValueTask SetApplicationTypeAsync(IdentityOidcApplication application, string? type, CancellationToken cancellationToken)
    {
        application.ApplicationType = type;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetClientIdAsync(IdentityOidcApplication application, string? identifier, CancellationToken cancellationToken)
    {
        application.ClientId = identifier;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetClientSecretAsync(IdentityOidcApplication application, string? secret, CancellationToken cancellationToken)
    {
        application.ClientSecret = secret;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetClientTypeAsync(IdentityOidcApplication application, string? type, CancellationToken cancellationToken)
    {
        application.ClientType = type;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetConsentTypeAsync(IdentityOidcApplication application, string? type, CancellationToken cancellationToken)
    {
        application.ConsentType = type;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetDisplayNameAsync(IdentityOidcApplication application, string? name, CancellationToken cancellationToken)
    {
        application.DisplayName = name;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetDisplayNamesAsync(IdentityOidcApplication application, ImmutableDictionary<CultureInfo, string> names, CancellationToken cancellationToken)
    {
        application.DisplayNamesJson = IdentityOidcStoreSupport.SerializeDisplayNames(names);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetJsonWebKeySetAsync(IdentityOidcApplication application, JsonWebKeySet? set, CancellationToken cancellationToken)
    {
        application.JsonWebKeySetJson = IdentityOidcStoreSupport.SerializeJsonWebKeySet(set);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPermissionsAsync(IdentityOidcApplication application, ImmutableArray<string> permissions, CancellationToken cancellationToken)
    {
        application.PermissionsJson = IdentityOidcStoreSupport.SerializeStringArray(permissions);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPostLogoutRedirectUrisAsync(IdentityOidcApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)
    {
        application.PostLogoutRedirectUrisJson = IdentityOidcStoreSupport.SerializeStringArray(uris);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPropertiesAsync(IdentityOidcApplication application, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        application.PropertiesJson = IdentityOidcStoreSupport.SerializeProperties(properties);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetRedirectUrisAsync(IdentityOidcApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)
    {
        application.RedirectUrisJson = IdentityOidcStoreSupport.SerializeStringArray(uris);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetRequirementsAsync(IdentityOidcApplication application, ImmutableArray<string> requirements, CancellationToken cancellationToken)
    {
        application.RequirementsJson = IdentityOidcStoreSupport.SerializeStringArray(requirements);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetSettingsAsync(IdentityOidcApplication application, ImmutableDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        application.SettingsJson = IdentityOidcStoreSupport.SerializeSettings(settings);
        return ValueTask.CompletedTask;
    }

    public async ValueTask UpdateAsync(IdentityOidcApplication application, CancellationToken cancellationToken)
    {
        application.UpdatedAtUtc = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
            IdentityOidcSql.UpdateApplication,
            MapApplication(application),
            cancellationToken).ConfigureAwait(false);
        IdentityOidcStoreSupport.EnsureConcurrency(affected);
        application.Version++;
    }

    private static Dictionary<string, object?> MapApplication(IdentityOidcApplication application) =>
        IdentitySqlParameters.Create(
            ("Id", application.Id),
            ("ClientId", application.ClientId),
            ("ClientSecret", application.ClientSecret),
            ("ConsentType", application.ConsentType),
            ("DisplayName", application.DisplayName),
            ("DisplayNamesJson", application.DisplayNamesJson),
            ("PermissionsJson", application.PermissionsJson),
            ("PostLogoutRedirectUrisJson", application.PostLogoutRedirectUrisJson),
            ("PropertiesJson", application.PropertiesJson),
            ("RedirectUrisJson", application.RedirectUrisJson),
            ("RequirementsJson", application.RequirementsJson),
            ("ApplicationType", application.ApplicationType),
            ("JsonWebKeySetJson", application.JsonWebKeySetJson),
            ("SettingsJson", application.SettingsJson),
            ("ClientType", application.ClientType),
            ("Version", application.Version),
            ("CreatedAtUtc", application.CreatedAtUtc),
            ("UpdatedAtUtc", application.UpdatedAtUtc));
}