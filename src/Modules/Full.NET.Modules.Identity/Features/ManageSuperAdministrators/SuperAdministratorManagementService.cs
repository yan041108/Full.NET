using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using FullNetIdentityOptions = Full.NET.Modules.Identity.Configuration.IdentityOptions;

namespace Full.NET.Modules.Identity.Features.ManageSuperAdministrators;

/// <summary>
/// 为远程高风险入口执行环境门禁与强认证，领域不变量仍由受保护服务负责。
/// </summary>
internal sealed class SuperAdministratorManagementService(
    IQueryExecutor queryExecutor,
    ISuperAdministratorService domainService,
    IStrongReauthenticationProvider reauthenticationProvider,
    IOptions<FullNetIdentityOptions> options,
    IHostEnvironment environment)
{
    private const string HostScope = "host";
    private readonly FullNetIdentityOptions _options = options.Value;

    public async Task<Result<SuperAdministratorChangeResponse>> GrantAsync(
        ClaimsPrincipal principal,
        string targetUsername,
        string currentPassword,
        string? totpCode = null,
        CancellationToken cancellationToken = default)
    {
        var operatorResult = await ReauthenticateAsync(
                principal,
                currentPassword,
                totpCode,
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
        string? totpCode = null,
        CancellationToken cancellationToken = default)
    {
        var operatorResult = await ReauthenticateAsync(
                principal,
                currentPassword,
                totpCode,
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
        string? totpCode,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableRemoteSuperAdministratorManagement)
        {
            return Disabled();
        }

        if (environment.IsProduction()
            && !reauthenticationProvider.IsProductionEligible)
        {
            return Disabled();
        }

        if (!Guid.TryParse(
                principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
                out var operatorUserId))
        {
            return Result<IdentityUser>.Failure(new Error(
                IdentityErrorCodes.SuperAdministratorReauthenticationFailed,
                "The current password reauthentication failed.",
                ErrorType.Unauthorized));
        }

        return await reauthenticationProvider.VerifyAsync(
                operatorUserId,
                currentPassword,
                totpCode,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string NormalizeUsername(string value) =>
        value.Trim().ToUpperInvariant();

    private static Result<IdentityUser> Disabled() =>
        Result<IdentityUser>.Failure(new Error(
            IdentityErrorCodes.SuperAdministratorRemoteManagementDisabled,
            "Remote super-administrator management is disabled.",
            ErrorType.Forbidden));

    private static Result<SuperAdministratorChangeResponse> Failure(
        string code,
        string message,
        ErrorType type) =>
        Result<SuperAdministratorChangeResponse>.Failure(new Error(code, message, type));
}
