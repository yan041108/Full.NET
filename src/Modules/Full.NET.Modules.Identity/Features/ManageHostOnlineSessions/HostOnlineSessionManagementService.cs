using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;

/// <summary>Host 在线会话强制下线；撤销整个刷新令牌族以阻断后续轮换。</summary>
internal sealed class HostOnlineSessionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock)
{
    public Task<Result<HostOnlineSessionResponse>> RevokeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RevokeCoreAsync(sessionId, token),
            cancellationToken);

    private async Task<Result<HostOnlineSessionResponse>> RevokeCoreAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OnlineSessionRevokeRow>(
                OnlineSessionSql.FindActiveHostSessionById,
                new
                {
                    SessionId = sessionId,
                    NowUtc = clock.UtcNow,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var snapshot = new HostOnlineSessionResponse(
            record.SessionId,
            record.UserId,
            record.Username,
            record.DisplayName,
            record.ClientId,
            record.ActiveTenantId,
            record.CreatedAtUtc,
            record.ExpiresAtUtc);
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.RevokeRefreshFamily,
                new
                {
                    record.FamilyId,
                    RevokedAtUtc = clock.UtcNow,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return NotFound();
        }

        return Result<HostOnlineSessionResponse>.Success(snapshot);
    }

    private static Result<HostOnlineSessionResponse> NotFound() =>
        Result<HostOnlineSessionResponse>.Failure(new Error(
            IdentityErrorCodes.OnlineSessionNotFound,
            "The online session was not found.",
            ErrorType.NotFound));
}
