using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Features.ManageTenantMembers;

internal sealed class TenantMemberProvisionService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    ICurrentTenantContextWriter currentTenantWriter,
    ITenantMemberSeatQuotaPort seatQuotaPort,
    TenantMembershipQueryService queries,
    Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser> passwordHasher,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const string HostScope = "host";

    public Task<Result<TenantMemberResponse>> ProvisionAsync(
        ProvisionTenantMemberRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => ProvisionCoreAsync(request, token),
            cancellationToken);

    private async Task<Result<TenantMemberResponse>> ProvisionCoreAsync(
        ProvisionTenantMemberRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentTenant.IsAvailable || currentTenant.Id is not Guid tenantId)
        {
            return Result<TenantMemberResponse>.Failure(new Error(
                IdentityErrorCodes.DataScopeTenantContextRequired,
                "Tenant context is required.",
                ErrorType.Validation));
        }

        var username = request.Username?.Trim() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        var role = request.MemberRole?.Trim() ?? string.Empty;
        if (username.Length is < 3 or > 128 || displayName.Length is < 1 or > 128)
        {
            return Result<TenantMemberResponse>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Username or display name is invalid.",
                ErrorType.Validation));
        }

        if (role is not (TenantMemberRoles.Admin or TenantMemberRoles.Member))
        {
            return Result<TenantMemberResponse>.Failure(new Error(
                IdentityErrorCodes.TenantMemberRoleInvalid,
                "Member role is invalid.",
                ErrorType.Validation));
        }

        var passwordViolations = IdentityPasswordPolicy.Validate(password);
        if (passwordViolations.Count > 0)
        {
            return Result<TenantMemberResponse>.Failure(new Error(
                Code: ValidationErrorCodes.Failed,
                Message: "The password does not satisfy the password policy.",
                Type: ErrorType.Validation,
                ValidationErrors: new Dictionary<string, string[]>
                {
                    [nameof(ProvisionTenantMemberRequest.Password)] = passwordViolations
                        .Select(violation => violation.Code)
                        .ToArray()
                }));
        }

        var normalizedUsername = username.ToUpperInvariant();
        var existingUser = await IdentityHostExecutionScope.RunAsync(
                currentTenantWriter,
                () => queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                    IdentitySql.FindUserByScopeAndUsername,
                    IdentitySqlParameters.Create(
                        ("ScopeKey", HostScope),
                        ("NormalizedUsername", normalizedUsername)),
                    cancellationToken))
            .ConfigureAwait(false);
        if (existingUser is not null)
        {
            return Result<TenantMemberResponse>.Failure(new Error(
                IdentityErrorCodes.UsernameExists,
                "A host user with this username already exists.",
                ErrorType.Conflict));
        }

        var email = request.Email?.Trim();
        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = AccountChallengeService.NormalizeEmail(email);
            if (normalizedEmail is null)
            {
                return Result<TenantMemberResponse>.Failure(new Error(
                    ValidationErrorCodes.Failed,
                    "Email is invalid.",
                    ErrorType.Validation));
            }
        }

        var now = clock.UtcNow;
        var userId = idGenerator.NewId();
        var user = new IdentityUser(
            userId,
            null,
            HostScope,
            username,
            normalizedUsername,
            displayName,
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
            PasswordHash = passwordHasher.HashPassword(user, password),
            MustChangePassword = true,
        };

        var userRecord = new IdentityUserRecord(
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
            user.Version,
            user.PreferredLocale,
            user.ProfileVersion,
            user.AccountType,
            user.MustChangePassword,
            user.PasswordChangedAtUtc);

        var operationId = idGenerator.NewId().ToString("D");
        var reserved = false;
        try
        {
            var reserveResult = await IdentityHostExecutionScope.RunAsync(
                    currentTenantWriter,
                    () => seatQuotaPort.TryReserveAsync(
                        tenantId,
                        operationId,
                        cancellationToken))
                .ConfigureAwait(false);
            if (!reserveResult.IsSuccess)
            {
                return Result<TenantMemberResponse>.Failure(reserveResult.Error!);
            }

            reserved = true;
            await IdentityHostExecutionScope.RunAsync(
                    currentTenantWriter,
                    async () =>
                    {
                        var userAffected = await commandExecutor.ExecuteAsync(
                                IdentitySql.InsertUser,
                                userRecord,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (userAffected != 1)
                        {
                            throw new InvalidOperationException(
                                $"Host user insert affected {userAffected} rows instead of one.");
                        }

                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            await commandExecutor.ExecuteAsync(
                                    AccountLifecycleSql.InsertUserProfileEmail,
                                    IdentitySqlParameters.Create(
                                        ("UserId", userId),
                                        ("Nickname", displayName),
                                        ("Email", AccountChallengeService.NormalizeEmail(email))),
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                    })
                .ConfigureAwait(false);

            var memberId = idGenerator.NewId();
            await commandExecutor.ExecuteAsync(
                    TenantMembershipSql.InsertMember,
                    IdentitySqlParameters.Create(
                        ("Id", memberId),
                        ("UserId", userId),
                        ("MemberRole", role),
                        ("Status", TenantMemberStatuses.Active),
                        ("CreatedAtUtc", now),
                        ("UpdatedAtUtc", now),
                        ("Version", 1)),
                    cancellationToken)
                .ConfigureAwait(false);

            var confirmResult = await IdentityHostExecutionScope.RunAsync(
                    currentTenantWriter,
                    () => seatQuotaPort.ConfirmAsync(
                        tenantId,
                        operationId,
                        cancellationToken))
                .ConfigureAwait(false);
            if (!confirmResult.IsSuccess)
            {
                return Result<TenantMemberResponse>.Failure(confirmResult.Error!);
            }

            reserved = false;
            var row = await queryExecutor.QuerySingleOrDefaultAsync<TenantMemberListRow>(
                    TenantMembershipSql.FindMemberListRowByTenantAndUser,
                    IdentitySqlParameters.Create(("UserId", userId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (row is null)
            {
                return await queries.GetMemberByIdAsync(memberId, cancellationToken)
                    .ConfigureAwait(false);
            }

            return Result<TenantMemberResponse>.Success(new TenantMemberResponse(
                row.Id,
                row.TenantId,
                row.UserId,
                row.Username,
                row.DisplayName,
                row.MemberRole,
                row.Status,
                row.CreatedAtUtc,
                row.UpdatedAtUtc,
                row.Version));
        }
        finally
        {
            if (reserved)
            {
                await IdentityHostExecutionScope.RunAsync(
                        currentTenantWriter,
                        () => seatQuotaPort.ReleaseAsync(tenantId, operationId, cancellationToken))
                    .ConfigureAwait(false);
            }
        }
    }
}
