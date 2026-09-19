using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Features.ManageTenantMembers;

internal sealed class TenantMembershipManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    TenantMembershipQueryService queries,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<CreateTenantInvitationResult>> InviteAsync(
        Guid invitedByUserId,
        CreateTenantInvitationRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => InviteCoreAsync(invitedByUserId, request, token),
            cancellationToken);

    public Task<Result<TenantInvitationResponse>> RevokeInvitationAsync(
        Guid invitationId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RevokeInvitationCoreAsync(invitationId, token),
            cancellationToken);

    public Task<Result<TenantMemberResponse>> UpdateMemberAsync(
        Guid memberId,
        UpdateTenantMemberRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateMemberCoreAsync(memberId, request, token),
            cancellationToken);

    public Task<Result<TenantMemberResponse>> RemoveMemberAsync(
        Guid memberId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RemoveMemberCoreAsync(memberId, version, token),
            cancellationToken);

    internal static Result<(string Email, string Role)> ValidateInvitationRequest(
        CreateTenantInvitationRequest request)
    {
        var email = request.TargetEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        if (email.Length is < 3 or > 320 || !email.Contains('@', StringComparison.Ordinal))
        {
            return Result<(string, string)>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Target email is invalid.",
                ErrorType.Validation));
        }

        var role = request.MemberRole?.Trim() ?? string.Empty;
        if (role is not (TenantMemberRoles.Admin or TenantMemberRoles.Member))
        {
            return Result<(string, string)>.Failure(new Error(
                IdentityErrorCodes.TenantMemberRoleInvalid,
                "Member role is invalid.",
                ErrorType.Validation));
        }

        var hours = Math.Clamp(request.ExpiresInHours, 1, 168);
        return Result<(string, string)>.Success((email, role));
    }

    private async Task<Result<CreateTenantInvitationResult>> InviteCoreAsync(
        Guid invitedByUserId,
        CreateTenantInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateInvitationRequest(request);
        if (!validation.IsSuccess)
        {
            return Result<CreateTenantInvitationResult>.Failure(validation.Error!);
        }

        var (email, role) = validation.Value;
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var tokenHash = TokenHash.Compute(token);
        var now = clock.UtcNow;
        var invitationId = idGenerator.NewId();
        var expiresAt = now.AddHours(Math.Clamp(request.ExpiresInHours, 1, 168));
        await commandExecutor.ExecuteAsync(
                TenantMembershipSql.InsertInvitation,
                Identity.Persistence.IdentitySqlParameters.Create(
                    ("Id", invitationId),
                    ("TargetEmail", email),
                    ("TargetUserId", null),
                    ("InvitedByUserId", invitedByUserId),
                    ("MemberRole", role),
                    ("TokenHash", tokenHash),
                    ("Status", TenantInvitationStatuses.Pending),
                    ("ExpiresAtUtc", expiresAt),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", now),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);
        var invitation = new TenantInvitationResponse(
            invitationId,
            Guid.Empty,
            email,
            null,
            invitedByUserId,
            role,
            TenantInvitationStatuses.Pending,
            expiresAt,
            now,
            now,
            1);
        return Result<CreateTenantInvitationResult>.Success(
            new CreateTenantInvitationResult(invitation, token));
    }

    private async Task<Result<TenantInvitationResponse>> RevokeInvitationCoreAsync(
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantInvitationRecord>(
                TenantMembershipSql.FindInvitationById,
                Identity.Persistence.IdentitySqlParameters.Create(("InvitationId", invitationId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return Result<TenantInvitationResponse>.Failure(new Error(
                IdentityErrorCodes.TenantInvitationNotFound,
                "The tenant invitation was not found.",
                ErrorType.NotFound));
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                TenantMembershipSql.UpdateInvitationStatus,
                Identity.Persistence.IdentitySqlParameters.Create(
                    ("InvitationId", invitationId),
                    ("Status", TenantInvitationStatuses.Revoked),
                    ("UpdatedAtUtc", now),
                    ("Version", existing.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return Result<TenantInvitationResponse>.Failure(new Error(
                IdentityErrorCodes.TenantInvitationNotFound,
                "The tenant invitation was not found.",
                ErrorType.NotFound));
        }

        return Result<TenantInvitationResponse>.Success(new TenantInvitationResponse(
            existing.Id,
            existing.TenantId,
            existing.TargetEmail,
            existing.TargetUserId,
            existing.InvitedByUserId,
            existing.MemberRole,
            TenantInvitationStatuses.Revoked,
            existing.ExpiresAtUtc,
            existing.CreatedAtUtc,
            now,
            existing.Version + 1));
    }

    private async Task<Result<TenantMemberResponse>> UpdateMemberCoreAsync(
        Guid memberId,
        UpdateTenantMemberRequest request,
        CancellationToken cancellationToken)
    {
        var role = request.MemberRole?.Trim() ?? string.Empty;
        if (role is not (TenantMemberRoles.Admin or TenantMemberRoles.Member))
        {
            return Result<TenantMemberResponse>.Failure(new Error(
                IdentityErrorCodes.TenantMemberRoleInvalid,
                "Member role is invalid.",
                ErrorType.Validation));
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                TenantMembershipSql.UpdateMember,
                Identity.Persistence.IdentitySqlParameters.Create(
                    ("MemberId", memberId),
                    ("MemberRole", role),
                    ("Status", TenantMemberStatuses.Active),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return Result<TenantMemberResponse>.Failure(new Error(
                IdentityErrorCodes.TenantMemberVersionConflict,
                "The tenant member record was updated concurrently.",
                ErrorType.Conflict));
        }

        return await queries.GetMemberByIdAsync(memberId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<TenantMemberResponse>> RemoveMemberCoreAsync(
        Guid memberId,
        int version,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantMemberRecord>(
                TenantMembershipSql.FindMemberById,
                Identity.Persistence.IdentitySqlParameters.Create(("MemberId", memberId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return Result<TenantMemberResponse>.Failure(new Error(
                IdentityErrorCodes.TenantMemberNotFound,
                "The tenant member was not found.",
                ErrorType.NotFound));
        }

        if (existing.MemberRole == TenantMemberRoles.Owner)
        {
            return Result<TenantMemberResponse>.Failure(new Error(
                IdentityErrorCodes.TenantOwnerProtected,
                "The tenant owner cannot be removed.",
                ErrorType.BusinessRule));
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                TenantMembershipSql.UpdateMember,
                Identity.Persistence.IdentitySqlParameters.Create(
                    ("MemberId", memberId),
                    ("MemberRole", existing.MemberRole),
                    ("Status", TenantMemberStatuses.Removed),
                    ("UpdatedAtUtc", now),
                    ("Version", version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return Result<TenantMemberResponse>.Failure(new Error(
                IdentityErrorCodes.TenantMemberVersionConflict,
                "The tenant member record was updated concurrently.",
                ErrorType.Conflict));
        }

        return await queries.GetMemberByIdAsync(memberId, cancellationToken)
            .ConfigureAwait(false);
    }
}
