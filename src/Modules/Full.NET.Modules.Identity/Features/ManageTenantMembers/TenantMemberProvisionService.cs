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
using Full.NET.Modules.Identity.Features.TenantSeatOperations;
using System.Security.Cryptography;
using System.Text;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

/// <summary>Identity 模块应用服务；编排事务、权限与持久化。</summary>
namespace Full.NET.Modules.Identity.Features.ManageTenantMembers;

internal sealed class TenantMemberProvisionService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    ICurrentTenantContextWriter currentTenantWriter,
    IIdentityActiveTenantDirectory activeTenants,
    TenantMembershipQueryService queries,
    Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser> passwordHasher,
    IClock clock,
    IIdGenerator idGenerator,
    TenantSeatOperationProcessor seatOperations)
{
    private const string HostScope = "host";

    public Task<Result<TenantMemberResponse>> ProvisionAsync(
        ProvisionTenantMemberRequest request,
        CancellationToken cancellationToken = default) =>
        ProvisionCoreAsync(request, cancellationToken);

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

        var tenantActive = await TenantMembershipActiveTenantGuard.EnsureTenantActiveAsync(
                tenantId,
                activeTenants,
                cancellationToken)
            .ConfigureAwait(false);
        if (!tenantActive.IsSuccess)
        {
            return Result<TenantMemberResponse>.Failure(tenantActive.Error!);
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
        var requestKey = "provision:" + Hash(normalizedUsername);
        var requestHash = Hash(string.Join("|", new[] { normalizedUsername, displayName, role,
            AccountChallengeService.NormalizeEmail(request.Email ?? string.Empty) ?? string.Empty }.Select(Uri.EscapeDataString)));
        if (existingUser is not null)
        {
            var operation = await seatOperations.FindRequestAsync(tenantId, requestKey, cancellationToken).ConfigureAwait(false);
            var account = new IdentityUser(existingUser.Id, existingUser.TenantId, existingUser.ScopeKey, existingUser.Username,
                existingUser.NormalizedUsername, existingUser.DisplayName, existingUser.PasswordHash, existingUser.IsActive,
                existingUser.FailedLoginCount, existingUser.LockoutEndUtc, existingUser.SecurityStamp, existingUser.CreatedAtUtc,
                existingUser.UpdatedAtUtc, existingUser.Version);
            if (operation is null || operation.UserId != existingUser.Id || operation.RequestHash != requestHash
                || passwordHasher.VerifyHashedPassword(account, existingUser.PasswordHash, password) == Microsoft.AspNetCore.Identity.PasswordVerificationResult.Failed)
                return Result<TenantMemberResponse>.Failure(new Error(IdentityErrorCodes.UsernameExists,
                    "A host user with this username already exists.", ErrorType.Conflict));
            if (operation.Phase == TenantSeatPhases.Cancelled)
            {
                if (operation.ErrorCode is "identity.seat_operation.member_removed" or "identity.seat_operation.member_changed")
                    return Result<TenantMemberResponse>.Failure(new Error(IdentityErrorCodes.TenantMemberVersionConflict,
                        "The original membership was terminated or changed.", ErrorType.Conflict));
                var previous = await queryExecutor.QuerySingleOrDefaultAsync<TenantMemberListRow>(TenantMembershipSql.FindMemberById,
                    IdentitySqlParameters.Create(("MemberId", operation.MemberId)), cancellationToken).ConfigureAwait(false);
                if (previous is null || previous.Status == TenantMemberStatuses.Active || previous.MemberRole == TenantMemberRoles.Owner)
                    return Result<TenantMemberResponse>.Failure(new Error(IdentityErrorCodes.TenantMemberVersionConflict,
                        "The previous member cannot be activated by this creation request.", ErrorType.Conflict));
                var restarted = await transaction.ExecuteResultAsync(async local =>
                {
                    if (await commandExecutor.ExecuteAsync(TenantMembershipSql.UpdateMember,
                        IdentitySqlParameters.Create(("MemberId", operation.MemberId), ("MemberRole", role),
                            ("Status", TenantMemberStatuses.Pending), ("UpdatedAtUtc", clock.UtcNow), ("Version", previous.Version)), local).ConfigureAwait(false) != 1)
                        return Result<bool>.Failure(new Error(IdentityErrorCodes.TenantMemberVersionConflict, "The member changed.", ErrorType.Conflict));
                    var affected = await IdentityHostExecutionScope.RunAsync(currentTenantWriter, () => commandExecutor.ExecuteAsync(TenantSeatOperationSql.Retry,
                        IdentitySqlParameters.Create(("Id", operation.Id), ("TenantId", tenantId), ("MemberVersion", previous.Version + 1),
                            ("Version", operation.Version), ("Now", clock.UtcNow)), local)).ConfigureAwait(false);
                    return affected == 1 ? Result<bool>.Success(true)
                        : Result<bool>.Failure(new Error(IdentityErrorCodes.TenantMemberVersionConflict, "The activation changed.", ErrorType.Conflict));
                }, cancellationToken).ConfigureAwait(false);
                if (!restarted.IsSuccess) return Result<TenantMemberResponse>.Failure(restarted.Error!);
            }
            var replay = await seatOperations.ProcessAsync(operation.Id, cancellationToken).ConfigureAwait(false);
            return replay.IsSuccess ? await queries.GetMemberByIdAsync(operation.MemberId, cancellationToken).ConfigureAwait(false)
                : Result<TenantMemberResponse>.Failure(replay.Error!);
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

        var memberId = idGenerator.NewId();
        var operationId = idGenerator.NewId();
        var writeResult = await transaction.ExecuteResultAsync(
            async token =>
            {
                await IdentityHostExecutionScope.RunAsync(
                        currentTenantWriter,
                        async () =>
                        {
                            var userAffected = await commandExecutor.ExecuteAsync(
                                    IdentitySql.InsertUser,
                                    userRecord,
                                    token)
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
                                        token)
                                    .ConfigureAwait(false);
                            }
                        })
                    .ConfigureAwait(false);

                var memberAffected = await commandExecutor.ExecuteAsync(
                        TenantMembershipSql.InsertMember,
                        IdentitySqlParameters.Create(
                            ("Id", memberId),
                            ("UserId", userId),
                            ("MemberRole", role),
                            ("Status", TenantMemberStatuses.Pending),
                            ("CreatedAtUtc", now),
                            ("UpdatedAtUtc", now),
                            ("Version", 1)),
                        token)
                    .ConfigureAwait(false);
                if (memberAffected != 1)
                    throw new InvalidOperationException("Tenant member insert did not persist exactly one row.");

                await seatOperations.EnlistAsync(new TenantSeatOperation(operationId, requestKey, tenantId, userId, memberId,
                    null, null, null, 1, null, TenantSeatPhases.Requested, requestHash, null, now, now, null, null, 1, 1), token).ConfigureAwait(false);

                return Result<Guid>.Success(memberId);
            },
            cancellationToken).ConfigureAwait(false);
        if (!writeResult.IsSuccess) return Result<TenantMemberResponse>.Failure(writeResult.Error!);
        var activation = await seatOperations.ProcessAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (!activation.IsSuccess) return Result<TenantMemberResponse>.Failure(activation.Error!);

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
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
