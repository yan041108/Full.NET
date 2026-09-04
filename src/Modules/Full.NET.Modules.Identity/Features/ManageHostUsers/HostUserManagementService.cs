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
    private const int MaxDeadlockRetryAttempts = 3;

    public Task<Result<HostUserResponse>> CreateAsync(
        CreateHostUserRequest request,
        IReadOnlyCollection<string>? allowedProfileFieldKeys = null,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(request, allowedProfileFieldKeys, token),
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

    /// <summary>
    /// 更新 Host 用户及其扩展资料；数据库回滚死锁事务后，有界重放完整事务单元。
    /// </summary>
    /// <param name="userId">待更新的 Host 用户标识。</param>
    /// <param name="request">用户基础资料与并发版本请求。</param>
    /// <param name="allowedProfileFieldKeys">当前调用方允许写入的扩展资料字段键集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的用户响应，或稳定的校验、并发及唯一性冲突结果。</returns>
    public async Task<Result<HostUserResponse>> UpdateAsync(
        Guid userId,
        UpdateHostUserRequest request,
        IReadOnlyCollection<string>? allowedProfileFieldKeys,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await transaction.ExecuteResultAsync(
                        token => UpdateCoreAsync(
                            userId,
                            request,
                            allowedProfileFieldKeys,
                            token),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DataCommandException exception)
                when (exception.Kind == DataCommandFailureKind.Deadlock
                      && attempt < MaxDeadlockRetryAttempts)
            {
                // Provider 已回滚死锁事务，必须从前置校验开始重放完整事务；短暂递增退避用于
                // 降低两个请求立即再次争用同一锁序列的概率，达到上限后保留原异常供统一诊断。
                await Task.Delay(
                        TimeSpan.FromMilliseconds(25 * attempt),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    public Task<Result<HostUserResponse>> ResetPasswordAsync(
        Guid userId,
        ResetHostUserPasswordRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ResetPasswordCoreAsync(userId, request, token),
            cancellationToken);

    /// <summary>逐行导入；超级管理员账号类型直接拒绝且不创建。</summary>
    public async Task<Result<ImportHostUsersResponse>> ImportAsync(
        ImportHostUsersRequest request,
        IReadOnlyCollection<string> allowedProfileFieldKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rows = request.Rows ?? [];
        var results = new List<ImportHostUserRowResult>(rows.Count);
        var succeeded = 0;
        var line = 0;
        foreach (var row in rows)
        {
            line++;
            if (row is null)
            {
                results.Add(new ImportHostUserRowResult(
                    line,
                    false,
                    null,
                    ValidationErrorCodes.Failed,
                    "Import row is required."));
                continue;
            }

            if (string.Equals(
                    row.AccountType?.Trim(),
                    IdentityAccountTypes.SuperAdmin,
                    StringComparison.Ordinal))
            {
                results.Add(new ImportHostUserRowResult(
                    line,
                    false,
                    null,
                    IdentityErrorCodes.SuperAdministratorImportRejected,
                    "Importing a super administrator is not allowed."));
                continue;
            }

            if (row.Profile is not null)
            {
                var requestedFieldKeys = HostUserProfileMapper.NormalizeFieldKeys(
                    row.Profile.FieldKeys);
                var allowedRequestedFieldKeys = HostUserProfileMapper.NormalizeFieldKeys(
                    row.Profile.FieldKeys,
                    allowedProfileFieldKeys);
                if (requestedFieldKeys.Count == 0
                    || requestedFieldKeys.Count != allowedRequestedFieldKeys.Count)
                {
                    results.Add(new ImportHostUserRowResult(
                        line,
                        false,
                        null,
                        CommonErrorCodes.PermissionDenied,
                        "Importing the requested profile fields is not allowed."));
                    continue;
                }
            }

            var created = await CreateAsync(row, allowedProfileFieldKeys, cancellationToken)
                .ConfigureAwait(false);
            if (created.IsSuccess)
            {
                succeeded++;
                results.Add(new ImportHostUserRowResult(
                    line,
                    true,
                    created.Value!.Id,
                    null,
                    null));
                continue;
            }

            results.Add(new ImportHostUserRowResult(
                line,
                false,
                null,
                created.Error?.Code,
                created.Error?.Message));
        }

        return Result<ImportHostUsersResponse>.Success(
            new ImportHostUsersResponse(succeeded, results));
    }

    /// <summary>逐个停用，复用最后一名超级管理员保护。</summary>
    public Task<Result<BatchHostUserStatusResponse>> BatchDisableAsync(
        BatchHostUserIdsRequest request,
        CancellationToken cancellationToken = default) =>
        BatchSetActiveAsync(request, disable: true, cancellationToken);

    /// <summary>逐个启用已停用账号。</summary>
    public Task<Result<BatchHostUserStatusResponse>> BatchEnableAsync(
        BatchHostUserIdsRequest request,
        CancellationToken cancellationToken = default) =>
        BatchSetActiveAsync(request, disable: false, cancellationToken);

    private async Task<Result<BatchHostUserStatusResponse>> BatchSetActiveAsync(
        BatchHostUserIdsRequest request,
        bool disable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userIds = request.UserIds ?? [];
        var results = new List<BatchHostUserStatusItem>(userIds.Count);
        var succeeded = 0;
        foreach (var userId in userIds)
        {
            var changed = disable
                ? await DisableAsync(userId, cancellationToken).ConfigureAwait(false)
                : await EnableAsync(userId, cancellationToken).ConfigureAwait(false);
            if (changed.IsSuccess)
            {
                succeeded++;
                results.Add(new BatchHostUserStatusItem(userId, true, null, null));
                continue;
            }

            results.Add(new BatchHostUserStatusItem(
                userId,
                false,
                changed.Error?.Code,
                changed.Error?.Message));
        }

        return Result<BatchHostUserStatusResponse>.Success(
            new BatchHostUserStatusResponse(succeeded, results));
    }

    private async Task<Result<HostUserResponse>> CreateCoreAsync(
        CreateHostUserRequest request,
        IReadOnlyCollection<string>? allowedProfileFieldKeys,
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

        var accountTypeResult = TryResolveAccountType(request.AccountType);
        if (!accountTypeResult.IsSuccess)
        {
            return Result<HostUserResponse>.Failure(accountTypeResult.Error!);
        }

        var accountType = accountTypeResult.Value ?? IdentityAccountTypes.NormalUser;

        var normalizedUsername = username.ToUpperInvariant();
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                IdentitySqlParameters.Create(("ScopeKey", HostScope), ("NormalizedUsername", normalizedUsername)),
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
        user = user with
        {
            PasswordHash = passwordHasher.HashPassword(user, password),
            AccountType = accountType,
        };
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
            user.ProfileVersion,
            user.AccountType);
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
                    allowedProfileFieldKeys,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!profileResult.IsSuccess)
            {
                return Result<HostUserResponse>.Failure(profileResult.Error!);
            }

            profileResponse = profileResult.Value;
        }

        return Result<HostUserResponse>.Success(
            MapHostUserResponse(user, profileResponse));
    }

    private async Task<Result<HostUserResponse>> DisableCoreAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", userId)),
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
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("SecurityStamp", idGenerator.NewId().ToString("N")),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (disabledRows != 1)
        {
            return NotFound();
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.RevokeAllUserSessions,
                IdentitySqlParameters.Create(("UserId", userId), ("RevokedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
        {
            return NotFound();
        }

        return Result<HostUserResponse>.Success(MapHostUserResponse(updated));
    }

    private async Task<Result<HostUserResponse>> EnableCoreAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || record.IsActive)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var enabledRows = await commandExecutor.ExecuteAsync(
                IdentitySql.EnableHostUser,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (enabledRows != 1)
        {
            return NotFound();
        }

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
        {
            return NotFound();
        }

        return Result<HostUserResponse>.Success(MapHostUserResponse(updated));
    }

    private async Task<Result<HostUserResponse>> UpdateCoreAsync(
        Guid userId,
        UpdateHostUserRequest request,
        IReadOnlyCollection<string>? allowedProfileFieldKeys,
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

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var accountTypeResult = TryResolveAccountType(
            request.AccountType,
            existing.AccountType);
        if (!accountTypeResult.IsSuccess)
        {
            return Result<HostUserResponse>.Failure(accountTypeResult.Error!);
        }

        var accountType = accountTypeResult.Value ?? IdentityAccountTypes.NormalUser;
        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateHostUserDisplayName,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("DisplayName", displayName),
                    ("AccountType", accountType),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            var exists = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                    IdentitySql.FindHostUserById,
                    IdentitySqlParameters.Create(("UserId", userId)),
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
                IdentitySqlParameters.Create(("UserId", userId)),
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
                    allowedProfileFieldKeys,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!profileResult.IsSuccess)
            {
                return Result<HostUserResponse>.Failure(profileResult.Error!);
            }

            profileResponse = profileResult.Value;
        }
        else if (HostUserProfileMapper.HasReadableFields(allowedProfileFieldKeys))
        {
            profileResponse = await LoadProfileResponseAsync(
                    userId,
                    allowedProfileFieldKeys,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<HostUserResponse>.Success(
            MapHostUserResponse(updated, profileResponse: profileResponse));
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
                IdentitySqlParameters.Create(("UserId", userId)),
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
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("PasswordHash", passwordHash),
                    ("SecurityStamp", securityStamp),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound();
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.RevokeAllUserSessions,
                IdentitySqlParameters.Create(("UserId", userId), ("RevokedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
        {
            return NotFound();
        }

        return Result<HostUserResponse>.Success(MapHostUserResponse(updated));
    }

    private async Task<Result<HostUserProfileResponse?>> UpsertProfileAsync(
        Guid userId,
        HostUserProfileWriteRequest profile,
        IReadOnlyCollection<string>? allowedProfileFieldKeys,
        CancellationToken cancellationToken)
    {
        var existing = (await queryExecutor.QueryAsync<HostUserProfileRecord>(
                IdentitySql.ListHostUserProfilesByIds,
                IdentitySqlParameters.Create(("UserIds", new[] { userId })),
                cancellationToken)
            .ConfigureAwait(false)).FirstOrDefault();
        var mergedProfile = HostUserProfileMapper.Merge(
            existing,
            profile,
            allowedProfileFieldKeys);
        var normalizedResult = HostUserProfilePolicy.NormalizeAndValidate(mergedProfile);
        if (!normalizedResult.IsSuccess)
        {
            return Result<HostUserProfileResponse?>.Failure(normalizedResult.Error!);
        }

        var normalizedProfile = normalizedResult.Value!;
        var existingConflict = await FindProfileConflictAsync(
                userId,
                normalizedProfile,
                cancellationToken)
            .ConfigureAwait(false);
        if (existingConflict is not null)
        {
            return Result<HostUserProfileResponse?>.Failure(existingConflict);
        }

        // 唯一索引是并发写入的最终仲裁者。禁止在写成功后再次扫描冲突行，否则两笔事务分别锁住
        // 不同资料行并争用同一唯一键时会形成锁顺序反转，SQL Server 可能把其中一笔选为死锁牺牲者。
        try
        {
            if (existing is null)
            {
                var inserted = await commandExecutor.ExecuteAsync(
                        IdentitySql.InsertHostUserProfile,
                        HostUserProfileMapper.ToParameters(userId, normalizedProfile),
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
                var affected = await commandExecutor.ExecuteAsync(
                        IdentitySql.UpdateHostUserProfile,
                        HostUserProfileMapper.ToParameters(userId, normalizedProfile),
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
        }
        catch (DataCommandException exception)
            when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            var racedConflict = await ResolveRacedProfileConflictAsync(
                    userId,
                    normalizedProfile,
                    cancellationToken)
                .ConfigureAwait(false);
            if (racedConflict is not null)
            {
                return Result<HostUserProfileResponse?>.Failure(racedConflict);
            }

            var mappedConflict = HostUserProfileUniqueConstraintMapper.TryMapConflict(
                exception,
                normalizedProfile);
            if (mappedConflict is not null)
            {
                return Result<HostUserProfileResponse?>.Failure(mappedConflict);
            }

            throw;
        }

        return Result<HostUserProfileResponse?>.Success(
            await LoadProfileResponseAsync(
                    userId,
                    allowedProfileFieldKeys,
                    cancellationToken)
                .ConfigureAwait(false));
    }

    private async Task<Error?> FindProfileConflictAsync(
        Guid userId,
        HostUserProfileWriteRequest profile,
        CancellationToken cancellationToken)
    {
        if (profile.PhoneNumber is null
            && profile.Email is null
            && profile.EmployeeNumber is null
            && profile.IdCardNumber is null)
        {
            return null;
        }

        var conflictKind = await queryExecutor.QuerySingleOrDefaultAsync<string>(
                IdentitySql.FindHostUserProfileConflictKind,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("PhoneNumber", profile.PhoneNumber),
                    ("Email", profile.Email),
                    ("EmployeeNumber", profile.EmployeeNumber),
                    ("IdCardType", profile.IdCardType),
                    ("IdCardNumber", profile.IdCardNumber)),
                cancellationToken)
            .ConfigureAwait(false);

        return conflictKind switch
        {
            "phone_number" => ProfileConflict(
                IdentityErrorCodes.UserPhoneNumberExists,
                "Phone number is already assigned to another host user."),
            "email" => ProfileConflict(
                IdentityErrorCodes.UserEmailExists,
                "Email is already assigned to another host user."),
            "employee_number" => ProfileConflict(
                IdentityErrorCodes.UserEmployeeNumberExists,
                "Employee number is already assigned to another host user."),
            "id_card" => ProfileConflict(
                IdentityErrorCodes.UserIdCardExists,
                "Identity document is already assigned to another host user."),
            _ => null,
        };
    }

    /// <summary>
    /// 在唯一约束竞态后重试读取冲突行；另一并发写入提交可能存在极短可见性窗口。
    /// </summary>
    private async Task<Error?> ResolveRacedProfileConflictAsync(
        Guid userId,
        HostUserProfileWriteRequest profile,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var conflict = await FindProfileConflictAsync(
                    userId,
                    profile,
                    cancellationToken)
                .ConfigureAwait(false);
            if (conflict is not null)
            {
                return conflict;
            }

            if (attempt < 9)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return null;
    }

    private static Error ProfileConflict(string code, string message) =>
        new(code, message, ErrorType.Conflict);

    private async Task<HostUserProfileResponse?> LoadProfileResponseAsync(
        Guid userId,
        IReadOnlyCollection<string>? allowedProfileFieldKeys,
        CancellationToken cancellationToken)
    {
        var record = (await queryExecutor.QueryAsync<HostUserProfileRecord>(
                IdentitySql.ListHostUserProfilesByIds,
                IdentitySqlParameters.Create(("UserIds", new[] { userId })),
                cancellationToken)
            .ConfigureAwait(false)).FirstOrDefault();
        return HostUserProfileMapper.ToResponse(record, allowedProfileFieldKeys);
    }

    private async Task<bool> IsActiveSuperAdministratorAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountActiveSuperAdministratorAssignment,
                IdentitySqlParameters.Create(("UserId", userId)),
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

    private static Result<string> TryResolveAccountType(
        string? requestedAccountType,
        string? existingAccountType = null)
    {
        if (string.IsNullOrWhiteSpace(requestedAccountType))
        {
            return Result<string>.Success(
                IdentityAccountTypes.NormalizeOrDefault(existingAccountType));
        }

        if (!IdentityAccountTypes.IsValid(requestedAccountType))
        {
            return Result<string>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Account type is invalid.",
                ErrorType.Validation));
        }

        return Result<string>.Success(requestedAccountType.Trim());
    }

    private static HostUserResponse MapHostUserResponse(
        IdentityUser user,
        HostUserProfileResponse? profileResponse = null) =>
        new(
            user.Id,
            user.Username,
            user.DisplayName,
            user.AccountType,
            user.IsActive,
            user.CreatedAtUtc,
            user.UpdatedAtUtc,
            user.Version,
            Profile: profileResponse);

    private static HostUserResponse MapHostUserResponse(
        IdentityUserRecord record,
        HostUserProfileResponse? profileResponse = null) =>
        new(
            record.Id,
            record.Username,
            record.DisplayName,
            record.AccountType,
            record.IsActive,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version,
            Profile: profileResponse);
}
