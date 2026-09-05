using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

/// <summary>按独立揭示权限返回手机号/证件号明文，并写入认证审计。</summary>
internal sealed class HostUserSensitiveFieldRevealService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IPermissionSnapshotReader permissionSnapshots,
    IUserFieldProjectionResolver projectionResolver,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const string HostScope = "host";
    private const string RevealAuditEventType = "host_user.profile_sensitive_field_revealed";

    /// <summary>揭示指定用户的敏感档案字段明文。</summary>
    public async Task<Result<RevealHostUserProfileFieldsResponse>> RevealAsync(
        Guid actorUserId,
        Guid targetUserId,
        RevealHostUserProfileFieldsRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        if (request.FieldKeys is not { Count: > 0 }
            || request.FieldKeys.Any(fieldKey => string.IsNullOrWhiteSpace(fieldKey)))
        {
            return ValidationFailure("Field keys are required.");
        }

        var normalizedKeys = request.FieldKeys
            .Select(fieldKey => fieldKey.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedKeys.Any(fieldKey =>
                fieldKey is not ("phone_number" or "id_card_number")))
        {
            return ValidationFailure("Only phone_number and id_card_number can be revealed.");
        }

        var targetExists = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", targetUserId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (targetExists is null)
        {
            return NotFound();
        }

        var projection = await projectionResolver.ResolveAsync(
                actorUserId,
                tenantId: null,
                FieldProjectionResourceKeys.HostUsers,
                cancellationToken)
            .ConfigureAwait(false);
        var readableKeys = HostUserProfileMapper.GetReadableFieldKeys(projection.FieldKeys)
            .ToHashSet(StringComparer.Ordinal);
        if (normalizedKeys.Any(fieldKey => !readableKeys.Contains(fieldKey)))
        {
            return Forbidden();
        }

        var permissions = await permissionSnapshots.ReadAsync(
                actorUserId,
                HostScope,
                tenantId: null,
                cancellationToken)
            .ConfigureAwait(false);
        var revealAccess = HostUserSensitiveFieldRevealAccess.FromPermissions(permissions.Permissions);
        if (normalizedKeys.Contains("phone_number", StringComparer.Ordinal)
            && !revealAccess.CanRevealPhoneNumber
            || normalizedKeys.Contains("id_card_number", StringComparer.Ordinal)
            && !revealAccess.CanRevealIdCardNumber)
        {
            return Forbidden();
        }

        var columnMap = HostUserProfileMapper.GetReadableColumnMap(projection.FieldKeys);
        var statement = IdentitySql.BuildProjectedHostUserProfilesByIds(columnMap.Values.ToArray());
        var record = await queryExecutor.QuerySingleOrDefaultAsync<HostUserProfileRecord>(
                statement,
                IdentitySqlParameters.Create(("UserIds", new[] { targetUserId })),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var fieldKey in normalizedKeys)
        {
            values[fieldKey] = fieldKey switch
            {
                "phone_number" => record.PhoneNumber,
                "id_card_number" => record.IdCardNumber,
                _ => null,
            };
        }

        await WriteAuditAsync(
                actorUserId,
                targetUserId,
                normalizedKeys,
                ipAddress,
                userAgent,
                cancellationToken)
            .ConfigureAwait(false);

        return Result<RevealHostUserProfileFieldsResponse>.Success(
            new RevealHostUserProfileFieldsResponse(values));
    }

    private async Task WriteAuditAsync(
        Guid actorUserId,
        Guid targetUserId,
        IReadOnlyList<string> fieldKeys,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            actorUserId,
            SessionId: null,
            UsernameFingerprint: string.Empty,
            EventType: RevealAuditEventType,
            ResultCode: $"target:{targetUserId:D};fields:{string.Join(',', fieldKeys)}",
            Succeeded: true,
            IpAddress: Truncate(ipAddress, 64),
            UserAgent: Truncate(userAgent, 512),
            ContextTenantId: null,
            clock.UtcNow);
        await commandExecutor.ExecuteAsync(
                IdentitySql.InsertAuthAudit,
                IdentitySqlParameters.Create(
                    ("Id", audit.Id),
                    ("UserId", audit.UserId),
                    ("SessionId", audit.SessionId),
                    ("UsernameFingerprint", audit.UsernameFingerprint),
                    ("EventType", audit.EventType),
                    ("ResultCode", audit.ResultCode),
                    ("Succeeded", audit.Succeeded),
                    ("IpAddress", audit.IpAddress),
                    ("UserAgent", audit.UserAgent),
                    ("ContextTenantId", audit.ContextTenantId),
                    ("OccurredAtUtc", audit.OccurredAtUtc)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static Result<RevealHostUserProfileFieldsResponse> ValidationFailure(string message) =>
        Result<RevealHostUserProfileFieldsResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));

    private static Result<RevealHostUserProfileFieldsResponse> Forbidden() =>
        Result<RevealHostUserProfileFieldsResponse>.Failure(new Error(
            CommonErrorCodes.PermissionDenied,
            "The caller is not allowed to reveal the requested profile fields.",
            ErrorType.Forbidden));

    private static Result<RevealHostUserProfileFieldsResponse> NotFound() =>
        Result<RevealHostUserProfileFieldsResponse>.Failure(new Error(
            IdentityErrorCodes.UserNotFound,
            "The host user was not found.",
            ErrorType.NotFound));
}
