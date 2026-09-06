using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Features.ManageOpenAccessClients;

/// <summary>OpenAccess 接入方应用创建、更新、停用与密钥轮换；明文密钥只在创建/轮换响应中返回一次。</summary>
internal sealed class OpenAccessClientManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    OpenAccessClientQueryService queries,
    AuthorizationCatalog catalog,
    IPermissionSnapshotReader permissionSnapshots,
    IRandomTokenGenerator tokenGenerator,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>应用名称允许的最大字符数。</summary>
    internal const int MaxNameLength = 128;

    /// <summary>应用描述允许的最大字符数。</summary>
    internal const int MaxDescriptionLength = 512;

    /// <summary>管理员备注允许的最大字符数。</summary>
    internal const int MaxRemarkLength = 256;

    private const string KeyPrefix = "fnoa_";

    /// <summary>创建接入方应用并签发一次性明文密钥。</summary>
    /// <param name="operatorUserId">操作人用户标识。</param>
    /// <param name="credentialPermissions">操作人凭据携带的权限集合。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建结果或稳定业务错误。</returns>
    public Task<Result<CreateOpenAccessClientResponse>> CreateAsync(
        Guid operatorUserId,
        IReadOnlyCollection<string> credentialPermissions,
        CreateOpenAccessClientRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(
                operatorUserId,
                credentialPermissions,
                request,
                token),
            cancellationToken);

    /// <summary>更新接入方应用元数据与权限绑定。</summary>
    /// <param name="operatorUserId">操作人用户标识。</param>
    /// <param name="credentialPermissions">操作人凭据携带的权限集合。</param>
    /// <param name="clientId">接入方应用标识。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的应用或稳定业务错误。</returns>
    public Task<Result<OpenAccessClientResponse>> UpdateAsync(
        Guid operatorUserId,
        IReadOnlyCollection<string> credentialPermissions,
        Guid clientId,
        UpdateOpenAccessClientRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(
                operatorUserId,
                credentialPermissions,
                clientId,
                request,
                token),
            cancellationToken);

    /// <summary>停用接入方应用；关联 API Key 立即失效。</summary>
    /// <param name="clientId">接入方应用标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>停用后的应用或稳定业务错误。</returns>
    public Task<Result<OpenAccessClientResponse>> DisableAsync(
        Guid clientId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(clientId, token),
            cancellationToken);

    /// <summary>轮换接入方应用密钥并返回一次性明文密钥。</summary>
    /// <param name="operatorUserId">操作人用户标识。</param>
    /// <param name="credentialPermissions">操作人凭据携带的权限集合。</param>
    /// <param name="clientId">接入方应用标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>轮换结果或稳定业务错误。</returns>
    public Task<Result<CreateOpenAccessClientResponse>> RotateAsync(
        Guid operatorUserId,
        IReadOnlyCollection<string> credentialPermissions,
        Guid clientId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RotateCoreAsync(
                operatorUserId,
                credentialPermissions,
                clientId,
                token),
            cancellationToken);

    private async Task<Result<CreateOpenAccessClientResponse>> CreateCoreAsync(
        Guid operatorUserId,
        IReadOnlyCollection<string> credentialPermissions,
        CreateOpenAccessClientRequest request,
        CancellationToken cancellationToken)
    {
        var metadataValidation = ValidateMetadata(
            request.Name,
            request.Description,
            request.Remark);
        if (!metadataValidation.IsSuccess)
        {
            return Result<CreateOpenAccessClientResponse>.Failure(metadataValidation.Error!);
        }

        var permissionResult = NormalizePermissions(request.Permissions);
        if (!permissionResult.IsSuccess)
        {
            return Result<CreateOpenAccessClientResponse>.Failure(permissionResult.Error!);
        }

        var user = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", request.UserId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return Result<CreateOpenAccessClientResponse>.Failure(new Error(
                IdentityErrorCodes.ApiKeyUserNotFound,
                "The host user was not found.",
                ErrorType.NotFound));
        }

        if (!user.IsActive)
        {
            return Result<CreateOpenAccessClientResponse>.Failure(new Error(
                IdentityErrorCodes.ApiKeyUserInactive,
                "The host user is inactive.",
                ErrorType.Conflict));
        }

        if (!await HasPermissionCeilingAsync(
                operatorUserId,
                request.UserId,
                credentialPermissions,
                permissionResult.Value!,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return Result<CreateOpenAccessClientResponse>.Failure(new Error(
                CommonErrorCodes.PermissionDenied,
                "OpenAccess client permissions cannot exceed the operator or target user permissions.",
                ErrorType.Forbidden));
        }

        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc <= clock.UtcNow)
        {
            return ValidationCreateFailure("Expiration must be in the future.");
        }

        var secret = $"{KeyPrefix}{tokenGenerator.Generate(32)}";
        var now = clock.UtcNow;
        var apiKeyId = idGenerator.NewId();
        var clientId = idGenerator.NewId();
        var apiKeyRecord = new ApiKeyRecord
        {
            Id = apiKeyId,
            UserId = request.UserId,
            DisplayName = metadataValidation.Value!.Name,
            KeyPrefix = secret[..Math.Min(secret.Length, 16)],
            KeyHash = TokenHash.Compute(secret),
            PermissionsJson = ApiKeyAuthenticationService.SerializePermissions(
                permissionResult.Value!),
            ExpiresAtUtc = request.ExpiresAtUtc,
            IsActive = true,
            CreatedAtUtc = now,
            Version = 1,
        };
        await commandExecutor.ExecuteAsync(
                ApiKeySql.Insert,
                apiKeyRecord,
                cancellationToken)
            .ConfigureAwait(false);

        var clientRecord = new OpenAccessClientRecord
        {
            Id = clientId,
            ApiKeyId = apiKeyId,
            Name = metadataValidation.Value!.Name,
            Description = metadataValidation.Value.Description,
            Remark = metadataValidation.Value.Remark,
            CreatedByUserId = operatorUserId,
            CreatedAtUtc = now,
            Version = 1,
        };
        await commandExecutor.ExecuteAsync(
                OpenAccessClientSql.Insert,
                clientRecord,
                cancellationToken)
            .ConfigureAwait(false);

        var response = OpenAccessClientQueryService.Map(new OpenAccessClientDetailRow
        {
            Id = clientId,
            ApiKeyId = apiKeyId,
            Name = clientRecord.Name,
            Description = clientRecord.Description,
            Remark = clientRecord.Remark,
            UserId = user.Id,
            Username = user.Username,
            AccessKeyId = apiKeyRecord.KeyPrefix,
            PermissionsJson = apiKeyRecord.PermissionsJson,
            ExpiresAtUtc = apiKeyRecord.ExpiresAtUtc,
            IsActive = true,
            CreatedAtUtc = now,
            Version = 1,
        });
        return Result<CreateOpenAccessClientResponse>.Success(
            new CreateOpenAccessClientResponse(response, secret));
    }

    private async Task<Result<OpenAccessClientResponse>> UpdateCoreAsync(
        Guid operatorUserId,
        IReadOnlyCollection<string> credentialPermissions,
        Guid clientId,
        UpdateOpenAccessClientRequest request,
        CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<OpenAccessClientDetailRow>(
                OpenAccessClientSql.FindById,
                IdentitySqlParameters.Create(("ClientId", clientId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null || !row.IsActive)
        {
            return NotFound();
        }

        var metadataValidation = ValidateMetadata(
            request.Name,
            request.Description,
            request.Remark);
        if (!metadataValidation.IsSuccess)
        {
            return Result<OpenAccessClientResponse>.Failure(metadataValidation.Error!);
        }

        var permissionResult = NormalizePermissions(request.Permissions);
        if (!permissionResult.IsSuccess)
        {
            return Result<OpenAccessClientResponse>.Failure(permissionResult.Error!);
        }

        if (!await HasPermissionCeilingAsync(
                operatorUserId,
                row.UserId,
                credentialPermissions,
                permissionResult.Value!,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return Result<OpenAccessClientResponse>.Failure(new Error(
                CommonErrorCodes.PermissionDenied,
                "OpenAccess client permissions cannot exceed the operator or target user permissions.",
                ErrorType.Forbidden));
        }

        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc <= clock.UtcNow)
        {
            return ValidationUpdateFailure("Expiration must be in the future.");
        }

        var now = clock.UtcNow;
        var metadataRows = await commandExecutor.ExecuteAsync(
                OpenAccessClientSql.UpdateMetadata,
                IdentitySqlParameters.Create(
                    ("ClientId", clientId),
                    ("Name", metadataValidation.Value!.Name),
                    ("Description", metadataValidation.Value.Description),
                    ("Remark", metadataValidation.Value.Remark),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (metadataRows < 1)
        {
            return VersionConflict();
        }

        var apiKeyRows = await commandExecutor.ExecuteAsync(
                OpenAccessClientSql.UpdateApiKeyPermissions,
                IdentitySqlParameters.Create(
                    ("ApiKeyId", row.ApiKeyId),
                    ("DisplayName", metadataValidation.Value.Name),
                    ("PermissionsJson", ApiKeyAuthenticationService.SerializePermissions(
                        permissionResult.Value!)),
                    ("ExpiresAtUtc", request.ExpiresAtUtc),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (apiKeyRows < 1)
        {
            return NotFound();
        }

        return await queries.GetByIdAsync(clientId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OpenAccessClientResponse>> DisableCoreAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<OpenAccessClientDetailRow>(
                OpenAccessClientSql.FindById,
                IdentitySqlParameters.Create(("ClientId", clientId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null || !row.IsActive)
        {
            return NotFound();
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                ApiKeySql.Disable,
                IdentitySqlParameters.Create(
                    ("ApiKeyId", row.ApiKeyId),
                    ("DisabledAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return NotFound();
        }

        return await queries.GetByIdAsync(clientId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<CreateOpenAccessClientResponse>> RotateCoreAsync(
        Guid operatorUserId,
        IReadOnlyCollection<string> credentialPermissions,
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<OpenAccessClientDetailRow>(
                OpenAccessClientSql.FindById,
                IdentitySqlParameters.Create(("ClientId", clientId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null || !row.IsActive)
        {
            return RotateNotFound();
        }

        var permissions = ApiKeyAuthenticationService.DeserializePermissions(row.PermissionsJson);
        if (!await HasPermissionCeilingAsync(
                operatorUserId,
                row.UserId,
                credentialPermissions,
                permissions,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return Result<CreateOpenAccessClientResponse>.Failure(new Error(
                CommonErrorCodes.PermissionDenied,
                "OpenAccess client permissions cannot exceed the operator or target user permissions.",
                ErrorType.Forbidden));
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                ApiKeySql.Disable,
                IdentitySqlParameters.Create(
                    ("ApiKeyId", row.ApiKeyId),
                    ("DisabledAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return RotateNotFound();
        }

        var secret = $"{KeyPrefix}{tokenGenerator.Generate(32)}";
        var now = clock.UtcNow;
        var apiKeyId = idGenerator.NewId();
        var apiKeyRecord = new ApiKeyRecord
        {
            Id = apiKeyId,
            UserId = row.UserId,
            DisplayName = row.Name,
            KeyPrefix = secret[..Math.Min(secret.Length, 16)],
            KeyHash = TokenHash.Compute(secret),
            PermissionsJson = row.PermissionsJson,
            ExpiresAtUtc = row.ExpiresAtUtc,
            IsActive = true,
            CreatedAtUtc = now,
            Version = 1,
        };
        await commandExecutor.ExecuteAsync(
                ApiKeySql.Insert,
                apiKeyRecord,
                cancellationToken)
            .ConfigureAwait(false);

        await commandExecutor.ExecuteAsync(
                OpenAccessClientSql.UpdateApiKeyLink,
                IdentitySqlParameters.Create(
                    ("ClientId", clientId),
                    ("ApiKeyId", apiKeyId),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        var response = OpenAccessClientQueryService.Map(new OpenAccessClientDetailRow
        {
            Id = row.Id,
            ApiKeyId = apiKeyId,
            Name = row.Name,
            Description = row.Description,
            Remark = row.Remark,
            UserId = row.UserId,
            Username = row.Username,
            AccessKeyId = apiKeyRecord.KeyPrefix,
            PermissionsJson = apiKeyRecord.PermissionsJson,
            ExpiresAtUtc = apiKeyRecord.ExpiresAtUtc,
            IsActive = true,
            CreatedAtUtc = row.CreatedAtUtc,
            Version = row.Version + 1,
        });
        return Result<CreateOpenAccessClientResponse>.Success(
            new CreateOpenAccessClientResponse(response, secret));
    }

    private async Task<bool> HasPermissionCeilingAsync(
        Guid operatorUserId,
        Guid targetUserId,
        IReadOnlyCollection<string> credentialPermissions,
        IReadOnlyCollection<string> requestedPermissions,
        CancellationToken cancellationToken)
    {
        var operatorSnapshot = await permissionSnapshots.ReadAsync(
                operatorUserId,
                "host",
                null,
                cancellationToken)
            .ConfigureAwait(false);
        var targetSnapshot = operatorUserId == targetUserId
            ? operatorSnapshot
            : await permissionSnapshots.ReadAsync(
                targetUserId,
                "host",
                null,
                cancellationToken)
            .ConfigureAwait(false);
        return HasPermissionCeiling(
            requestedPermissions,
            credentialPermissions,
            operatorSnapshot.Permissions,
            targetSnapshot.Permissions);
    }

    private Result<IReadOnlyList<string>> NormalizePermissions(
        IReadOnlyList<string>? permissions)
    {
        var normalized = (permissions ?? [])
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(permission => permission, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
        {
            return Result<IReadOnlyList<string>>.Failure(new Error(
                IdentityErrorCodes.ApiKeyInvalidPermissions,
                "At least one permission is required.",
                ErrorType.Validation));
        }

        var hostCodes = catalog.Permissions
            .Where(permission => permission.Scope.HasFlag(AuthorizationScope.Host))
            .Select(permission => permission.Code)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var code in normalized)
        {
            if (!hostCodes.Contains(code))
            {
                return Result<IReadOnlyList<string>>.Failure(new Error(
                    IdentityErrorCodes.ApiKeyInvalidPermissions,
                    "One or more permissions are invalid for OpenAccess clients.",
                    ErrorType.Validation));
            }
        }

        return Result<IReadOnlyList<string>>.Success(normalized);
    }

    internal static Result<ValidatedMetadata> ValidateMetadata(
        string? name,
        string? description,
        string? remark)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > MaxNameLength)
        {
            return Result<ValidatedMetadata>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Application name is invalid.",
                ErrorType.Validation));
        }

        var normalizedDescription = NormalizeOptional(description, MaxDescriptionLength);
        if (!normalizedDescription.IsSuccess)
        {
            return Result<ValidatedMetadata>.Failure(normalizedDescription.Error!);
        }

        var normalizedRemark = NormalizeOptional(remark, MaxRemarkLength);
        if (!normalizedRemark.IsSuccess)
        {
            return Result<ValidatedMetadata>.Failure(normalizedRemark.Error!);
        }

        return Result<ValidatedMetadata>.Success(new ValidatedMetadata(
            normalizedName,
            normalizedDescription.Value,
            normalizedRemark.Value));
    }

    private static Result<string?> NormalizeOptional(string? value, int maxLength)
    {
        if (value is null)
        {
            return Result<string?>.Success(null);
        }

        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return Result<string?>.Success(null);
        }

        if (normalized.Length > maxLength)
        {
            return Result<string?>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Field length exceeds the allowed limit.",
                ErrorType.Validation));
        }

        return Result<string?>.Success(normalized);
    }

    private static bool HasPermissionCeiling(
        IReadOnlyCollection<string> requestedPermissions,
        IReadOnlyCollection<string> credentialPermissions,
        IReadOnlyCollection<string> currentOperatorPermissions,
        IReadOnlyCollection<string> targetPermissions)
    {
        var credentialCodes = credentialPermissions.ToHashSet(StringComparer.Ordinal);
        var operatorCodes = currentOperatorPermissions.ToHashSet(StringComparer.Ordinal);
        var targetCodes = targetPermissions.ToHashSet(StringComparer.Ordinal);
        return requestedPermissions.All(code =>
            credentialCodes.Contains(code)
            && operatorCodes.Contains(code)
            && targetCodes.Contains(code));
    }

    private static Result<CreateOpenAccessClientResponse> ValidationCreateFailure(string message) =>
        Result<CreateOpenAccessClientResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));

    private static Result<OpenAccessClientResponse> ValidationUpdateFailure(string message) =>
        Result<OpenAccessClientResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));

    private static Result<OpenAccessClientResponse> NotFound() =>
        Result<OpenAccessClientResponse>.Failure(new Error(
            IdentityErrorCodes.OpenAccessClientNotFound,
            "The OpenAccess client was not found.",
            ErrorType.NotFound));

    private static Result<OpenAccessClientResponse> VersionConflict() =>
        Result<OpenAccessClientResponse>.Failure(new Error(
            IdentityErrorCodes.OpenAccessClientVersionConflict,
            "The OpenAccess client was modified by another request.",
            ErrorType.Conflict));

    private static Result<CreateOpenAccessClientResponse> RotateNotFound() =>
        Result<CreateOpenAccessClientResponse>.Failure(new Error(
            IdentityErrorCodes.OpenAccessClientNotFound,
            "The OpenAccess client was not found.",
            ErrorType.NotFound));

    internal sealed record ValidatedMetadata(
        string Name,
        string? Description,
        string? Remark);
}
