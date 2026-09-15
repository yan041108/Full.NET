using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.Modules.Identity.Features.ManageOidcAuthorizations;

/// <summary>OIDC 授权授予撤销；幂等撤销授权及其关联令牌。</summary>
internal sealed class OidcAuthorizationManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    OidcAuthorizationQueryService queries,
    IClock clock)
{
    public Task<Result<OidcAuthorizationResponse>> RevokeAsync(
        Guid authorizationId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RevokeCoreAsync(authorizationId, token),
            cancellationToken);

    private async Task<Result<OidcAuthorizationResponse>> RevokeCoreAsync(
        Guid authorizationId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcAuthorizationRow>(
                IdentityOidcSql.FindAuthorizationById,
                IdentitySqlParameters.Create(("Id", authorizationId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return Result<OidcAuthorizationResponse>.Failure(new Error(
                IdentityErrorCodes.OidcAuthorizationNotFound,
                "The OIDC authorization was not found.",
                ErrorType.NotFound));
        }

        var now = clock.UtcNow;
        // 已撤销时仍返回成功，满足管理端幂等撤销语义。
        await commandExecutor.ExecuteAsync(
                IdentityOidcSql.RevokeAuthorizationById,
                IdentitySqlParameters.Create(
                    ("Id", authorizationId),
                    ("RevokedStatus", Statuses.Revoked),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
                IdentityOidcSql.RevokeTokensByAuthorizationId,
                IdentitySqlParameters.Create(
                    ("AuthorizationId", authorizationId),
                    ("RevokedStatus", Statuses.Revoked),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        return await queries.GetByIdAsync(authorizationId, cancellationToken).ConfigureAwait(false);
    }
}