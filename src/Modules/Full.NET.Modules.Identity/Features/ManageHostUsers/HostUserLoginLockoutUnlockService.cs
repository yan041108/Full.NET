using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

/// <summary>管理员解除 Host 用户登录锁定；仅清除失败计数与锁定截止时间，不修改账号启用状态。</summary>
internal sealed class HostUserLoginLockoutUnlockService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const string UnlockAuditEventType = "host_user.login_lockout_unlocked";

    /// <summary>解除指定 Host 用户的登录锁定并写入认证审计。</summary>
    /// <param name="actorUserId">执行解锁的管理员用户标识。</param>
    /// <param name="targetUserId">待解除锁定的 Host 用户标识。</param>
    /// <param name="ipAddress">请求来源 IP，用于审计。</param>
    /// <param name="userAgent">请求 User-Agent，用于审计。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Result<HostUserResponse>> UnlockAsync(
        Guid actorUserId,
        Guid targetUserId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", targetUserId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (!record.IsActive)
        {
            return Result<HostUserResponse>.Failure(new Error(
                IdentityErrorCodes.UnlockInactiveUserRejected,
                "Disabled host users must be enabled before login lockout can be cleared.",
                ErrorType.BusinessRule));
        }

        if (!HasLoginLockoutState(record))
        {
            return Result<HostUserResponse>.Failure(new Error(
                IdentityErrorCodes.LoginNotLocked,
                "The host user is not currently in a login lockout state.",
                ErrorType.BusinessRule));
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.ClearHostUserLoginLockout,
                IdentitySqlParameters.Create(
                    ("UserId", targetUserId),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return Result<HostUserResponse>.Failure(new Error(
                IdentityErrorCodes.LoginNotLocked,
                "The host user is not currently in a login lockout state.",
                ErrorType.BusinessRule));
        }

        await WriteAuditAsync(
                actorUserId,
                targetUserId,
                ipAddress,
                userAgent,
                cancellationToken)
            .ConfigureAwait(false);

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", targetUserId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
        {
            return NotFound();
        }

        return Result<HostUserResponse>.Success(MapHostUserResponse(updated));
    }

    /// <summary>判断用户是否仍存在需要管理员清除的登录锁定痕迹。</summary>
    internal static bool HasLoginLockoutState(IdentityUserRecord record) =>
        record.FailedLoginCount > 0 || record.LockoutEndUtc is not null;

    private async Task WriteAuditAsync(
        Guid actorUserId,
        Guid targetUserId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            actorUserId,
            SessionId: null,
            UsernameFingerprint: string.Empty,
            EventType: UnlockAuditEventType,
            ResultCode: $"target:{targetUserId:D}",
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

    private static HostUserResponse MapHostUserResponse(IdentityUserRecord record) =>
        new(
            record.Id,
            record.Username,
            record.DisplayName,
            record.AccountType,
            record.IsActive,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version,
            Profile: null);

    private static Result<HostUserResponse> NotFound() =>
        Result<HostUserResponse>.Failure(new Error(
            IdentityErrorCodes.UserNotFound,
            "The host user was not found.",
            ErrorType.NotFound));

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength
            ? value
            : value[..maxLength];
}
