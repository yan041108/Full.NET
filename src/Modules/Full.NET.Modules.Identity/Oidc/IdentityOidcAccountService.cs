using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.ManageMfaRecoveryCodes;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>中心改密与恢复码重置；密码 CAS、恢复码消费及所有认证权威撤销共用事务。</summary>
internal sealed class IdentityOidcAccountService(ICommandTransaction transaction, IQueryExecutor queries,
    ICommandExecutor commands, IPasswordHasher<IdentityUser> hasher, IClock clock,
    IdentityOidcCenterLoginService login, MfaRecoveryCodeService recovery,
    IIdentityOidcUserAuthorityRevoker revoker, OidcAuthenticationEventWriter audit, ICurrentTenantContextWriter tenant,
    IOptions<Configuration.IdentityOptions> options)
{
    public Task<Result<bool>> ChangeByCredentialsAsync(string username, string password, string? secondFactor,
        string nextPassword, CancellationToken token) => InHostAsync(() => transaction.ExecuteAsync(async ct =>
    {
        if (string.IsNullOrEmpty(nextPassword) || nextPassword.Length > 1024
            || IdentityPasswordPolicy.Validate(nextPassword).Count != 0) return Invalid(ValidationErrorCodes.Failed);
        var attempt = await login.AuthenticateWithOutcomeAsync(username, password, ct, secondFactor).ConfigureAwait(false);
        if (attempt.Login is null) return Invalid(IdentityErrorCodes.InvalidCredentials);
        return await UpdateAsync(attempt.Login.UserId, password, nextPassword, false, ct).ConfigureAwait(false);
    }, token));

    public Task<Result<bool>> ChangeAuthenticatedAsync(Guid userId, string password, string nextPassword,
        CancellationToken token) => InHostAsync(() => transaction.ExecuteAsync(ct => UpdateAsync(userId, password, nextPassword, false, ct), token));

    public Task<Result<bool>> RecoverAsync(string username, string recoveryCode, string nextPassword, CancellationToken token)
        => InHostAsync(() => transaction.ExecuteAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(username) || username.Length > 128 || string.IsNullOrEmpty(nextPassword)
                || nextPassword.Length > 1024 || IdentityPasswordPolicy.Validate(nextPassword).Count != 0)
                return Invalid(ValidationErrorCodes.Failed);
            var user = await queries.QuerySingleOrDefaultAsync<IdentityUserRecord>(IdentitySql.FindUserByScopeAndUsername,
                IdentitySqlParameters.Create(("ScopeKey", "host"), ("NormalizedUsername", username.Trim().ToUpperInvariant())), ct)
                .ConfigureAwait(false);
            if (user is not { IsActive: true } || user.LockoutEndUtc > clock.UtcNow
                || !(await recovery.ConsumeAsync(user.Id, recoveryCode, ct).ConfigureAwait(false)).IsSuccess)
            {
                await audit.WriteAsync(user?.Id, username.Trim().ToUpperInvariant(), "password.recovery",
                    "identity.invalid_recovery_proof", false, ct).ConfigureAwait(false);
                return Invalid(IdentityErrorCodes.MfaRecoveryCodeInvalid);
            }
            return await UpdateAsync(user.Id, null, nextPassword, true, ct).ConfigureAwait(false);
        }, token));

    private async Task<Result<bool>> UpdateAsync(Guid id, string? password, string nextPassword, bool recovered, CancellationToken token)
    {
        if (string.IsNullOrEmpty(nextPassword) || nextPassword.Length > 1024 || password?.Length > 1024
            || IdentityPasswordPolicy.Validate(nextPassword).Count != 0) return Invalid(ValidationErrorCodes.Failed);
        var record = await queries.QuerySingleOrDefaultAsync<IdentityUserRecord>(IdentitySql.FindHostUserById,
            IdentitySqlParameters.Create(("UserId", id)), token).ConfigureAwait(false);
        if (record is not { IsActive: true } || record.LockoutEndUtc > clock.UtcNow) return Invalid(IdentityErrorCodes.InvalidCredentials);
        var user = new IdentityUser(record.Id, null, "host", record.Username, record.NormalizedUsername,
            record.DisplayName, record.PasswordHash, true, record.FailedLoginCount, record.LockoutEndUtc,
            record.SecurityStamp, record.CreatedAtUtc, record.UpdatedAtUtc, record.Version);
        if (!recovered && (password is null || hasher.VerifyHashedPassword(user, record.PasswordHash, password)
            == PasswordVerificationResult.Failed))
        {
            await commands.ExecuteAsync(IdentitySql.UpdateLoginFailure,
                new LoginFailureUpdate(id, record.FailedLoginCount + 1,
                    record.FailedLoginCount + 1 >= options.Value.LockoutThreshold ? clock.UtcNow.AddMinutes(options.Value.LockoutMinutes) : null,
                    clock.UtcNow, record.Version), token)
                .ConfigureAwait(false);
            await audit.WriteAsync(id, record.NormalizedUsername, "password.self_change", "identity.invalid_credentials", false, token)
                .ConfigureAwait(false);
            return Invalid(IdentityErrorCodes.InvalidCredentials);
        }
        if (hasher.VerifyHashedPassword(user, record.PasswordHash, nextPassword) != PasswordVerificationResult.Failed)
            return Invalid(IdentityErrorCodes.NewPasswordSameAsCurrent);
        var count = await commands.ExecuteAsync(IdentityOidcInteractionSql.ChangePassword,
            IdentitySqlParameters.Create(("UserId", id), ("Version", record.Version), ("PreviousStamp", record.SecurityStamp),
                ("PasswordHash", hasher.HashPassword(user, nextPassword)), ("SecurityStamp", Guid.CreateVersion7().ToString("N")),
                ("Now", clock.UtcNow)), token).ConfigureAwait(false);
        if (count != 1) return Invalid(IdentityErrorCodes.SessionContextConflict);
        await commands.ExecuteAsync(IdentitySql.RevokeAllUserSessions,
            IdentitySqlParameters.Create(("UserId", id), ("RevokedAtUtc", clock.UtcNow)), token).ConfigureAwait(false);
        await revoker.RevokeUserAuthorityAsync(id, token).ConfigureAwait(false);
        await audit.WriteAsync(id, record.NormalizedUsername, recovered ? "password.recovery" : "password.self_change",
            "identity.password_change_succeeded", true, token).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }

    private static Result<bool> Invalid(string code) => Result<bool>.Failure(new Error(code,
        "The account proof or password is not acceptable.", ErrorType.Validation));

    private async Task<Result<bool>> InHostAsync(Func<Task<Result<bool>>> action)
    {
        var wasHost = tenant.IsHost;
        var previous = tenant.Id is Guid id ? new TenantContext(id, tenant.Identifier!, tenant.Name!) : null;
        tenant.SetHost();
        try { return await action().ConfigureAwait(false); }
        finally
        {
            if (previous is not null) tenant.SetTenant(previous);
            else if (wasHost) tenant.SetHost();
            else tenant.Clear();
        }
    }
}
