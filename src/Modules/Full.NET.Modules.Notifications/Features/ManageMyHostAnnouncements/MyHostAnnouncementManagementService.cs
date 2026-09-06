using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;

namespace Full.NET.Modules.Notifications.Features.ManageMyHostAnnouncements;

/// <summary>当前 Host 用户公告已读状态写入；重复标记保持幂等。</summary>
internal sealed class MyHostAnnouncementManagementService(
    ICommandExecutor commandExecutor,
    MyHostAnnouncementQueryService queries,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>将单条可见公告标记为已读。</summary>
    public async Task<Result<ReceivedHostAnnouncementDetailResponse>> MarkReadAsync(
        Guid userId,
        Guid announcementId,
        CancellationToken cancellationToken = default)
    {
        var existing = await queries.GetByIdAsync(userId, announcementId, cancellationToken)
            .ConfigureAwait(false);
        if (!existing.IsSuccess)
        {
            return existing;
        }

        if (existing.Value!.IsRead)
        {
            return existing;
        }

        await commandExecutor.ExecuteAsync(
                AnnouncementReadReceiptSql.InsertIfAbsent,
                new Dictionary<string, object?>
                {
                    ["Id"] = idGenerator.NewId(),
                    ["AnnouncementId"] = announcementId,
                    ["UserId"] = userId,
                    ["ReadAtUtc"] = clock.UtcNow,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return await queries.GetByIdAsync(userId, announcementId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>将当前可见的全部未读公告标记为已读。</summary>
    public async Task<Result<HostAnnouncementUnreadCountResponse>> MarkAllReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var page = 1;
        while (true)
        {
            var listed = await queries.ListAsync(
                    userId,
                    page,
                    100,
                    new ReceivedHostAnnouncementListFilter(IsRead: false),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!listed.IsSuccess)
            {
                return Result<HostAnnouncementUnreadCountResponse>.Failure(listed.Error!);
            }

            if (listed.Value!.Items.Count == 0)
            {
                break;
            }

            var now = clock.UtcNow;
            foreach (var item in listed.Value.Items)
            {
                await commandExecutor.ExecuteAsync(
                        AnnouncementReadReceiptSql.InsertIfAbsent,
                        new Dictionary<string, object?>
                        {
                            ["Id"] = idGenerator.NewId(),
                            ["AnnouncementId"] = item.Id,
                            ["UserId"] = userId,
                            ["ReadAtUtc"] = now,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (listed.Value.Items.Count < listed.Value.PageSize)
            {
                break;
            }

            page++;
        }

        return await queries.GetUnreadCountAsync(userId, cancellationToken)
            .ConfigureAwait(false);
    }
}
