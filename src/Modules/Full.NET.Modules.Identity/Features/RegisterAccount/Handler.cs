using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;
using Full.NET.Modules.Identity.Features.RegistrationInvitations;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Identity;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Features.RegisterAccount;

internal sealed class Handler(
    RegistrationPolicyService policyService,
    AccountChallengeService challengeService,
    RegistrationInvitationService invitationService,
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IPasswordHasher<IdentityUser> passwordHasher,
    IIdentityActiveTenantDirectory activeTenants,
    IClock clock,
    IIdGenerator idGenerator) : ICommandHandler<Command, RegisterAccountResponse>
{
    public async Task<Result<RegisterAccountResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var target = await ResolveTargetAsync(command.Request, cancellationToken).ConfigureAwait(false);
        if (!target.IsSuccess)
        {
            return Result<RegisterAccountResponse>.Failure(target.Error!);
        }

        // Tenancy 权威读取不能借用 Identity 本地事务；事务内只复核本模块目标快照。
        if (!await activeTenants.IsActiveTenantAsync(target.Value!.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return Result<RegisterAccountResponse>.Failure(new Error(
                IdentityErrorCodes.RegistrationWayTenantInactive,
                "The target tenant was not found or is inactive.",
                ErrorType.NotFound));
        }

        return await transaction.ExecuteResultAsync(
                token => HandleCoreAsync(command.Request, target.Value!, token), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<RegistrationTarget>> ResolveTargetAsync(
        RegisterAccountRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await policyService.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!policy.IsSuccess)
        {
            return Result<RegistrationTarget>.Failure(policy.Error!);
        }

        var mode = policy.Value!.RegistrationMode;
        if (mode == IdentityRegistrationMode.Disabled)
        {
            return Result<RegistrationTarget>.Failure(new Error(
                IdentityErrorCodes.RegistrationDisabled,
                "Registration is disabled.",
                ErrorType.Forbidden));
        }

        var normalizedEmail = AccountChallengeService.NormalizeEmail(request.Email);
        if (normalizedEmail is null)
        {
            return Result<RegistrationTarget>.Failure(ValidationFailure().Error!);
        }

        int? wayVersion = null;
        VerifyRegistrationInvitationResponse? verifiedInvitation = null;
        Guid tenantId;
        IdentityAccountChallengePurpose challengePurpose;
        if (mode == IdentityRegistrationMode.InvitationOnly)
        {
            if (!request.InvitationId.HasValue || string.IsNullOrWhiteSpace(request.InvitationToken))
            {
                return Result<RegistrationTarget>.Failure(new Error(
                    IdentityErrorCodes.RegistrationInvitationInvalid,
                    "A valid invitation is required.",
                    ErrorType.Validation));
            }

            var invitation = await invitationService.VerifyAsync(
                    request.InvitationId.Value,
                    request.InvitationToken!,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!invitation.IsSuccess)
            {
                return Result<RegistrationTarget>.Failure(invitation.Error!);
            }

            verifiedInvitation = invitation.Value!;
            tenantId = verifiedInvitation.TenantId;
            challengePurpose = IdentityAccountChallengePurpose.InvitationEmailVerification;
            if (!string.Equals(verifiedInvitation.Email, normalizedEmail, StringComparison.Ordinal))
            {
                return Result<RegistrationTarget>.Failure(ValidationFailure().Error!);
            }
        }
        else
        {
            if (!request.RegistrationWayId.HasValue)
            {
                return Result<RegistrationTarget>.Failure(ValidationFailure().Error!);
            }

            var way = await queryExecutor.QuerySingleOrDefaultAsync<RegistrationWayRecord>(
                    RegistrationWaySql.FindById,
                    IdentitySqlParameters.Create(("WayId", request.RegistrationWayId.Value)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (way is null || !way.IsEnabled)
            {
                return Result<RegistrationTarget>.Failure(new Error(
                    IdentityErrorCodes.RegistrationWayNotFound,
                    "The registration way was not found.",
                    ErrorType.NotFound));
            }

            tenantId = way.TenantId;
            wayVersion = way.Version;
            challengePurpose = IdentityAccountChallengePurpose.RegistrationEmailVerification;
        }

        return Result<RegistrationTarget>.Success(new RegistrationTarget(
            policy.Value.Version, mode, tenantId, normalizedEmail, challengePurpose, wayVersion, verifiedInvitation));
    }

    private async Task<Result<RegisterAccountResponse>> HandleCoreAsync(
        RegisterAccountRequest request,
        RegistrationTarget expectedTarget,
        CancellationToken cancellationToken)
    {
        var target = await ResolveTargetAsync(request, cancellationToken).ConfigureAwait(false);
        if (!target.IsSuccess)
        {
            return Result<RegisterAccountResponse>.Failure(target.Error!);
        }

        // 预检查后策略、方式或邀请可能变化；禁止将原租户的检查结果用于新目标。
        if (target.Value != expectedTarget)
        {
            var code = target.Value!.PolicyVersion != expectedTarget.PolicyVersion
                || target.Value.Mode != expectedTarget.Mode
                ? IdentityErrorCodes.RegistrationPolicyVersionConflict
                : IdentityErrorCodes.RegistrationWayVersionConflict;
            return Result<RegisterAccountResponse>.Failure(new Error(
                code, "The registration target changed. Retry registration.", ErrorType.Conflict));
        }

        var tenantId = expectedTarget.TenantId;
        var normalizedEmail = expectedTarget.NormalizedEmail;
        var challengePurpose = expectedTarget.ChallengePurpose;
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                AccountLifecycleSql.FindUserByProfileEmail,
                IdentitySqlParameters.Create(("Email", normalizedEmail)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<RegisterAccountResponse>.Failure(new Error(
                IdentityErrorCodes.RegistrationEmailAlreadyExists,
                "An account with this email already exists.",
                ErrorType.Conflict));
        }

        var challenge = await challengeService.ConsumeAsync(
                request.ChallengeId,
                challengePurpose,
                normalizedEmail,
                request.ChallengeCode,
                cancellationToken)
            .ConfigureAwait(false);
        if (!challenge.IsSuccess)
        {
            return Result<RegisterAccountResponse>.Failure(challenge.Error!);
        }

        var passwordViolations = IdentityPasswordPolicy.Validate(request.Password);
        if (passwordViolations.Count > 0)
        {
            return Result<RegisterAccountResponse>.Failure(new Error(
                Code: ValidationErrorCodes.Failed,
                Message: "The password does not satisfy the password policy.",
                Type: ErrorType.Validation));
        }

        if (request.InvitationId.HasValue && !string.IsNullOrWhiteSpace(request.InvitationToken))
        {
            var consumed = await invitationService.ConsumeCredentialAsync(
                    request.InvitationId.Value,
                    request.InvitationToken!,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!consumed.IsSuccess)
            {
                return Result<RegisterAccountResponse>.Failure(consumed.Error!);
            }
        }

        var now = clock.UtcNow;
        var scopeKey = $"tenant:{tenantId:N}";
        var user = new IdentityUser(
            idGenerator.NewId(),
            tenantId,
            scopeKey,
            normalizedEmail,
            normalizedEmail.ToUpperInvariant(),
            request.DisplayName.Trim(),
            string.Empty,
            true,
            0,
            null,
            idGenerator.NewId().ToString("N"),
            now,
            null,
            1);
        user = user with
        {
            PasswordHash = passwordHasher.HashPassword(user, request.Password),
            MustChangePassword = false,
            PasswordChangedAtUtc = now,
        };

        await commandExecutor.ExecuteAsync(IdentitySql.InsertUser, user, cancellationToken).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
                AccountLifecycleSql.InsertUserProfileEmail,
                IdentitySqlParameters.Create(
                    ("UserId", user.Id),
                    ("Nickname", request.DisplayName.Trim()),
                    ("Email", normalizedEmail)),
                cancellationToken)
            .ConfigureAwait(false);

        if (request.InvitationId.HasValue)
        {
            await invitationService.BindCreatedUserAsync(request.InvitationId.Value, user.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<RegisterAccountResponse>.Success(new RegisterAccountResponse(user.Id, true));
    }

    private sealed record RegistrationTarget(
        int PolicyVersion,
        IdentityRegistrationMode Mode,
        Guid TenantId,
        string NormalizedEmail,
        IdentityAccountChallengePurpose ChallengePurpose,
        int? WayVersion,
        VerifyRegistrationInvitationResponse? Invitation);

    private static Result<RegisterAccountResponse> ValidationFailure() =>
        Result<RegisterAccountResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "The registration request is invalid.",
            ErrorType.Validation));
}
