using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Features.Logout;

/// <summary>
/// 登出处理器。安全要点：
/// 1) 以 Refresh Token 哈希查找会话后，直接按 FamilyId 撤销整族 Session，
///    避免只撤销当前 Session 时 Refresh 并发获胜留下替代会话的窗口；
/// 2) 找不到 Session 时静默成功，符合 CSRF 场景与重复点击的幂等期望；
/// 3) 审计事件携带当前 ActiveTenantId，便于切换租户后的行为审计追踪。
/// </summary>
internal sealed class Handler(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator) : ICommandHandler<Command, LogoutResult>
{
    public async Task<Result<LogoutResult>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                IdentitySql.FindRefreshSessionByHash,
                IdentitySqlParameters.Create(("TokenHash", TokenHash.Compute(command.RefreshToken))),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<LogoutResult>.Success(new LogoutResult());
        }

        // Logout 必须撤销整个轮换 family；只撤销当前行会在 Refresh 并发获胜时遗留替代会话。
        await commandExecutor.ExecuteAsync(
            IdentitySql.RevokeRefreshFamily,
            IdentitySqlParameters.Create(("FamilyId", record.FamilyId), ("RevokedAtUtc", clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);

        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            record.UserId,
            record.SessionId,
            TokenHash.Compute(record.NormalizedUsername),
            "logout",
            "identity.logout-succeeded",
            true,
            Truncate(command.Client.IpAddress, 64),
            Truncate(command.Client.UserAgent, 512),
            record.ActiveTenantId,
            clock.UtcNow);
        var rows = await commandExecutor.ExecuteAsync(
                IdentitySql.InsertAuthAudit,
                audit,
                cancellationToken)
            .ConfigureAwait(false);
        if (rows != 1)
        {
            throw new InvalidOperationException(
                $"Identity logout audit insert affected {rows} rows instead of one.");
        }

        return Result<LogoutResult>.Success(new LogoutResult());
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
}
