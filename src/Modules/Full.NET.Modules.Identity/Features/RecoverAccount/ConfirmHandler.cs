using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Identity;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Features.RecoverAccount;

internal sealed class ConfirmHandler(
    AccountChallengeService challengeService,
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IPasswordHasher<IdentityUser> passwordHasher,
    IIdentityOidcUserAuthorityRevoker oidcUserAuthorityRevoker,
    IClock clock,
    IIdGenerator idGenerator) : ICommandHandler<ConfirmCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(
        ConfirmCommand command,
        CancellationToken cancellationToken)
    {
        var challenge = await queryExecutor.QuerySingleOrDefaultAsync<AccountChallengeRecord>(
                AccountChallengeSql.FindById,
                IdentitySqlParameters.Create(("ChallengeId", command.Request.ChallengeId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (challenge is null)
        {
            return Result<bool>.Failure(InvalidChallenge());
        }

        var consumed = await challengeService.ConsumeAsync(
                command.Request.ChallengeId,
                IdentityAccountChallengePurpose.PasswordRecovery,
                challenge.NormalizedEmail,
                command.Request.ChallengeCode,
                cancellationToken)
            .ConfigureAwait(false);
        if (!consumed.IsSuccess)
        {
            return consumed;
        }

        var user = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                AccountLifecycleSql.FindUserByProfileEmail,
                IdentitySqlParameters.Create(("Email", challenge.NormalizedEmail)),
                cancellationToken)
            .ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<bool>.Failure(InvalidChallenge());
        }

        var passwordViolations = IdentityPasswordPolicy.Validate(command.Request.NewPassword);
        if (passwordViolations.Count > 0)
        {
            return Result<bool>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "The password does not satisfy the password policy.",
                ErrorType.Validation));
        }

        var domainUser = new IdentityUser(
            user.Id,
            user.TenantId,
            user.ScopeKey,
            user.Username,
            user.NormalizedUsername,
            user.DisplayName,
            user.PasswordHash,
            user.IsActive,
            user.FailedLoginCount,
            user.LockoutEndUtc,
            user.SecurityStamp,
            user.CreatedAtUtc,
            user.UpdatedAtUtc,
            user.Version);
        var passwordHash = passwordHasher.HashPassword(domainUser, command.Request.NewPassword);
        var securityStamp = idGenerator.NewId().ToString("N");
        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.ResetUserPasswordByIdentity,
                IdentitySqlParameters.Create(
                    ("UserId", user.Id),
                    ("ScopeKey", user.ScopeKey),
                    ("PasswordHash", passwordHash),
                    ("SecurityStamp", securityStamp),
                    ("PasswordChangedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return Result<bool>.Failure(InvalidChallenge());
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.RevokeAllUserSessions,
                IdentitySqlParameters.Create(("UserId", user.Id), ("RevokedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        await oidcUserAuthorityRevoker.RevokeUserAuthorityAsync(user.Id, cancellationToken)
            .ConfigureAwait(false);
        return Result<bool>.Success(true);
    }

    private static Error InvalidChallenge() => new(
        IdentityErrorCodes.AccountChallengeInvalid,
        "The account challenge is invalid or has expired.",
        ErrorType.Validation);
}
