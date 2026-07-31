using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Features.ManageHostApiKeys;

/// <summary>Host API Key 创建、禁用与轮换；明文密钥只在创建/轮换响应中返回一次。</summary>
internal sealed class HostApiKeyManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    AuthorizationCatalog catalog,
    IPermissionSnapshotReader permissionSnapshots,
    IRandomTokenGenerator tokenGenerator,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const string KeyPrefix = "fnk_";

    public Task<Result<CreateHostApiKeyResponse>> CreateAsync(
        Guid operatorUserId,
        IReadOnlyCollection<string> credentialPermissions,
        CreateHostApiKeyRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(
                operatorUserId,
                credentialPermissions,
                request,
                token),
            cancellationToken);

    public Task<Result<HostApiKeyResponse>> DisableAsync(
        Guid apiKeyId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(apiKeyId, token),
            cancellationToken);

    public Task<Result<CreateHostApiKeyResponse>> RotateAsync(
        Guid operatorUserId,
        IReadOnlyCollection<string> credentialPermissions,
        Guid apiKeyId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RotateCoreAsync(
                operatorUserId,
                credentialPermissions,
                apiKeyId,
                token),
            cancellationToken);

    private async Task<Result<CreateHostApiKeyResponse>> RotateCoreAsync(
        Guid operatorUserId,
        IReadOnlyCollection<string> credentialPermissions,
        Guid apiKeyId,
        CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<ApiKeyListRow>(
                ApiKeySql.FindById,
                new { ApiKeyId = apiKeyId },
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null || !row.IsActive)
        {
            return RotateNotFound();
        }

        var permissions = ApiKeyAuthenticationService.DeserializePermissions(
            row.PermissionsJson);
        var user = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = row.UserId },
                cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return Result<CreateHostApiKeyResponse>.Failure(new Error(
                IdentityErrorCodes.ApiKeyUserNotFound,
                "The host user was not found.",
                ErrorType.NotFound));
        }

        if (!user.IsActive)
        {
            return Result<CreateHostApiKeyResponse>.Failure(new Error(
                IdentityErrorCodes.ApiKeyUserInactive,
                "The host user is inactive.",
                ErrorType.Conflict));
        }

        var operatorSnapshot = await permissionSnapshots.ReadAsync(
                operatorUserId,
                "host",
                null,
                cancellationToken)
            .ConfigureAwait(false);
        var targetSnapshot = operatorUserId == row.UserId
            ? operatorSnapshot
            : await permissionSnapshots.ReadAsync(
                row.UserId,
                "host",
                null,
                cancellationToken)
            .ConfigureAwait(false);
        if (!HasPermissionCeiling(
                permissions,
                credentialPermissions,
                operatorSnapshot.Permissions,
                targetSnapshot.Permissions))
        {
            return Result<CreateHostApiKeyResponse>.Failure(new Error(
                CommonErrorCodes.PermissionDenied,
                "API key permissions cannot exceed the operator or target user permissions.",
                ErrorType.Forbidden));
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                ApiKeySql.Disable,
                new
                {
                    ApiKeyId = apiKeyId,
                    DisabledAtUtc = clock.UtcNow,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return RotateNotFound();
        }

        var secret = $"{KeyPrefix}{tokenGenerator.Generate(32)}";
        var now = clock.UtcNow;
        var record = new ApiKeyRecord
        {
            Id = idGenerator.NewId(),
            UserId = row.UserId,
            DisplayName = row.DisplayName,
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
                record,
                cancellationToken)
            .ConfigureAwait(false);

        var response = new HostApiKeyResponse(
            record.Id,
            record.UserId,
            user.Username,
            record.DisplayName,
            record.KeyPrefix,
            permissions,
            record.ExpiresAtUtc,
            record.IsActive,
            record.LastUsedAtUtc,
            record.CreatedAtUtc);
        return Result<CreateHostApiKeyResponse>.Success(
            new CreateHostApiKeyResponse(response, secret));
    }

    private async Task<Result<CreateHostApiKeyResponse>> CreateCoreAsync(
        Guid operatorUserId,
        IReadOnlyCollection<string> credentialPermissions,
        CreateHostApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is < 1 or > 128)
        {
            return ValidationFailure("Display name is invalid.");
        }

        var permissionResult = NormalizePermissions(request.Permissions);
        if (!permissionResult.IsSuccess)
        {
            return Result<CreateHostApiKeyResponse>.Failure(permissionResult.Error!);
        }

        var user = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = request.UserId },
                cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return Result<CreateHostApiKeyResponse>.Failure(new Error(
                IdentityErrorCodes.ApiKeyUserNotFound,
                "The host user was not found.",
                ErrorType.NotFound));
        }

        if (!user.IsActive)
        {
            return Result<CreateHostApiKeyResponse>.Failure(new Error(
                IdentityErrorCodes.ApiKeyUserInactive,
                "The host user is inactive.",
                ErrorType.Conflict));
        }

        var operatorSnapshot = await permissionSnapshots.ReadAsync(
                operatorUserId,
                "host",
                null,
                cancellationToken)
            .ConfigureAwait(false);
        var targetSnapshot = operatorUserId == request.UserId
            ? operatorSnapshot
            : await permissionSnapshots.ReadAsync(
                request.UserId,
                "host",
                null,
                cancellationToken)
            .ConfigureAwait(false);
        if (!HasPermissionCeiling(
                permissionResult.Value!,
                credentialPermissions,
                operatorSnapshot.Permissions,
                targetSnapshot.Permissions))
        {
            return Result<CreateHostApiKeyResponse>.Failure(new Error(
                CommonErrorCodes.PermissionDenied,
                "API key permissions cannot exceed the operator or target user permissions.",
                ErrorType.Forbidden));
        }

        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc <= clock.UtcNow)
        {
            return ValidationFailure("Expiration must be in the future.");
        }

        var secret = $"{KeyPrefix}{tokenGenerator.Generate(32)}";
        var now = clock.UtcNow;
        var record = new ApiKeyRecord
        {
            Id = idGenerator.NewId(),
            UserId = request.UserId,
            DisplayName = displayName,
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
                record,
                cancellationToken)
            .ConfigureAwait(false);

        var response = new HostApiKeyResponse(
            record.Id,
            record.UserId,
            user.Username,
            record.DisplayName,
            record.KeyPrefix,
            permissionResult.Value!,
            record.ExpiresAtUtc,
            record.IsActive,
            record.LastUsedAtUtc,
            record.CreatedAtUtc);
        return Result<CreateHostApiKeyResponse>.Success(
            new CreateHostApiKeyResponse(response, secret));
    }

    private async Task<Result<HostApiKeyResponse>> DisableCoreAsync(
        Guid apiKeyId,
        CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<ApiKeyListRow>(
                ApiKeySql.FindById,
                new { ApiKeyId = apiKeyId },
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null || !row.IsActive)
        {
            return NotFound();
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                ApiKeySql.Disable,
                new
                {
                    ApiKeyId = apiKeyId,
                    DisabledAtUtc = clock.UtcNow,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return NotFound();
        }

        return Result<HostApiKeyResponse>.Success(new HostApiKeyResponse(
            row.Id,
            row.UserId,
            row.Username,
            row.DisplayName,
            row.KeyPrefix,
            ApiKeyAuthenticationService.DeserializePermissions(row.PermissionsJson),
            row.ExpiresAtUtc,
            false,
            row.LastUsedAtUtc,
            row.CreatedAtUtc));
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
                    "One or more permissions are invalid for host API keys.",
                    ErrorType.Validation));
            }
        }

        return Result<IReadOnlyList<string>>.Success(normalized);
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

    private static Result<CreateHostApiKeyResponse> ValidationFailure(string message) =>
        Result<CreateHostApiKeyResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));

    private static Result<HostApiKeyResponse> NotFound() =>
        Result<HostApiKeyResponse>.Failure(new Error(
            IdentityErrorCodes.ApiKeyNotFound,
            "The API key was not found.",
            ErrorType.NotFound));

    private static Result<CreateHostApiKeyResponse> RotateNotFound() =>
        Result<CreateHostApiKeyResponse>.Failure(new Error(
            IdentityErrorCodes.ApiKeyNotFound,
            "The API key was not found.",
            ErrorType.NotFound));
}
