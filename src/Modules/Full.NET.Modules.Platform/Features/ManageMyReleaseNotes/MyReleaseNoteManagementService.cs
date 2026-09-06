using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Platform.Contracts;
using Full.NET.Modules.Platform.Persistence;

namespace Full.NET.Modules.Platform.Features.ManageMyReleaseNotes;

/// <summary>当前用户更新日志已读状态变更。</summary>
internal sealed class MyReleaseNoteManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>
    /// 将当前用户对一条已发布更新日志标记为已读；重复调用幂等返回当前事实。
    /// </summary>
    /// <param name="userId">当前用户标识。</param>
    /// <param name="releaseNoteId">更新日志标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>带已读状态的更新日志或稳定业务错误。</returns>
    public Task<Result<MyReleaseNoteResponse>> MarkReadAsync(
        Guid userId,
        Guid releaseNoteId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => MarkReadCoreAsync(userId, releaseNoteId, token),
            cancellationToken);

    private async Task<Result<MyReleaseNoteResponse>> MarkReadCoreAsync(
        Guid userId,
        Guid releaseNoteId,
        CancellationToken cancellationToken)
    {
        var published = await queryExecutor.QuerySingleOrDefaultAsync<MyReleaseNoteRecord>(
                ReleaseNoteSql.FindPublishedByIdForUser,
                ReleaseNoteSqlParameters.Create(
                    ("UserId", userId),
                    ("PublishedStatus", ReleaseNoteStatuses.Published),
                    ("ReleaseNoteId", releaseNoteId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (published is null)
        {
            return NotFound();
        }

        if (published.IsRead)
        {
            return Result<MyReleaseNoteResponse>.Success(MyReleaseNoteQueryService.Map(published));
        }

        var now = clock.UtcNow;
        try
        {
            await commandExecutor.ExecuteAsync(
                    ReleaseNoteSql.InsertRead,
                    ReleaseNoteSqlParameters.Create(
                        ("Id", idGenerator.NewId()),
                        ("ReleaseNoteId", releaseNoteId),
                        ("UserId", userId),
                        ("ReadAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUniqueReadViolation(exception))
        {
            // 并发重复标记已读时回读当前事实，保持幂等语义。
        }

        var updated = await queryExecutor.QuerySingleOrDefaultAsync<MyReleaseNoteRecord>(
                ReleaseNoteSql.FindPublishedByIdForUser,
                ReleaseNoteSqlParameters.Create(
                    ("UserId", userId),
                    ("PublishedStatus", ReleaseNoteStatuses.Published),
                    ("ReleaseNoteId", releaseNoteId)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<MyReleaseNoteResponse>.Success(MyReleaseNoteQueryService.Map(updated!));
    }

    private static bool IsUniqueReadViolation(Exception exception) =>
        exception.Message.Contains("UX_fn_platform_release_note_read_User_ReleaseNote", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase);

    private static Result<MyReleaseNoteResponse> NotFound() =>
        Result<MyReleaseNoteResponse>.Failure(new Error(
            PlatformErrorCodes.ReleaseNoteNotFound,
            "The release note was not found.",
            ErrorType.NotFound));
}
