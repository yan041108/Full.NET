using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using IdentityOptions = Full.NET.Modules.Identity.Configuration.IdentityOptions;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Features.ChangePassword;

/// <summary>
/// 当前用户自助改密处理器：校验当前密码、轮换 SecurityStamp、撤销其他会话并轮换当前 Refresh Session。
/// </summary>
internal sealed class Handler(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IPasswordHasher<IdentityUser> passwordHasher,
    IClock clock,
    IIdGenerator idGenerator,
    IPermissionSnapshotReader permissionSnapshotReader,
    IAccessTokenIssuer accessTokenIssuer,
    IRandomTokenGenerator randomTokenGenerator,
    IOptions<IdentityOptions> options)
    : ICommandHandler<Command, ChangePasswordSessionResult>
{
    private readonly IdentityOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<Result<ChangePasswordSessionResult>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        if (!TryReadIdentity(
                command.Principal,
                out var userId,
                out var sessionId,
                out var scopeKey))
        {
            return Unauthorized();
        }

        var session = await FindSessionAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (!IsOwnedActiveSession(session, userId, scopeKey, command.Principal))
        {
            return Unauthorized();
        }

        if (!session!.IsActive)
        {
            return Unauthorized();
        }

        var user = ToUser(session);
        var currentVerification = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            command.CurrentPassword);
        if (currentVerification == PasswordVerificationResult.Failed)
        {
            await WriteAuditAsync(
                    user.Id,
                    sessionId,
                    session.NormalizedUsername,
                    "password_change",
                    IdentityErrorCodes.CurrentPasswordInvalid,
                    false,
                    command,
                    cancellationToken)
                .ConfigureAwait(false);
            return CurrentPasswordInvalid();
        }

        var newPasswordViolations = IdentityPasswordPolicy.Validate(command.NewPassword);
        if (newPasswordViolations.Count > 0)
        {
            return ValidationFailure(newPasswordViolations);
        }

        var samePasswordVerification = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            command.NewPassword);
        if (samePasswordVerification != PasswordVerificationResult.Failed)
        {
            return NewPasswordSameAsCurrent();
        }

        var passwordHash = passwordHasher.HashPassword(user, command.NewPassword);
        var securityStamp = idGenerator.NewId().ToString("N");
        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.ResetUserPasswordByIdentity,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("ScopeKey", scopeKey),
                    ("PasswordHash", passwordHash),
                    ("SecurityStamp", securityStamp),
                    ("PasswordChangedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return Unauthorized();
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.RevokeUserSessionsExcept,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("ExceptSessionId", sessionId),
                    ("RevokedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        var refreshedSession = await FindSessionAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (!IsOwnedActiveSession(refreshedSession, userId, scopeKey, securityStamp))
        {
            return Unauthorized();
        }

        var replacementId = idGenerator.NewId();
        var replacementToken = randomTokenGenerator.Generate(32);
        var csrfToken = randomTokenGenerator.Generate(32);
        var consumed = false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var consumedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.ConsumeRefreshSession,
                    new ConsumeRefreshSessionUpdate(
                        refreshedSession!.SessionId,
                        now,
                        replacementId,
                        refreshedSession.SessionVersion),
                    cancellationToken)
                .ConfigureAwait(false);
            if (consumedRows == 1)
            {
                consumed = true;
                break;
            }

            refreshedSession = await FindSessionAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);
            if (refreshedSession?.ConsumedAtUtc.HasValue == true)
            {
                return Unauthorized();
            }

            if (!IsOwnedActiveSession(refreshedSession, userId, scopeKey, securityStamp))
            {
                return Unauthorized();
            }
        }

        if (!consumed)
        {
            return Unauthorized();
        }

        var replacement = new Domain.RefreshSession(
            replacementId,
            refreshedSession!.UserId,
            refreshedSession.FamilyId,
            refreshedSession.ClientId,
            TokenHash.Compute(replacementToken),
            clock.UtcNow.AddDays(_options.RefreshTokenDays),
            null,
            null,
            null,
            refreshedSession.ActiveTenantId,
            now,
            1);
        await EnsureSingleRowAsync(
                IdentitySql.InsertRefreshSession,
                replacement,
                "replacement refresh session insert",
                cancellationToken)
            .ConfigureAwait(false);

        var authorization = await permissionSnapshotReader.ReadAsync(
                userId,
                scopeKey,
                refreshedSession.ActiveTenantId ?? refreshedSession.TenantId,
                cancellationToken)
            .ConfigureAwait(false);
        var updatedUser = ToUser(refreshedSession) with
        {
            PasswordHash = passwordHash,
            SecurityStamp = securityStamp,
            FailedLoginCount = 0,
            LockoutEndUtc = null,
            MustChangePassword = false,
            PasswordChangedAtUtc = now,
            UpdatedAtUtc = now,
            Version = refreshedSession.UserVersion,
        };
        var accessToken = accessTokenIssuer.Issue(
            updatedUser,
            replacementId,
            refreshedSession.ActiveTenantId,
            authorization.Permissions,
            authorization.IsSuperAdministrator);

        await WriteAuditAsync(
                userId,
                replacementId,
                refreshedSession.NormalizedUsername,
                "password_change",
                "identity.password_change_succeeded",
                true,
                command,
                cancellationToken)
            .ConfigureAwait(false);

        return Result<ChangePasswordSessionResult>.Success(new ChangePasswordSessionResult(
            new TokenResponse(
                accessToken.AccessToken,
                "Bearer",
                accessToken.ExpiresAtUtc),
            replacementToken,
            csrfToken));
    }

    private static bool TryReadIdentity(
        ClaimsPrincipal principal,
        out Guid userId,
        out Guid sessionId,
        out string scopeKey)
    {
        sessionId = Guid.Empty;
        scopeKey = principal.FindFirstValue(IdentityClaimTypes.ActorScope) ?? string.Empty;
        return Guid.TryParse(
                principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                out userId)
            && Guid.TryParse(
                principal.FindFirstValue(IdentityClaimTypes.SessionId),
                out sessionId)
            && !string.IsNullOrWhiteSpace(scopeKey);
    }

    private Task<RefreshSessionRecord?> FindSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
            IdentitySql.FindRefreshSessionById,
            IdentitySqlParameters.Create(("SessionId", sessionId)),
            cancellationToken);

    private bool IsOwnedActiveSession(
        RefreshSessionRecord? session,
        Guid userId,
        string scopeKey,
        ClaimsPrincipal principal) =>
        session is not null
        && session.UserId == userId
        && string.Equals(session.ScopeKey, scopeKey, StringComparison.Ordinal)
        && string.Equals(
            session.SecurityStamp,
            principal.FindFirstValue(IdentityClaimTypes.SecurityStamp),
            StringComparison.Ordinal)
        && session.IsActive
        && session.ExpiresAtUtc > clock.UtcNow
        && !session.ConsumedAtUtc.HasValue
        && !session.RevokedAtUtc.HasValue;

    private bool IsOwnedActiveSession(
        RefreshSessionRecord? session,
        Guid userId,
        string scopeKey,
        string securityStamp) =>
        session is not null
        && session.UserId == userId
        && string.Equals(session.ScopeKey, scopeKey, StringComparison.Ordinal)
        && string.Equals(session.SecurityStamp, securityStamp, StringComparison.Ordinal)
        && session.IsActive
        && session.ExpiresAtUtc > clock.UtcNow
        && !session.ConsumedAtUtc.HasValue
        && !session.RevokedAtUtc.HasValue;

    private async Task WriteAuditAsync(
        Guid? userId,
        Guid? sessionId,
        string normalizedUsername,
        string eventType,
        string resultCode,
        bool succeeded,
        Command command,
        CancellationToken cancellationToken)
    {
        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            userId,
            sessionId,
            TokenHash.Compute(normalizedUsername),
            eventType,
            resultCode,
            succeeded,
            Truncate(command.Client.IpAddress, 64),
            Truncate(command.Client.UserAgent, 512),
            null,
            clock.UtcNow,
            ActorUserId: userId,
            TraceId: System.Diagnostics.Activity.Current?.TraceId.ToString(),
            AuthenticationMethod: "password");
        await EnsureSingleRowAsync(
                IdentitySql.InsertAuthAudit,
                audit,
                "password change audit insert",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnsureSingleRowAsync(
        SqlStatement statement,
        object parameters,
        string operation,
        CancellationToken cancellationToken)
    {
        var affectedRows = await commandExecutor.ExecuteAsync(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows is not (1 or -1))
        {
            throw new InvalidOperationException(
                $"Identity {operation} affected {affectedRows} rows instead of one.");
        }
    }

    private static IdentityUser ToUser(RefreshSessionRecord record) => new(
        record.UserId,
        record.TenantId,
        record.ScopeKey,
        record.Username,
        record.NormalizedUsername,
        record.DisplayName,
        record.PasswordHash,
        record.IsActive,
        record.FailedLoginCount,
        record.LockoutEndUtc,
        record.SecurityStamp,
        record.UserCreatedAtUtc,
        record.UserUpdatedAtUtc,
        record.UserVersion,
        record.PreferredLocale,
        record.ProfileVersion,
        MustChangePassword: record.MustChangePassword,
        PasswordChangedAtUtc: record.PasswordChangedAtUtc);

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

    private static Result<ChangePasswordSessionResult> Unauthorized() =>
        Result<ChangePasswordSessionResult>.Failure(new Error(
            IdentityErrorCodes.SessionNotActive,
            "The current session is no longer active.",
            ErrorType.Unauthorized));

    private static Result<ChangePasswordSessionResult> CurrentPasswordInvalid() =>
        Result<ChangePasswordSessionResult>.Failure(new Error(
            IdentityErrorCodes.CurrentPasswordInvalid,
            "The current password is invalid.",
            ErrorType.Unauthorized));

    private static Result<ChangePasswordSessionResult> NewPasswordSameAsCurrent() =>
        Result<ChangePasswordSessionResult>.Failure(new Error(
            IdentityErrorCodes.NewPasswordSameAsCurrent,
            "The new password must be different from the current password.",
            ErrorType.Validation));

    private static Result<ChangePasswordSessionResult> ValidationFailure(
        IReadOnlyList<IdentityPasswordPolicyViolation> violations) =>
        Result<ChangePasswordSessionResult>.Failure(new Error(
            Code: ValidationErrorCodes.Failed,
            Message: "The password does not satisfy the password policy.",
            Type: ErrorType.Validation,
            ValidationErrors: new Dictionary<string, string[]>
            {
                [nameof(ChangePasswordRequest.NewPassword)] = violations
                    .Select(violation => violation.DefaultMessage)
                    .ToArray(),
            },
            Arguments: null,
            ValidationViolations: violations
                .Select(violation => new ValidationViolation(
                    nameof(ChangePasswordRequest.NewPassword),
                    violation.Code,
                    violation.Arguments))
                .ToArray()));
}
