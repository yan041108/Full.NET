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
using Full.NET.Modules.Identity.Features.TenantSeatOperations;

/// <summary>Identity 模块应用服务；编排事务、权限与持久化。</summary>
namespace Full.NET.Modules.Identity.Features.AcceptTenantInvitation;

internal sealed class AcceptTenantInvitationService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenantContextWriter currentTenant,
    IActiveTenantContextResolver tenantResolver,
    TenantSeatOperationProcessor seatOperations,
    IClock clock,
    IIdGenerator idGenerator)
{
    // 先原子提交 Pending 成员及耐久工作项，再在事务外处理席位；崩溃后由 Worker 接续。
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
            || invitation.Status is not (TenantInvitationStatuses.Pending or TenantInvitationStatuses.Accepted)
            || invitation.Status == TenantInvitationStatuses.Pending && invitation.ExpiresAtUtc <= clock.UtcNow)
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

        if (invitation.Status == TenantInvitationStatuses.Accepted)
        {
            var previous = await IdentityHostExecutionScope.RunAsync(currentTenant,
                () => queryExecutor.QuerySingleOrDefaultAsync<TenantSeatOperation>(TenantSeatOperationSql.FindInvitation,
                    IdentitySqlParameters.Create(("TenantId", invitation.TenantId), ("InvitationId", invitation.Id),
                        ("UserId", userId), ("InvitationVersion", invitation.Version)), cancellationToken)).ConfigureAwait(false);
            return previous is null ? InvalidInvitation() : await CompleteAsync(previous, invitation.MemberRole, cancellationToken).ConfigureAwait(false);
        }

        var existing = await IdentityTenantInvitationScope.RunAsync(
                currentTenant,
                tenant,
                () => queryExecutor.QuerySingleOrDefaultAsync<TenantMemberRecord>(
                    TenantMembershipSql.FindMemberByTenantAndUser,
                    IdentitySqlParameters.Create(("UserId", userId)),
                    cancellationToken))
            .ConfigureAwait(false);
        if (existing is { Status: TenantMemberStatuses.Active or TenantMemberStatuses.Pending }
            || existing is { MemberRole: TenantMemberRoles.Owner })
        {
            return Result<AcceptTenantInvitationResponse>.Failure(new Error(
                IdentityErrorCodes.TenantMemberAlreadyActive,
                "The user is already an active tenant member.",
                ErrorType.Conflict));
        }

        var memberResult = await IdentityTenantInvitationScope.RunAsync(currentTenant, tenant,
            () => transaction.ExecuteResultAsync(token => PersistAcceptedInvitationAsync(userId, invitation, existing, token), cancellationToken)).ConfigureAwait(false);
        if (!memberResult.IsSuccess) return memberResult;
        var operation = await seatOperations.FindRequestAsync(invitation.TenantId, InvitationRequestKey(invitation), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Committed invitation has no seat intent.");
        return await CompleteAsync(operation, invitation.MemberRole, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<AcceptTenantInvitationResponse>> CompleteAsync(TenantSeatOperation operation, string role, CancellationToken token)
    {
        var result = await seatOperations.ProcessAsync(operation.Id, token).ConfigureAwait(false);
        if (!result.IsSuccess) return Result<AcceptTenantInvitationResponse>.Failure(result.Error!);
        return Result<AcceptTenantInvitationResponse>.Success(new(operation.MemberId, operation.TenantId, operation.UserId,
            role, result.Value ? TenantMemberStatuses.Active : TenantMemberStatuses.Pending));
    }

    private static string InvitationRequestKey(TenantInvitationRecord invitation) =>
        "invitation:" + invitation.Id.ToString("D") + ":" + invitation.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task<Result<AcceptTenantInvitationResponse>> PersistAcceptedInvitationAsync(
        Guid userId,
        TenantInvitationRecord invitation,
        TenantMemberRecord? existing,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var memberId = existing?.Id ?? idGenerator.NewId();
        int memberAffected;
        if (existing is null)
        {
            memberAffected = await commandExecutor.ExecuteAsync(
                    TenantMembershipSql.InsertMember,
                    IdentitySqlParameters.Create(
                        ("Id", memberId),
                        ("UserId", userId),
                        ("MemberRole", invitation.MemberRole),
                        ("Status", TenantMemberStatuses.Pending),
                        ("CreatedAtUtc", now),
                        ("UpdatedAtUtc", now),
                        ("Version", 1)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            memberAffected = await commandExecutor.ExecuteAsync(
                    TenantMembershipSql.UpdateMember,
                    IdentitySqlParameters.Create(
                        ("MemberId", memberId),
                        ("MemberRole", invitation.MemberRole),
                        ("Status", TenantMemberStatuses.Pending),
                        ("UpdatedAtUtc", now),
                        ("Version", existing.Version)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (memberAffected != 1)
            return Result<AcceptTenantInvitationResponse>.Failure(new Error(IdentityErrorCodes.TenantMemberVersionConflict,
                "The tenant member was updated concurrently.", ErrorType.Conflict));

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

        await seatOperations.EnlistAsync(new TenantSeatOperation(idGenerator.NewId(), InvitationRequestKey(invitation), invitation.TenantId,
            userId, memberId, invitation.Id, existing?.MemberRole, existing?.Status, (existing?.Version ?? 0) + 1,
            invitation.Version + 1, TenantSeatPhases.Requested, null, null, now, now, null, null, 1, 1), cancellationToken).ConfigureAwait(false);

        return Result<AcceptTenantInvitationResponse>.Success(
            new AcceptTenantInvitationResponse(
                memberId,
                invitation.TenantId,
                userId,
                invitation.MemberRole,
                TenantMemberStatuses.Pending));
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
