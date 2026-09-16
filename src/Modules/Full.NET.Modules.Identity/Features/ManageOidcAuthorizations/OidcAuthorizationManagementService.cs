using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.Modules.Identity.Features.ManageOidcAuthorizations;

/// <summary>OIDC 授权授予撤销；幂等撤销授权及其关联令牌。</summary>
internal sealed class OidcAuthorizationManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    OidcAuthorizationQueryService queries,
    IdentityOidcSessionService sessionService,
    IdentitySessionRealtimeDelivery sessionRealtimeDelivery,
    OidcManagementAuditWriter auditWriter,
    IClock clock)
{
    public Task<Result<OidcAuthorizationResponse>> RevokeAsync(
        Guid authorizationId,
        OidcManagementActorContext actor,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RevokeCoreAsync(authorizationId, actor, token),
            cancellationToken);

    private async Task<Result<OidcAuthorizationResponse>> RevokeCoreAsync(
        Guid authorizationId,
        OidcManagementActorContext actor,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcAuthorizationDetailRow>(
                IdentityOidcSql.FindAuthorizationDetailById,
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

        if (Guid.TryParse(existing.Subject, out var userId)
            && !string.IsNullOrWhiteSpace(existing.ClientId))
        {
            var revokedSessionIds = await sessionService
                .RevokeActiveApplicationSessionsByUserAndClientAsync(
                    userId,
                    existing.ClientId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (revokedSessionIds.Count > 0)
            {
                await sessionRealtimeDelivery.PublishSessionsRevokedAsync(
                        userId,
                        revokedSessionIds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var revoked = await queries.GetByIdAsync(authorizationId, cancellationToken).ConfigureAwait(false);
        if (revoked.IsSuccess)
        {
            await auditWriter.WriteAsync(
                    actor.ActorUserId,
                    OidcManagementAuditWriter.AuthorizationRevokedEventType,
                    $"authorization:{authorizationId:D}",
                    actor.IpAddress,
                    actor.UserAgent,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return revoked;
    }
}