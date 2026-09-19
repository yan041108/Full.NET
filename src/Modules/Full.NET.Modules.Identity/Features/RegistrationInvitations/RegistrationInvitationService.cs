using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.RegistrationInvitations;

internal sealed class RegistrationInvitationService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator)
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(7);

    public async Task<Result<(Guid InvitationId, string Token)>> CreateAsync(
        Guid tenantId,
        string email,
        Guid registrationWayId,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = AccountChallengeService.NormalizeEmail(email);
        if (normalizedEmail is null)
        {
            return Result<(Guid, string)>.Failure(InvalidInvitation());
        }

        var invitationId = idGenerator.NewId();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                RegistrationInvitationSql.Insert,
                IdentitySqlParameters.Create(
                    ("InvitationId", invitationId),
                    ("TenantId", tenantId),
                    ("NormalizedEmail", normalizedEmail),
                    ("CredentialHash", RegistrationInvitationCredentialHasher.Hash(invitationId, token)),
                    ("RegistrationWayId", registrationWayId),
                    ("Status", (byte)IdentityRegistrationInvitationStatus.Pending),
                    ("ExpiresAtUtc", now.Add(DefaultLifetime)),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<(Guid, string)>.Success((invitationId, token));
    }

    public async Task<Result<VerifyRegistrationInvitationResponse>> VerifyAsync(
        Guid invitationId,
        string invitationToken,
        CancellationToken cancellationToken = default)
    {
        var record = await FindActiveAsync(invitationId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return Result<VerifyRegistrationInvitationResponse>.Failure(InvalidInvitation());
        }

        var credentialHash = RegistrationInvitationCredentialHasher.Hash(invitationId, invitationToken);
        if (!string.Equals(record.CredentialHash, credentialHash, StringComparison.Ordinal))
        {
            return Result<VerifyRegistrationInvitationResponse>.Failure(InvalidInvitation());
        }

        return Result<VerifyRegistrationInvitationResponse>.Success(
            new VerifyRegistrationInvitationResponse(
                record.InvitationId,
                record.TenantId,
                record.NormalizedEmail,
                record.RegistrationWayId,
                record.ExpiresAtUtc));
    }

    public async Task<Result<bool>> ConsumeCredentialAsync(
        Guid invitationId,
        string invitationToken,
        CancellationToken cancellationToken = default)
    {
        var record = await FindActiveAsync(invitationId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return Result<bool>.Failure(InvalidInvitation());
        }

        var credentialHash = RegistrationInvitationCredentialHasher.Hash(invitationId, invitationToken);
        var affectedRows = await commandExecutor.ExecuteAsync(
                RegistrationInvitationSql.MarkConsumed,
                IdentitySqlParameters.Create(
                    ("InvitationId", invitationId),
                    ("ConsumedAtUtc", clock.UtcNow),
                    ("CredentialHash", credentialHash),
                    ("ExpectedStatus", (byte)IdentityRegistrationInvitationStatus.Pending),
                    ("Version", record.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affectedRows == 1
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(InvalidInvitation());
    }

    public async Task<Result<bool>> BindCreatedUserAsync(
        Guid invitationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<RegistrationInvitationRecord>(
                RegistrationInvitationSql.FindById,
                IdentitySqlParameters.Create(("InvitationId", invitationId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || record.RevokedAtUtc.HasValue)
        {
            return Result<bool>.Failure(InvalidInvitation());
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                RegistrationInvitationSql.BindUser,
                IdentitySqlParameters.Create(
                    ("InvitationId", invitationId),
                    ("BoundUserId", userId),
                    ("Status", (byte)IdentityRegistrationInvitationStatus.AccountCreatedPendingOnboarding),
                    ("Version", record.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affectedRows == 1
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(InvalidInvitation());
    }

    private async Task<RegistrationInvitationRecord?> FindActiveAsync(
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<RegistrationInvitationRecord>(
                RegistrationInvitationSql.FindById,
                IdentitySqlParameters.Create(("InvitationId", invitationId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null
            || record.RevokedAtUtc.HasValue
            || record.ExpiresAtUtc <= clock.UtcNow
            || record.Status == (byte)IdentityRegistrationInvitationStatus.Revoked)
        {
            return null;
        }

        return record;
    }

    private static Error InvalidInvitation() => new(
        IdentityErrorCodes.RegistrationInvitationInvalid,
        "The registration invitation is invalid or has expired.",
        ErrorType.Validation);
}
