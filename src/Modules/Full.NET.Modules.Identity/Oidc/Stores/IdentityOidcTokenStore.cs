using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using OpenIddict.Abstractions;

namespace Full.NET.Modules.Identity.Oidc.Stores;

internal sealed class IdentityOidcTokenStore(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IdentityOidcStoreSqlResolver sqlResolver) : IOpenIddictTokenStore<IdentityOidcToken>
{
    public async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcCountRow>(
            IdentityOidcSql.CountTokens,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return row?.Count ?? 0;
    }

    public ValueTask<long> CountAsync<TResult>(Func<IQueryable<IdentityOidcToken>, IQueryable<TResult>> query, CancellationToken cancellationToken) =>
        throw IdentityOidcStoreSupport.LinqNotSupported();

    public async ValueTask CreateAsync(IdentityOidcToken token, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        token.CreatedAtUtc = now;
        token.UpdatedAtUtc = now;
        if (token.Version <= 0)
        {
            token.Version = 1;
        }

        token.PropertiesJson = IdentityOidcStoreSupport.WriteSessionId(token.PropertiesJson, token.SessionId);
        await commandExecutor.ExecuteAsync(
            IdentityOidcSql.InsertToken,
            MapToken(token),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DeleteAsync(IdentityOidcToken token, CancellationToken cancellationToken)
    {
        var affected = await commandExecutor.ExecuteAsync(
            IdentityOidcSql.DeleteToken,
            IdentitySqlParameters.Create(("Id", token.Id), ("Version", token.Version)),
            cancellationToken).ConfigureAwait(false);
        IdentityOidcStoreSupport.EnsureConcurrency(affected);
    }

    public async IAsyncEnumerable<IdentityOidcToken> FindAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcTokenRow>(
            IdentityOidcSql.FindTokensByFilter,
            IdentitySqlParameters.Create(
                ("Subject", subject),
                ("ApplicationId", IdentityOidcStoreSupport.ParseOptionalId(client)),
                ("Status", status),
                ("Type", type)),
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return IdentityOidcRecordMapper.ToToken(row);
        }
    }

    public async IAsyncEnumerable<IdentityOidcToken> FindByApplicationIdAsync(string identifier, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcTokenRow>(
            IdentityOidcSql.FindTokensByApplicationId,
            IdentitySqlParameters.Create(("ApplicationId", IdentityOidcStoreSupport.ParseRequiredId(identifier))),
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); yield return IdentityOidcRecordMapper.ToToken(row); }
    }

    public async IAsyncEnumerable<IdentityOidcToken> FindByAuthorizationIdAsync(string identifier, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcTokenRow>(
            IdentityOidcSql.FindTokensByAuthorizationId,
            IdentitySqlParameters.Create(("AuthorizationId", IdentityOidcStoreSupport.ParseRequiredId(identifier))),
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); yield return IdentityOidcRecordMapper.ToToken(row); }
    }

    public async ValueTask<IdentityOidcToken?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcTokenRow>(
            IdentityOidcSql.FindTokenById,
            IdentitySqlParameters.Create(("Id", IdentityOidcStoreSupport.ParseRequiredId(identifier))),
            cancellationToken).ConfigureAwait(false);
        return row is null ? null : IdentityOidcRecordMapper.ToToken(row);
    }

    public async ValueTask<IdentityOidcToken?> FindByReferenceIdAsync(string identifier, CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcTokenRow>(
            IdentityOidcSql.FindTokenByReferenceId,
            IdentitySqlParameters.Create(("ReferenceId", identifier)),
            cancellationToken).ConfigureAwait(false);
        return row is null ? null : IdentityOidcRecordMapper.ToToken(row);
    }

    public async IAsyncEnumerable<IdentityOidcToken> FindBySubjectAsync(string subject, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcTokenRow>(
            IdentityOidcSql.FindTokensBySubject,
            IdentitySqlParameters.Create(("Subject", subject)),
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); yield return IdentityOidcRecordMapper.ToToken(row); }
    }

    public ValueTask<string?> GetApplicationIdAsync(IdentityOidcToken token, CancellationToken cancellationToken) =>
        ValueTask.FromResult(token.ApplicationId is Guid id ? IdentityOidcStoreSupport.FormatId(id) : null);

    public ValueTask<TResult?> GetAsync<TState, TResult>(Func<IQueryable<IdentityOidcToken>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        throw IdentityOidcStoreSupport.LinqNotSupported();

    public ValueTask<string?> GetAuthorizationIdAsync(IdentityOidcToken token, CancellationToken cancellationToken) =>
        ValueTask.FromResult(token.AuthorizationId is Guid id ? IdentityOidcStoreSupport.FormatId(id) : null);

    public ValueTask<DateTimeOffset?> GetCreationDateAsync(IdentityOidcToken token, CancellationToken cancellationToken) =>
        ValueTask.FromResult(token.CreationDateUtc);

    public ValueTask<DateTimeOffset?> GetExpirationDateAsync(IdentityOidcToken token, CancellationToken cancellationToken) =>
        ValueTask.FromResult(token.ExpirationDateUtc);

    public ValueTask<string?> GetIdAsync(IdentityOidcToken token, CancellationToken cancellationToken) =>
        ValueTask.FromResult<string?>(IdentityOidcStoreSupport.FormatId(token.Id));

    public ValueTask<string?> GetPayloadAsync(IdentityOidcToken token, CancellationToken cancellationToken) =>
        ValueTask.FromResult(token.Payload);

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(IdentityOidcToken token, CancellationToken cancellationToken) =>
        ValueTask.FromResult(IdentityOidcStoreSupport.DeserializeProperties(token.PropertiesJson));

    public ValueTask<DateTimeOffset?> GetRedemptionDateAsync(IdentityOidcToken token, CancellationToken cancellationToken) =>
        ValueTask.FromResult(token.RedemptionDateUtc);

    public ValueTask<string?> GetReferenceIdAsync(IdentityOidcToken token, CancellationToken cancellationToken) =>
        ValueTask.FromResult(token.ReferenceId);

    public ValueTask<string?> GetSessionIdAsync(IdentityOidcToken token, CancellationToken cancellationToken) =>
        ValueTask.FromResult(token.SessionId);

    public ValueTask<string?> GetStatusAsync(IdentityOidcToken token, CancellationToken cancellationToken) =>
        ValueTask.FromResult(token.Status);

    public ValueTask<string?> GetSubjectAsync(IdentityOidcToken token, CancellationToken cancellationToken) =>
        ValueTask.FromResult(token.Subject);

    public ValueTask<string?> GetTypeAsync(IdentityOidcToken token, CancellationToken cancellationToken) =>
        ValueTask.FromResult(token.Type);

    public ValueTask<IdentityOidcToken> InstantiateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new IdentityOidcToken { Id = Guid.CreateVersion7(), Version = 1 });

    public async IAsyncEnumerable<IdentityOidcToken> ListAsync(int? count, int? offset, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<IdentityOidcTokenRow>(
            sqlResolver.ListTokens(),
            IdentitySqlParameters.Create(("Offset", offset ?? 0), ("PageSize", count ?? int.MaxValue)),
            cancellationToken).ConfigureAwait(false);
        foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); yield return IdentityOidcRecordMapper.ToToken(row); }
    }

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(Func<IQueryable<IdentityOidcToken>, TState, IQueryable<TResult>> query, TState state, CancellationToken cancellationToken) =>
        throw IdentityOidcStoreSupport.LinqNotSupported();

    public async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
            IdentityOidcSql.PruneTokens,
            IdentitySqlParameters.Create(
                ("Threshold", threshold),
                ("ValidStatus", OpenIddictConstants.Statuses.Valid)),
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<long> RevokeAsync(string? subject, string? client, string? status, string? type, CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
            IdentityOidcSql.RevokeTokensByFilter,
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
            IdentityOidcSql.RevokeTokensByApplicationId,
            IdentitySqlParameters.Create(
                ("ApplicationId", IdentityOidcStoreSupport.ParseRequiredId(identifier)),
                ("RevokedStatus", OpenIddictConstants.Statuses.Revoked),
                ("UpdatedAtUtc", clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<long> RevokeByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
            IdentityOidcSql.RevokeTokensByAuthorizationId,
            IdentitySqlParameters.Create(
                ("AuthorizationId", IdentityOidcStoreSupport.ParseRequiredId(identifier)),
                ("RevokedStatus", OpenIddictConstants.Statuses.Revoked),
                ("UpdatedAtUtc", clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
            IdentityOidcSql.RevokeTokensBySubject,
            IdentitySqlParameters.Create(
                ("Subject", subject),
                ("RevokedStatus", OpenIddictConstants.Statuses.Revoked),
                ("UpdatedAtUtc", clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);

    public ValueTask SetApplicationIdAsync(IdentityOidcToken token, string? identifier, CancellationToken cancellationToken)
    {
        token.ApplicationId = IdentityOidcStoreSupport.ParseOptionalId(identifier);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetAuthorizationIdAsync(IdentityOidcToken token, string? identifier, CancellationToken cancellationToken)
    {
        token.AuthorizationId = IdentityOidcStoreSupport.ParseOptionalId(identifier);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetSessionIdAsync(IdentityOidcToken token, string? identifier, CancellationToken cancellationToken)
    {
        token.SessionId = identifier;
        token.PropertiesJson = IdentityOidcStoreSupport.WriteSessionId(token.PropertiesJson, identifier);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetCreationDateAsync(IdentityOidcToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        token.CreationDateUtc = date;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetExpirationDateAsync(IdentityOidcToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        token.ExpirationDateUtc = date;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPayloadAsync(IdentityOidcToken token, string? payload, CancellationToken cancellationToken)
    {
        token.Payload = payload;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPropertiesAsync(IdentityOidcToken token, ImmutableDictionary<string, JsonElement> properties, CancellationToken cancellationToken)
    {
        token.PropertiesJson = IdentityOidcStoreSupport.SerializeProperties(properties);
        token.SessionId = IdentityOidcStoreSupport.ReadSessionId(token.PropertiesJson);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetRedemptionDateAsync(IdentityOidcToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        token.RedemptionDateUtc = date;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetReferenceIdAsync(IdentityOidcToken token, string? identifier, CancellationToken cancellationToken)
    {
        token.ReferenceId = identifier;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetStatusAsync(IdentityOidcToken token, string? status, CancellationToken cancellationToken)
    {
        token.Status = status;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetSubjectAsync(IdentityOidcToken token, string? subject, CancellationToken cancellationToken)
    {
        token.Subject = subject;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetTypeAsync(IdentityOidcToken token, string? type, CancellationToken cancellationToken)
    {
        token.Type = type;
        return ValueTask.CompletedTask;
    }

    public async ValueTask UpdateAsync(IdentityOidcToken token, CancellationToken cancellationToken)
    {
        token.UpdatedAtUtc = clock.UtcNow;
        token.PropertiesJson = IdentityOidcStoreSupport.WriteSessionId(token.PropertiesJson, token.SessionId);

        if (IdentityOidcStoreSupport.ShouldUseAtomicTokenRedemption(token))
        {
            // 授权码与 refresh token 兑换必须原子成功，否则视为并发冲突（V03/V09/V15）。
            var affected = await commandExecutor.ExecuteAsync(
                sqlResolver.RedeemAuthorizationCodeToken(),
                IdentitySqlParameters.Create(
                    ("Id", token.Id),
                    ("Version", token.Version),
                    ("RedemptionDateUtc", token.RedemptionDateUtc),
                    ("RedeemedStatus", OpenIddictConstants.Statuses.Redeemed),
                    ("ValidStatus", OpenIddictConstants.Statuses.Valid),
                    ("UpdatedAtUtc", token.UpdatedAtUtc)),
                cancellationToken).ConfigureAwait(false);
            IdentityOidcStoreSupport.EnsureConcurrency(affected);
            token.Status = OpenIddictConstants.Statuses.Redeemed;
            token.Version++;
            return;
        }

        var rows = await commandExecutor.ExecuteAsync(
            IdentityOidcSql.UpdateToken,
            MapToken(token),
            cancellationToken).ConfigureAwait(false);
        IdentityOidcStoreSupport.EnsureConcurrency(rows);
        token.Version++;
    }

    private static Dictionary<string, object?> MapToken(IdentityOidcToken token) =>
        IdentitySqlParameters.Create(
            ("Id", token.Id),
            ("ApplicationId", token.ApplicationId),
            ("AuthorizationId", token.AuthorizationId),
            ("CreationDateUtc", token.CreationDateUtc),
            ("ExpirationDateUtc", token.ExpirationDateUtc),
            ("Payload", token.Payload),
            ("PropertiesJson", token.PropertiesJson),
            ("RedemptionDateUtc", token.RedemptionDateUtc),
            ("ReferenceId", token.ReferenceId),
            ("Status", token.Status),
            ("Subject", token.Subject),
            ("Type", token.Type),
            ("Version", token.Version),
            ("CreatedAtUtc", token.CreatedAtUtc),
            ("UpdatedAtUtc", token.UpdatedAtUtc));
}