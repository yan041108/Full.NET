using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Features.AcceptTenantInvitation;

internal sealed class AcceptTenantInvitationService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenantContextWriter currentTenant,
    IActiveTenantContextResolver tenantResolver,
    ITenantMemberSeatQuotaPort seatQuotaPort,
    IClock clock,
    IIdGenerator idGenerator)
{
    // 成员已写入后仍可能遇到邀请版本竞争或配额确认失败，失败结果必须回滚本地写入。
    public Task<Result<AcceptTenantInvitationResponse>> AcceptAsync(
        Guid userId,
        AcceptTenantInvitationRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => AcceptCoreAsync(userId, request, token),
            cancellationToken);

    private async Task<Result<AcceptTenantInvitationResponse>> AcceptCoreAsync(
        Guid userId,
        AcceptTenantInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var token = request.InvitationToken?.Trim() ?? string.Empty;
        if (token.Length < 16)
        {
            return InvalidInvitation();
        }

        var invitation = await queryExecutor.QuerySingleOrDefaultAsync<TenantInvitationRecord>(
                TenantMembershipSql.FindInvitationByTokenHash,
                IdentitySqlParameters.Create(("TokenHash", TokenHash.Compute(token))),
                cancellationToken)
            .ConfigureAwait(false);
        if (invitation is null
            || invitation.Status != TenantInvitationStatuses.Pending
            || invitation.ExpiresAtUtc <= clock.UtcNow)
        {
            return InvalidInvitation();
        }

        var tenant = await tenantResolver.ResolveActiveByIdAsync(invitation.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            return InvalidInvitation();
        }

        if (!await MatchesInviteeAsync(userId, invitation, cancellationToken).ConfigureAwait(false))
        {
            return InvalidInvitation();
        }

        var operationId = invitation.Id.ToString("D");
        var reserved = false;
        var memberResult = await IdentityTenantInvitationScope.RunAsync(
            currentTenant,
            tenant,
            async () =>
            {
                var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantMemberRecord>(
                        TenantMembershipSql.FindMemberByTenantAndUser,
                        IdentitySqlParameters.Create(("UserId", userId)),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (existing is { Status: TenantMemberStatuses.Active })
                {
                    return Result<AcceptTenantInvitationResponse>.Failure(new Error(
                        IdentityErrorCodes.TenantMemberAlreadyActive,
                        "The user is already an active tenant member.",
                        ErrorType.Conflict));
                }

                var reserveResult = await seatQuotaPort.TryReserveAsync(
                        invitation.TenantId,
                        operationId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!reserveResult.IsSuccess)
                {
                    return Result<AcceptTenantInvitationResponse>.Failure(reserveResult.Error!);
                }

                reserved = true;
                var now = clock.UtcNow;
                var memberId = existing?.Id ?? idGenerator.NewId();
                if (existing is null)
                {
                    await commandExecutor.ExecuteAsync(
                            TenantMembershipSql.InsertMember,
                            IdentitySqlParameters.Create(
                                ("Id", memberId),
                                ("UserId", userId),
                                ("MemberRole", invitation.MemberRole),
                                ("Status", TenantMemberStatuses.Active),
                                ("CreatedAtUtc", now),
                                ("UpdatedAtUtc", now),
                                ("Version", 1)),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await commandExecutor.ExecuteAsync(
                            TenantMembershipSql.UpdateMember,
                            IdentitySqlParameters.Create(
                                ("MemberId", memberId),
                                ("MemberRole", invitation.MemberRole),
                                ("Status", TenantMemberStatuses.Active),
                                ("UpdatedAtUtc", now),
                                ("Version", existing.Version)),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                var affected = await commandExecutor.ExecuteAsync(
                        TenantMembershipSql.UpdateInvitationStatus,
                        IdentitySqlParameters.Create(
                            ("InvitationId", invitation.Id),
                            ("Status", TenantInvitationStatuses.Accepted),
                            ("UpdatedAtUtc", now),
                            ("Version", invitation.Version)),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (affected != 1)
                {
                    return Result<AcceptTenantInvitationResponse>.Failure(new Error(
                        IdentityErrorCodes.TenantInvitationNotFound,
                        "The tenant invitation was not found or was updated concurrently.",
                        ErrorType.Conflict));
                }

                return Result<AcceptTenantInvitationResponse>.Success(
                    new AcceptTenantInvitationResponse(
                        memberId,
                        invitation.TenantId,
                        userId,
                        invitation.MemberRole,
                        TenantMemberStatuses.Active));
            }).ConfigureAwait(false);
        if (!memberResult.IsSuccess)
        {
            if (reserved)
            {
                await seatQuotaPort.ReleaseAsync(invitation.TenantId, operationId, cancellationToken)
                    .ConfigureAwait(false);
            }

            return memberResult;
        }

        var confirmResult = await seatQuotaPort.ConfirmAsync(
                invitation.TenantId,
                operationId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!confirmResult.IsSuccess)
        {
            return Result<AcceptTenantInvitationResponse>.Failure(confirmResult.Error!);
        }

        return memberResult;
    }

    private async Task<bool> MatchesInviteeAsync(
        Guid userId,
        TenantInvitationRecord invitation,
        CancellationToken cancellationToken)
    {
        if (invitation.TargetUserId is Guid targetUserId)
        {
            return targetUserId == userId;
        }

        var profileEmail = await queryExecutor.QuerySingleOrDefaultAsync<string>(
                AccountLifecycleSql.FindUserProfileEmailByUserId,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (profileEmail is null)
        {
            return false;
        }

        var normalizedProfileEmail = AccountChallengeService.NormalizeEmail(profileEmail);
        var normalizedTargetEmail = AccountChallengeService.NormalizeEmail(invitation.TargetEmail);
        return normalizedProfileEmail is not null
            && normalizedTargetEmail is not null
            && string.Equals(normalizedProfileEmail, normalizedTargetEmail, StringComparison.Ordinal);
    }

    private static Result<AcceptTenantInvitationResponse> InvalidInvitation() =>
        Result<AcceptTenantInvitationResponse>.Failure(new Error(
            IdentityErrorCodes.TenantInvitationInvalid,
            "The tenant invitation is invalid or expired.",
            ErrorType.BusinessRule));
}
