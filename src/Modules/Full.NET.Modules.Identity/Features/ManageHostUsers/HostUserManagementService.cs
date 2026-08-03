using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

/// <summary>Host 用户创建、禁用与启用；禁用超级管理员时沿用最后一名保护。</summary>
internal sealed class HostUserManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser> passwordHasher,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const string HostScope = "host";

    public Task<Result<HostUserResponse>> CreateAsync(
        CreateHostUserRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    public Task<Result<HostUserResponse>> DisableAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(userId, token),
            cancellationToken);

    public Task<Result<HostUserResponse>> EnableAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => EnableCoreAsync(userId, token),
            cancellationToken);

    public Task<Result<HostUserResponse>> UpdateAsync(
        Guid userId,
        UpdateHostUserRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(userId, request, token),
            cancellationToken);

    public Task<Result<HostUserResponse>> ResetPasswordAsync(
        Guid userId,
        ResetHostUserPasswordRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ResetPasswordCoreAsync(userId, request, token),
            cancellationToken);

    private async Task<Result<HostUserResponse>> CreateCoreAsync(
        CreateHostUserRequest request,
        CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        var passwordViolations = IdentityPasswordPolicy.Validate(password);
        if (passwordViolations.Count > 0)
        {
            return ValidationFailure(passwordViolations);
        }

        if (username.Length is < 3 or > 128 || displayName.Length is < 1 or > 128)
        {
            return Result<HostUserResponse>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Username or display name is invalid.",
                ErrorType.Validation));
        }

        var normalizedUsername = username.ToUpperInvariant();
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                new { ScopeKey = HostScope, NormalizedUsername = normalizedUsername },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Conflict();
        }

        var now = clock.UtcNow;
        var user = new IdentityUser(
            idGenerator.NewId(),
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
        user = user with { PasswordHash = passwordHasher.HashPassword(user, password) };
        var record = new IdentityUserRecord(
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
            user.ProfileVersion);
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.InsertUser,
                record,
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Host user insert affected {affectedRows} rows instead of one.");
        }

        HostUserProfileResponse? profileResponse = null;
        if (request.Profile is not null)
        {
            var profileResult = await UpsertProfileAsync(
                    user.Id,
                    request.Profile,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!profileResult.IsSuccess)
            {
                return Result<HostUserResponse>.Failure(profileResult.Error!);
            }

            profileResponse = profileResult.Value;
        }

        return Result<HostUserResponse>.Success(
            new HostUserResponse(
                user.Id,
                user.Username,
                user.DisplayName,
                user.IsActive,
                user.CreatedAtUtc,
                user.UpdatedAtUtc,
                user.Version,
                Profile: profileResponse));
    }

    private async Task<Result<HostUserResponse>> DisableCoreAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || !record.IsActive)
        {
            return NotFound();
        }

        if (await IsActiveSuperAdministratorAsync(userId, cancellationToken)
                .ConfigureAwait(false))
        {
            var activeCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                    IdentitySql.CountActiveSuperAdministrators,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (activeCount <= 1)
            {
                return Result<HostUserResponse>.Failure(new Error(
                    IdentityErrorCodes.SuperAdministratorLastRemaining,
                    "The last active super administrator cannot be disabled.",
                    ErrorType.BusinessRule));
            }
        }

        var now = clock.UtcNow;
        var disabledRows = await commandExecutor.ExecuteAsync(
                IdentitySql.DisableHostUser,
                new
                {
                    UserId = userId,
                    SecurityStamp = idGenerator.NewId().ToString("N"),
                    UpdatedAtUtc = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (disabledRows != 1)
        {
            return NotFound();
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.RevokeAllUserSessions,
                new { UserId = userId, RevokedAtUtc = now },
                cancellationToken)
            .ConfigureAwait(false);

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
        {
            return NotFound();
        }

        return Result<HostUserResponse>.Success(
            new HostUserResponse(
                updated.Id,
                updated.Username,
                updated.DisplayName,
                updated.IsActive,
                updated.CreatedAtUtc,
                updated.UpdatedAtUtc,
                updated.Version));
    }

    private async Task<Result<HostUserResponse>> EnableCoreAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || record.IsActive)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var enabledRows = await commandExecutor.ExecuteAsync(
                IdentitySql.EnableHostUser,
                new
                {
                    UserId = userId,
                    UpdatedAtUtc = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (enabledRows != 1)
        {
            return NotFound();
        }

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
        {
            return NotFound();
        }

        return Result<HostUserResponse>.Success(
            new HostUserResponse(
                updated.Id,
                updated.Username,
                updated.DisplayName,
                updated.IsActive,
                updated.CreatedAtUtc,
                updated.UpdatedAtUtc,
                updated.Version));
    }

    private async Task<Result<HostUserResponse>> UpdateCoreAsync(
        Guid userId,
        UpdateHostUserRequest request,
        CancellationToken cancellationToken)
    {
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is < 1 or > 128)
        {
            return Result<HostUserResponse>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Display name is invalid.",
                ErrorType.Validation));
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateHostUserDisplayName,
                new
                {
                    UserId = userId,
                    DisplayName = displayName,
                    UpdatedAtUtc = now,
                    request.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            var exists = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                    IdentitySql.FindHostUserById,
                    new { UserId = userId },
                    cancellationToken)
                .ConfigureAwait(false);
            if (exists is null)
            {
                return NotFound();
            }

            return VersionConflict();
        }

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
        {
            return NotFound();
        }

        HostUserProfileResponse? profileResponse = null;
        if (request.Profile is not null)
        {
            var profileResult = await UpsertProfileAsync(
                    userId,
                    request.Profile,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!profileResult.IsSuccess)
            {
                return Result<HostUserResponse>.Failure(profileResult.Error!);
            }

            profileResponse = profileResult.Value;
        }
        else
        {
            profileResponse = await LoadProfileResponseAsync(userId, cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<HostUserResponse>.Success(
            new HostUserResponse(
                updated.Id,
                updated.Username,
                updated.DisplayName,
                updated.IsActive,
                updated.CreatedAtUtc,
                updated.UpdatedAtUtc,
                updated.Version,
                Profile: profileResponse));
    }

    private async Task<Result<HostUserResponse>> ResetPasswordCoreAsync(
        Guid userId,
        ResetHostUserPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var password = request.Password ?? string.Empty;
        var passwordViolations = IdentityPasswordPolicy.Validate(password);
        if (passwordViolations.Count > 0)
        {
            return ValidationFailure(passwordViolations);
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || !record.IsActive)
        {
            return NotFound();
        }

        var user = new IdentityUser(
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
        var passwordHash = passwordHasher.HashPassword(user, password);
        var securityStamp = idGenerator.NewId().ToString("N");
        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.ResetHostUserPassword,
                new
                {
                    UserId = userId,
                    PasswordHash = passwordHash,
                    SecurityStamp = securityStamp,
                    UpdatedAtUtc = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound();
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.RevokeAllUserSessions,
                new { UserId = userId, RevokedAtUtc = now },
                cancellationToken)
            .ConfigureAwait(false);

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
        {
            return NotFound();
        }

        return Result<HostUserResponse>.Success(
            new HostUserResponse(
                updated.Id,
                updated.Username,
                updated.DisplayName,
                updated.IsActive,
                updated.CreatedAtUtc,
                updated.UpdatedAtUtc,
                updated.Version));
    }

    private async Task<Result<HostUserProfileResponse?>> UpsertProfileAsync(
        Guid userId,
        HostUserProfileWriteRequest profile,
        CancellationToken cancellationToken)
    {
        var existing = (await queryExecutor.QueryAsync<HostUserProfileRecord>(
                IdentitySql.ListHostUserProfilesByIds,
                new { UserIds = new[] { userId } },
                cancellationToken)
            .ConfigureAwait(false)).FirstOrDefault();

        if (existing is null)
        {
            var inserted = await commandExecutor.ExecuteAsync(
                    IdentitySql.InsertHostUserProfile,
                    HostUserProfileMapper.ToParameters(userId, profile),
                    cancellationToken)
                .ConfigureAwait(false);
            if (inserted != 1)
            {
                throw new InvalidOperationException(
                    $"Host user profile insert affected {inserted} rows instead of one.");
            }
        }
        else
        {
            var writeRequest = profile with
            {
                Version = profile.Version ?? existing.Version
            };
            var affected = await commandExecutor.ExecuteAsync(
                    IdentitySql.UpdateHostUserProfile,
                    HostUserProfileMapper.ToParameters(userId, writeRequest),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected != 1)
            {
                return Result<HostUserProfileResponse?>.Failure(new Error(
                    IdentityErrorCodes.ProfileVersionConflict,
                    "The host user profile was updated concurrently.",
                    ErrorType.Conflict));
            }
        }

        return Result<HostUserProfileResponse?>.Success(
            await LoadProfileResponseAsync(userId, cancellationToken).ConfigureAwait(false));
    }

    private async Task<HostUserProfileResponse?> LoadProfileResponseAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var record = (await queryExecutor.QueryAsync<HostUserProfileRecord>(
                IdentitySql.ListHostUserProfilesByIds,
                new { UserIds = new[] { userId } },
                cancellationToken)
            .ConfigureAwait(false)).FirstOrDefault();
        return HostUserProfileMapper.ToResponse(record);
    }

    private async Task<bool> IsActiveSuperAdministratorAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountActiveSuperAdministratorAssignment,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false) > 0;

    private static Result<HostUserResponse> Conflict() =>
        Result<HostUserResponse>.Failure(new Error(
            IdentityErrorCodes.UsernameExists,
            "A host user with this username already exists.",
            ErrorType.Conflict));

    private static Result<HostUserResponse> NotFound() =>
        Result<HostUserResponse>.Failure(new Error(
            IdentityErrorCodes.UserNotFound,
            "The host user was not found.",
            ErrorType.NotFound));

    private static Result<HostUserResponse> VersionConflict() =>
        Result<HostUserResponse>.Failure(new Error(
            IdentityErrorCodes.ProfileVersionConflict,
            "The host user was updated concurrently.",
            ErrorType.Conflict));

    private static Result<HostUserResponse> ValidationFailure(
        IReadOnlyList<IdentityPasswordPolicyViolation> violations) =>
        Result<HostUserResponse>.Failure(new Error(
            Code: ValidationErrorCodes.Failed,
            Message: "The password does not satisfy the password policy.",
            Type: ErrorType.Validation,
            ValidationErrors: new Dictionary<string, string[]>
            {
                [nameof(CreateHostUserRequest.Password)] = violations
                    .Select(violation => violation.DefaultMessage)
                    .ToArray(),
            },
            Arguments: null,
            ValidationViolations: violations
                .Select(violation => new ValidationViolation(
                    nameof(CreateHostUserRequest.Password),
                    violation.Code,
                    violation.Arguments))
                .ToArray()));
}