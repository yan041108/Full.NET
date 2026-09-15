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
}