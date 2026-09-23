using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features;
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
    // 席位预留/确认在 Identity 本地事务外执行；成员与邀请状态仅在本地事务内写入，确认失败时补偿回滚。
    public Task<Result<AcceptTenantInvitationResponse>> AcceptAsync(
        Guid userId,
        AcceptTenantInvitationRequest request,
        CancellationToken cancellationToken = default) =>
        AcceptByTokenCoreAsync(userId, request, cancellationToken);

    public Task<Result<AcceptTenantInvitationResponse>> AcceptByIdAsync(
        Guid userId,
        Guid invitationId,
        CancellationToken cancellationToken = default) =>
        AcceptByIdCoreAsync(userId, invitationId, cancellationToken);

    private async Task<Result<AcceptTenantInvitationResponse>> AcceptByTokenCoreAsync(
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
        return await AcceptPreparedInvitationAsync(userId, invitation, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<AcceptTenantInvitationResponse>> AcceptByIdCoreAsync(
        Guid userId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var invitation = await queryExecutor.QuerySingleOrDefaultAsync<TenantInvitationRecord>(
                TenantMembershipSql.FindInvitationByIdGlobal,
                IdentitySqlParameters.Create(("InvitationId", invitationId)),
                cancellationToken)
            .ConfigureAwait(false);
        return await AcceptPreparedInvitationAsync(userId, invitation, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<AcceptTenantInvitationResponse>> AcceptPreparedInvitationAsync(
        Guid userId,
        TenantInvitationRecord? invitation,
        CancellationToken cancellationToken)
    {
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
        var existing = await IdentityTenantInvitationScope.RunAsync(
                currentTenant,
                tenant,
                () => queryExecutor.QuerySingleOrDefaultAsync<TenantMemberRecord>(
                    TenantMembershipSql.FindMemberByTenantAndUser,
                    IdentitySqlParameters.Create(("UserId", userId)),
                    cancellationToken))
            .ConfigureAwait(false);
        if (existing is { Status: TenantMemberStatuses.Active })
        {
            return Result<AcceptTenantInvitationResponse>.Failure(new Error(
                IdentityErrorCodes.TenantMemberAlreadyActive,
                "The user is already an active tenant member.",
                ErrorType.Conflict));
        }

        var reserveResult = await IdentityHostExecutionScope.RunAsync(
                currentTenant,
                () => seatQuotaPort.TryReserveAsync(
                    invitation.TenantId,
                    operationId,
                    cancellationToken))
            .ConfigureAwait(false);
        if (!reserveResult.IsSuccess)
        {
            return Result<AcceptTenantInvitationResponse>.Failure(reserveResult.Error!);
        }

        var memberResult = await IdentityTenantInvitationScope.RunAsync(
            currentTenant,
            tenant,
            () => transaction.ExecuteResultAsync(
                token => PersistAcceptedInvitationAsync(
                    userId,
                    invitation,
                    existing,
                    token),
                cancellationToken)).ConfigureAwait(false);
        if (!memberResult.IsSuccess)
        {
            await IdentityHostExecutionScope.RunAsync(
                    currentTenant,
                    () => seatQuotaPort.ReleaseAsync(
                        invitation.TenantId,
                        operationId,
                        cancellationToken))
                .ConfigureAwait(false);
            return memberResult;
        }

        var confirmResult = await IdentityHostExecutionScope.RunAsync(
                currentTenant,
                () => seatQuotaPort.ConfirmAsync(
                    invitation.TenantId,
                    operationId,
                    cancellationToken))
            .ConfigureAwait(false);
        if (!confirmResult.IsSuccess)
        {
            await CompensateFailedConfirmationAsync(
                    userId,
                    invitation,
                    memberResult.Value!.MemberId,
                    existing,
                    cancellationToken)
                .ConfigureAwait(false);
            await IdentityHostExecutionScope.RunAsync(
                    currentTenant,
                    () => seatQuotaPort.ReleaseAsync(
                        invitation.TenantId,
                        operationId,
                        cancellationToken))
                .ConfigureAwait(false);
            return Result<AcceptTenantInvitationResponse>.Failure(confirmResult.Error!);
        }

        return memberResult;
    }

    private async Task<Result<AcceptTenantInvitationResponse>> PersistAcceptedInvitationAsync(
        Guid userId,
        TenantInvitationRecord invitation,
        TenantMemberRecord? existing,
        CancellationToken cancellationToken)
    {
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
    }

    private async Task CompensateFailedConfirmationAsync(
        Guid userId,
        TenantInvitationRecord invitation,
        Guid memberId,
        TenantMemberRecord? existingBeforeAccept,
        CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.ResolveActiveByIdAsync(invitation.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            return;
        }

        await IdentityTenantInvitationScope.RunAsync(
            currentTenant,
            tenant,
            () => transaction.ExecuteAsync(
                async token =>
                {
                    var now = clock.UtcNow;
                    if (existingBeforeAccept is null)
                    {
                        await commandExecutor.ExecuteAsync(
                                TenantMembershipSql.UpdateMember,
                                IdentitySqlParameters.Create(
                                    ("MemberId", memberId),
                                    ("MemberRole", invitation.MemberRole),
                                    ("Status", TenantMemberStatuses.Removed),
                                    ("UpdatedAtUtc", now),
                                    ("Version", 1)),
                                token)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await commandExecutor.ExecuteAsync(
                                TenantMembershipSql.UpdateMember,
                                IdentitySqlParameters.Create(
                                    ("MemberId", memberId),
                                    ("MemberRole", existingBeforeAccept.MemberRole),
                                    ("Status", existingBeforeAccept.Status),
                                    ("UpdatedAtUtc", now),
                                    ("Version", existingBeforeAccept.Version + 1)),
                                token)
                            .ConfigureAwait(false);
                    }

                    await commandExecutor.ExecuteAsync(
                            TenantMembershipSql.UpdateInvitationStatus,
                            IdentitySqlParameters.Create(
                                ("InvitationId", invitation.Id),
                                ("Status", TenantInvitationStatuses.Pending),
                                ("UpdatedAtUtc", now),
                                ("Version", invitation.Version + 1)),
                            token)
                        .ConfigureAwait(false);
                    return true;
                },
                cancellationToken)).ConfigureAwait(false);
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
