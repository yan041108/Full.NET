using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>按用户 Subject 撤销 OpenIddict 授权与令牌，阻断 refresh token 续期。</summary>
internal sealed record IdentityOidcGrantRevocationResult(
    int TokensRevoked,
    int AuthorizationsRevoked);

internal sealed class IdentityOidcGrantRevocationService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock)
{
    public async Task<IdentityOidcGrantRevocationResult> RevokeByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var subject = userId.ToString("D");
        var now = clock.UtcNow;
        var tokensRevoked = await commandExecutor.ExecuteAsync(
                IdentityOidcSql.RevokeTokensBySubject,
                IdentitySqlParameters.Create(
                    ("Subject", subject),
                    ("RevokedStatus", Statuses.Revoked),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        var authorizationsRevoked = await commandExecutor.ExecuteAsync(
                IdentityOidcSql.RevokeAuthorizationsBySubject,
                IdentitySqlParameters.Create(
                    ("Subject", subject),
                    ("RevokedStatus", Statuses.Revoked),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        return new IdentityOidcGrantRevocationResult(tokensRevoked, authorizationsRevoked);
    }

    /// <summary>撤销某 OIDC 客户端下全部授权与令牌，用于客户端禁用治理。</summary>
    public async Task<IdentityOidcGrantRevocationResult> RevokeByApplicationIdAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var tokensRevoked = await commandExecutor.ExecuteAsync(
                IdentityOidcSql.RevokeTokensByApplicationId,
                IdentitySqlParameters.Create(
                    ("ApplicationId", applicationId),
                    ("RevokedStatus", Statuses.Revoked),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        var authorizationsRevoked = await commandExecutor.ExecuteAsync(
                IdentityOidcSql.RevokeAuthorizationsByApplicationId,
                IdentitySqlParameters.Create(
                    ("ApplicationId", applicationId),
                    ("RevokedStatus", Statuses.Revoked),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        return new IdentityOidcGrantRevocationResult(tokensRevoked, authorizationsRevoked);
    }

    /// <summary>仅撤销指定用户在某 OIDC 客户端下的 refresh token，用于上下文切换轮换。</summary>
    public async Task<int> RevokeRefreshTokensByUserAndClientAsync(
        Guid userId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        var application = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationRow>(
                IdentityOidcSql.FindApplicationByClientId,
                IdentitySqlParameters.Create(("ClientId", clientId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (application is null)
        {
            return 0;
        }

        var subject = userId.ToString("D");
        var now = clock.UtcNow;
        return await commandExecutor.ExecuteAsync(
                IdentityOidcSql.RevokeTokensByFilter,
                IdentitySqlParameters.Create(
                    ("Subject", subject),
                    ("ApplicationId", application.Id),
                    ("RevokedStatus", Statuses.Revoked),
                    ("UpdatedAtUtc", now),
                    ("StatusFilter", null),
                    ("Type", TokenTypeIdentifiers.RefreshToken)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>仅撤销指定用户在某 OIDC 客户端下的授权与令牌，用于单应用下线。</summary>
    public async Task<IdentityOidcGrantRevocationResult> RevokeByUserAndClientAsync(
        Guid userId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        var application = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationRow>(
                IdentityOidcSql.FindApplicationByClientId,
                IdentitySqlParameters.Create(("ClientId", clientId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (application is null)
        {
            return new IdentityOidcGrantRevocationResult(0, 0);
        }

        return await RevokeByUserAndApplicationAsync(userId, application.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IdentityOidcGrantRevocationResult> RevokeByUserAndApplicationAsync(
        Guid userId,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        var subject = userId.ToString("D");
        var now = clock.UtcNow;
        var tokensRevoked = await commandExecutor.ExecuteAsync(
                IdentityOidcSql.RevokeTokensByFilter,
                IdentitySqlParameters.Create(
                    ("Subject", subject),
                    ("ApplicationId", applicationId),
                    ("RevokedStatus", Statuses.Revoked),
                    ("UpdatedAtUtc", now),
                    ("StatusFilter", null),
                    ("Type", null)),
                cancellationToken)
            .ConfigureAwait(false);
        var authorizationsRevoked = await commandExecutor.ExecuteAsync(
                IdentityOidcSql.RevokeAuthorizationsByFilter,
                IdentitySqlParameters.Create(
                    ("Subject", subject),
                    ("ApplicationId", applicationId),
                    ("RevokedStatus", Statuses.Revoked),
                    ("UpdatedAtUtc", now),
                    ("StatusFilter", null),
                    ("Type", null)),
                cancellationToken)
            .ConfigureAwait(false);
        return new IdentityOidcGrantRevocationResult(tokensRevoked, authorizationsRevoked);
    }
}
