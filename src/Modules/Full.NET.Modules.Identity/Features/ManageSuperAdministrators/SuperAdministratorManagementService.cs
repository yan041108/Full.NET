using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using FullNetIdentityOptions = Full.NET.Modules.Identity.Configuration.IdentityOptions;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Features.ManageSuperAdministrators;

/// <summary>
/// 为远程高风险入口执行环境门禁、操作者解析和当前密码重认证，领域不变量仍由受保护服务负责。
/// </summary>
internal sealed class SuperAdministratorManagementService(
    IQueryExecutor queryExecutor,
    ISuperAdministratorService domainService,
    IPasswordHasher<IdentityUser> passwordHasher,
    IOptions<FullNetIdentityOptions> options,
    IHostEnvironment environment)
{
    private const string HostScope = "host";
    private readonly FullNetIdentityOptions _options = options.Value;

    public async Task<Result<SuperAdministratorChangeResponse>> GrantAsync(
        ClaimsPrincipal principal,
        string targetUsername,
        string currentPassword,
        CancellationToken cancellationToken = default)
    {
        var operatorResult = await ReauthenticateAsync(
                principal,
                currentPassword,
                cancellationToken)
            .ConfigureAwait(false);
        if (!operatorResult.IsSuccess)
        {
            return Result<SuperAdministratorChangeResponse>.Failure(
                operatorResult.Error!);
        }

        var target = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                new
                {
                    ScopeKey = HostScope,
                    NormalizedUsername = NormalizeUsername(targetUsername),
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (target is not { IsActive: true, TenantId: null }
            || !string.Equals(target.ScopeKey, HostScope, StringComparison.Ordinal))
        {
            return Failure(
                IdentityErrorCodes.SuperAdministratorTargetNotFound,
                "The target is not an active Host account.",
                ErrorType.Forbidden);
        }

        return await domainService.GrantAsync(
                operatorResult.Value!.Id,
                target.Id,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<SuperAdministratorChangeResponse>> RevokeAsync(
        ClaimsPrincipal principal,
        Guid targetUserId,
        string currentPassword,
        CancellationToken cancellationToken = default)
    {
        var operatorResult = await ReauthenticateAsync(
                principal,
                currentPassword,
                cancellationToken)
            .ConfigureAwait(false);
        if (!operatorResult.IsSuccess)
        {
            return Result<SuperAdministratorChangeResponse>.Failure(
                operatorResult.Error!);
        }

        return await domainService.RevokeAsync(
                operatorResult.Value!.Id,
                targetUserId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<IdentityUser>> ReauthenticateAsync(
        ClaimsPrincipal principal,
        string currentPassword,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableRemoteSuperAdministratorManagement
            || environment.IsProduction())
        {
            return Failure<IdentityUser>(
                IdentityErrorCodes.SuperAdministratorRemoteManagementDisabled,
                "Remote super-administrator management is disabled.",
                ErrorType.Forbidden);
        }

        if (!Guid.TryParse(
                principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                out var operatorUserId))
        {
            return ReauthenticationFailed();
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = operatorUserId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is not { IsActive: true }
            || string.IsNullOrEmpty(currentPassword))
        {
            return ReauthenticationFailed();
        }

        var user = ToUser(record);
        var verification = passwordHasher.VerifyHashedPassword(
            user,
            record.PasswordHash,
            currentPassword);
        return verification == PasswordVerificationResult.Failed
            ? ReauthenticationFailed()
            : Result<IdentityUser>.Success(user);
    }

    private static string NormalizeUsername(string value) =>
        value.Trim().ToUpperInvariant();

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
        record.Version,
        record.PreferredLocale,
        record.ProfileVersion);

    private static Result<IdentityUser> ReauthenticationFailed() =>
        Failure<IdentityUser>(
            IdentityErrorCodes.SuperAdministratorReauthenticationFailed,
            "The current password reauthentication failed.",
            ErrorType.Unauthorized);

    private static Result<SuperAdministratorChangeResponse> Failure(
        string code,
        string message,
        ErrorType type) =>
        Failure<SuperAdministratorChangeResponse>(code, message, type);

    private static Result<T> Failure<T>(
        string code,
        string message,
        ErrorType type) =>
        Result<T>.Failure(new Error(code, message, type));
}
