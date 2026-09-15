using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>OIDC 中心会话与应用会话权威读写；撤销中心会话级联应用会话。</summary>
internal sealed class IdentityOidcSessionService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock)
{
    public async Task<IdentityOidcCenterSession> CreateCenterSessionAsync(
        Guid userId,
        string securityStamp,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);
        var now = clock.UtcNow;
        var session = new IdentityOidcCenterSession
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            SecurityStamp = securityStamp,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc,
            RevokedAtUtc = null,
            Version = 1,
            UpdatedAtUtc = now,
        };
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentityOidcSessionSql.InsertCenterSession,
                session,
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"OIDC center session insert affected {affectedRows} rows instead of one.");
        }

        return session;
    }

    public async Task<IdentityOidcApplicationSession> CreateApplicationSessionAsync(
        Guid centerSessionId,
        Guid oidcApplicationId,
        string clientId,
        Guid userId,
        string actorScope,
        string effectiveScope,
        Guid? activeTenantId,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorScope);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectiveScope);
        var now = clock.UtcNow;
        var session = new IdentityOidcApplicationSession
        {
            Id = Guid.CreateVersion7(),
            CenterSessionId = centerSessionId,
            OidcApplicationId = oidcApplicationId,
            ClientId = clientId,
            UserId = userId,
            ActorScope = actorScope,
            EffectiveScope = effectiveScope,
            ActiveTenantId = activeTenantId,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc,
            RevokedAtUtc = null,
            Version = 1,
            UpdatedAtUtc = now,
        };
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentityOidcSessionSql.InsertApplicationSession,
                session,
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"OIDC application session insert affected {affectedRows} rows instead of one.");
        }

        return session;
    }

    public async Task<bool> RevokeCenterSessionAsync(
        Guid centerSessionId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcCenterSessionRow>(
                IdentityOidcSessionSql.FindCenterSessionById,
                IdentitySqlParameters.Create(("Id", centerSessionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || record.RevokedAtUtc.HasValue)
        {
            return false;
        }

        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                IdentityOidcSessionSql.RevokeApplicationSessionsByCenterSession,
                IdentitySqlParameters.Create(
                    ("CenterSessionId", centerSessionId),
                    ("RevokedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentityOidcSessionSql.RevokeCenterSession,
                IdentitySqlParameters.Create(
                    ("Id", centerSessionId),
                    ("RevokedAtUtc", now),
                    ("UpdatedAtUtc", now),
                    ("Version", record.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affectedRows > 0;
    }

    public async Task<bool> RevokeApplicationSessionAsync(
        Guid applicationSessionId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationSessionRow>(
                IdentityOidcSessionSql.FindApplicationSessionById,
                IdentitySqlParameters.Create(("Id", applicationSessionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || record.RevokedAtUtc.HasValue)
        {
            return false;
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentityOidcSessionSql.RevokeApplicationSession,
                IdentitySqlParameters.Create(
                    ("Id", applicationSessionId),
                    ("RevokedAtUtc", now),
                    ("UpdatedAtUtc", now),
                    ("Version", record.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affectedRows > 0;
    }

    public async Task<int> RevokeAllApplicationSessionsByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        return await commandExecutor.ExecuteAsync(
                IdentityOidcSessionSql.RevokeAllApplicationSessionsByUser,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("RevokedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> RevokeAllCenterSessionsByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        await RevokeAllApplicationSessionsByUserAsync(userId, cancellationToken)
            .ConfigureAwait(false);
        return await commandExecutor.ExecuteAsync(
                IdentityOidcSessionSql.RevokeAllCenterSessionsByUser,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("RevokedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IdentityOidcCenterSession?> FindActiveCenterSessionAsync(
        Guid centerSessionId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcCenterSessionRow>(
                IdentityOidcSessionSql.FindActiveCenterSessionById,
                IdentitySqlParameters.Create(
                    ("Id", centerSessionId),
                    ("NowUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : IdentityOidcSessionRecordMapper.ToCenterSession(row);
    }

    public async Task<IdentityOidcApplicationSession?> FindActiveApplicationSessionAsync(
        Guid applicationSessionId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationSessionRow>(
                IdentityOidcSessionSql.FindActiveApplicationSessionById,
                IdentitySqlParameters.Create(
                    ("Id", applicationSessionId),
                    ("NowUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : IdentityOidcSessionRecordMapper.ToApplicationSession(row);
    }
}