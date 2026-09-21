using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Full.NET.Modules.Identity.FieldProjection;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Validation;

namespace Full.NET.Modules.Identity.Features.SelfServiceProfile;

/// <summary>当前用户自助档案读取与写入；复用 Host 档案权威校验，敏感字段始终掩码展示。</summary>
internal sealed class SelfServiceProfileService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IUserFieldProjectionResolver projectionResolver,
    IClock clock,
    ICurrentTenantContextWriter currentTenantWriter)
{
    /// <summary>读取当前 Host 用户的自助档案快照。</summary>
    public Task<Result<SelfServiceProfileResponse>> GetAsync(
        Guid userId,
        string actorScope,
        CancellationToken cancellationToken = default) =>
        IdentityHostExecutionScope.RunAsync(
            currentTenantWriter,
            () => GetCoreAsync(userId, actorScope, cancellationToken));

    private async Task<Result<SelfServiceProfileResponse>> GetCoreAsync(
        Guid userId,
        string actorScope,
        CancellationToken cancellationToken)
    {
        if (!SelfServiceProfilePolicy.IsHostActorScope(actorScope))
        {
            return HostOnlyFailure<SelfServiceProfileResponse>();
        }

        var user = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (user is not { IsActive: true })
        {
            return Unauthorized<SelfServiceProfileResponse>();
        }

        var projection = await projectionResolver.ResolveAsync(
                userId,
                tenantId: null,
                FieldProjectionResourceKeys.HostUsers,
                cancellationToken)
            .ConfigureAwait(false);
        var readableFieldKeys = HostUserProfileMapper
            .GetReadableFieldKeys(projection.FieldKeys)
            .ToArray();
        var writableFieldKeys = SelfServiceProfilePolicy
            .GetWritableProfileFieldKeys(projection.FieldKeys);
        var profileRecord = (await queryExecutor.QueryAsync<HostUserProfileRecord>(
                    IdentitySql.ListHostUserProfilesByIds,
                    IdentitySqlParameters.Create(("UserIds", new[] { userId })),
                    cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault();
        var profile = HostUserProfileMapper.ToResponse(
            profileRecord,
            readableFieldKeys,
            revealAccess: default);

        return Result<SelfServiceProfileResponse>.Success(new SelfServiceProfileResponse(
            user.Id,
            user.Username,
            user.DisplayName,
            user.AccountType,
            user.Version,
            readableFieldKeys,
            writableFieldKeys,
            profileRecord?.AvatarFileId,
            profileRecord?.SignatureFileId,
            profile));
    }

    /// <summary>按自助边界更新展示名称与/或扩展档案。</summary>
    public Task<Result<SelfServiceProfileResponse>> UpdateAsync(
        Guid userId,
        string actorScope,
        UpdateSelfServiceProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IdentityHostExecutionScope.RunAsync(
            currentTenantWriter,
            () => UpdateCoreAsync(userId, actorScope, request, cancellationToken));
    }

    private async Task<Result<SelfServiceProfileResponse>> UpdateCoreAsync(
        Guid userId,
        string actorScope,
        UpdateSelfServiceProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!SelfServiceProfilePolicy.IsHostActorScope(actorScope))
        {
            return HostOnlyFailure<SelfServiceProfileResponse>();
        }

        var hasDisplayNamePatch = request.DisplayName is not null;
        var hasProfilePatch = request.Profile is not null;
        if (!hasDisplayNamePatch && !hasProfilePatch)
        {
            return ValidationFailure<SelfServiceProfileResponse>(
                "At least one of displayName or profile must be provided.");
        }

        var user = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (user is not { IsActive: true })
        {
            return Unauthorized<SelfServiceProfileResponse>();
        }

        var projection = await projectionResolver.ResolveAsync(
                userId,
                tenantId: null,
                FieldProjectionResourceKeys.HostUsers,
                cancellationToken)
            .ConfigureAwait(false);
        var writableFieldKeys = SelfServiceProfilePolicy
            .GetWritableProfileFieldKeys(projection.FieldKeys);

        if (hasDisplayNamePatch)
        {
            var displayNameResult = await UpdateDisplayNameAsync(
                    userId,
                    request.DisplayName!,
                    request.UserVersion,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!displayNameResult.IsSuccess)
            {
                return Result<SelfServiceProfileResponse>.Failure(displayNameResult.Error!);
            }

            user = displayNameResult.Value!;
        }

        if (hasProfilePatch)
        {
            var profileResult = await UpsertProfileAsync(
                    userId,
                    request.Profile!,
                    writableFieldKeys,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!profileResult.IsSuccess)
            {
                return Result<SelfServiceProfileResponse>.Failure(profileResult.Error!);
            }
        }

        return await GetAsync(userId, actorScope, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<IdentityUserRecord>> UpdateDisplayNameAsync(
        Guid userId,
        string displayName,
        int? userVersion,
        CancellationToken cancellationToken)
    {
        var normalized = displayName.Trim();
        if (normalized.Length is < 1 or > 128)
        {
            return ValidationFailure<IdentityUserRecord>("Display name is invalid.");
        }

        if (userVersion is not int version || version < 0)
        {
            return ValidationFailure<IdentityUserRecord>("User version is required.");
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateSelfServiceDisplayName,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("DisplayName", normalized),
                    ("UpdatedAtUtc", now),
                    ("Version", version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            var exists = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                    IdentitySql.FindHostUserById,
                    IdentitySqlParameters.Create(("UserId", userId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (exists is not { IsActive: true })
            {
                return Unauthorized<IdentityUserRecord>();
            }

            return Result<IdentityUserRecord>.Failure(new Error(
                IdentityErrorCodes.ProfileVersionConflict,
                "The account profile was updated concurrently.",
                ErrorType.Conflict));
        }

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
        {
            return Unauthorized<IdentityUserRecord>();
        }

        return Result<IdentityUserRecord>.Success(updated);
    }

    private async Task<Result<HostUserProfileResponse?>> UpsertProfileAsync(
        Guid userId,
        HostUserProfileWriteRequest profile,
        IReadOnlyCollection<string> allowedProfileFieldKeys,
        CancellationToken cancellationToken)
    {
        var readOnlyError = SelfServiceProfilePolicy.ValidateNoReadOnlyFieldsInPatch(
            profile.FieldKeys);
        if (readOnlyError is not null)
        {
            return Result<HostUserProfileResponse?>.Failure(readOnlyError);
        }

        var existing = (await queryExecutor.QueryAsync<HostUserProfileRecord>(
                    IdentitySql.ListHostUserProfilesByIds,
                    IdentitySqlParameters.Create(("UserIds", new[] { userId })),
                    cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault();
        var mergedProfile = HostUserProfileMapper.Merge(
            existing,
            profile,
            allowedProfileFieldKeys);
        var maskedValueError = HostUserProfileMapper.ValidateWritableSensitiveValues(
            mergedProfile.FieldKeys ?? [],
            mergedProfile);
        if (maskedValueError is not null)
        {
            return Result<HostUserProfileResponse?>.Failure(new Error(
                maskedValueError,
                "Masked profile values cannot be written back to the authoritative store.",
                ErrorType.Validation));
        }

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

        var readableFieldKeys = HostUserProfileMapper
            .GetReadableFieldKeys(allowedProfileFieldKeys)
            .ToArray();
        var record = (await queryExecutor.QueryAsync<HostUserProfileRecord>(
                    IdentitySql.ListHostUserProfilesByIds,
                    IdentitySqlParameters.Create(("UserIds", new[] { userId })),
                    cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault();
        return Result<HostUserProfileResponse?>.Success(
            HostUserProfileMapper.ToResponse(record, readableFieldKeys, revealAccess: default));
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

    private static Result<T> HostOnlyFailure<T>() =>
        Result<T>.Failure(new Error(
            IdentityErrorCodes.SelfServiceProfileHostOnly,
            "Self-service profile is only available in the host actor scope.",
            ErrorType.Forbidden));

    private static Result<T> Unauthorized<T>() =>
        Result<T>.Failure(new Error(
            IdentityErrorCodes.SessionNotActive,
            "The current session is no longer active.",
            ErrorType.Unauthorized));

    private static Result<T> ValidationFailure<T>(string message) =>
        Result<T>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));
}
