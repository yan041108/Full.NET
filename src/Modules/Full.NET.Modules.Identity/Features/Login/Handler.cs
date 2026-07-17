using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Features.Login;

internal sealed class Handler(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser> passwordHasher,
    IClock clock,
    IIdGenerator idGenerator,
    IAccessTokenIssuer accessTokenIssuer,
    IRandomTokenGenerator randomTokenGenerator,
    IOptions<IdentityOptions> options)
    : ICommandHandler<Command, LoginSessionResult>
{
    private const string HostScope = "host";
    private readonly IdentityOptions _options = options.Value;

    public async Task<Result<LoginSessionResult>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = command.Username.Trim().ToUpperInvariant();
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                new { ScopeKey = HostScope, NormalizedUsername = normalizedUsername },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            await WriteAuditAsync(
                null,
                null,
                normalizedUsername,
                "login",
                "identity.user-not-found",
                false,
                command,
                cancellationToken).ConfigureAwait(false);
            return InvalidCredentials();
        }

        var user = ToUser(record);
        if (!user.IsActive)
        {
            await WriteAuditAsync(
                user.Id,
                null,
                normalizedUsername,
                "login",
                "identity.user-disabled",
                false,
                command,
                cancellationToken).ConfigureAwait(false);
            return InvalidCredentials();
        }

        if (user.LockoutEndUtc > clock.UtcNow)
        {
            await WriteAuditAsync(
                user.Id,
                null,
                normalizedUsername,
                "login",
                "identity.account-locked",
                false,
                command,
                cancellationToken).ConfigureAwait(false);
            return InvalidCredentials();
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            command.Password);
        if (verification == Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed)
        {
            await RecordFailureAsync(user, command, cancellationToken).ConfigureAwait(false);
            return InvalidCredentials();
        }

        var passwordHash = verification
            == Microsoft.AspNetCore.Identity.PasswordVerificationResult.SuccessRehashNeeded
            ? passwordHasher.HashPassword(user, command.Password)
            : user.PasswordHash;
        await EnsureSingleRowAsync(
            IdentitySql.UpdateLoginSuccess,
            new LoginSuccessUpdate(
                user.Id,
                passwordHash,
                clock.UtcNow,
                user.Version),
            "login success update",
            cancellationToken).ConfigureAwait(false);

        user = user with
        {
            PasswordHash = passwordHash,
            FailedLoginCount = 0,
            LockoutEndUtc = null,
            UpdatedAtUtc = clock.UtcNow,
            Version = user.Version + 1,
        };
        var sessionId = idGenerator.NewId();
        var familyId = idGenerator.NewId();
        var refreshToken = randomTokenGenerator.Generate(32);
        var csrfToken = randomTokenGenerator.Generate(32);
        var session = new Full.NET.Modules.Identity.Domain.RefreshSession(
            sessionId,
            user.Id,
            familyId,
            _options.ClientId,
            TokenHash.Compute(refreshToken),
            clock.UtcNow.AddDays(_options.RefreshTokenDays),
            null,
            null,
            null,
            clock.UtcNow,
            1);
        await EnsureSingleRowAsync(
            IdentitySql.InsertRefreshSession,
            session,
            "refresh session insert",
            cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(
            user.Id,
            sessionId,
            normalizedUsername,
            "login",
            "identity.login-succeeded",
            true,
            command,
            cancellationToken).ConfigureAwait(false);

        var accessToken = accessTokenIssuer.Issue(user, sessionId);
        return Result<LoginSessionResult>.Success(new LoginSessionResult(
            new TokenResponse(
                accessToken.AccessToken,
                "Bearer",
                accessToken.ExpiresAtUtc),
            refreshToken,
            csrfToken));
    }

    private async Task RecordFailureAsync(
        IdentityUser user,
        Command command,
        CancellationToken cancellationToken)
    {
        var failedLoginCount = user.FailedLoginCount + 1;
        var lockoutEnd = failedLoginCount >= _options.LockoutThreshold
            ? clock.UtcNow.AddMinutes(_options.LockoutMinutes)
            : (DateTimeOffset?)null;
        await EnsureSingleRowAsync(
            IdentitySql.UpdateLoginFailure,
            new LoginFailureUpdate(
                user.Id,
                failedLoginCount,
                lockoutEnd,
                clock.UtcNow,
                user.Version),
            "login failure update",
            cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(
            user.Id,
            null,
            user.NormalizedUsername,
            "login",
            lockoutEnd.HasValue
                ? "identity.account-locked"
                : "identity.password-mismatch",
            false,
            command,
            cancellationToken).ConfigureAwait(false);
    }

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
            clock.UtcNow);
        await EnsureSingleRowAsync(
            IdentitySql.InsertAuthAudit,
            audit,
            "authentication audit insert",
            cancellationToken).ConfigureAwait(false);
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
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Identity {operation} affected {affectedRows} rows instead of one.");
        }
    }

    private static IdentityUser ToUser(IdentityUserRecord record) => new(
        record.Id,
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
        record.CreatedAtUtc,
        record.UpdatedAtUtc,
        record.Version);

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

    private static Result<LoginSessionResult> InvalidCredentials() =>
        Result<LoginSessionResult>.Failure(new Error(
            "identity.invalid_credentials",
            "The username or password is invalid.",
            ErrorType.Unauthorized));
}
