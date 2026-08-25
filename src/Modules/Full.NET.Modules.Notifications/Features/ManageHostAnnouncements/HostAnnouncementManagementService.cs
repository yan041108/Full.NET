using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;

/// <summary>Host 公告创建、更新与发布；提交后执行低延迟广播并由 Outbox 负责修复。</summary>
internal sealed class HostAnnouncementManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IOutboxWriter outboxWriter,
    HostAnnouncementQueryService queries,
    NotificationRealtimeDelivery realtimeDelivery,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<HostAnnouncementManagementService> logger)
{
    /// <summary>
    /// 创建一条处于草稿状态的 Host 公告；全程在命令事务内执行。
    /// </summary>
    /// <param name="actorUserId">触发创建操作的 Host 用户标识，用于审计。</param>
    /// <param name="request">公告标题与正文，长度经服务端校验。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    public Task<Result<HostAnnouncementResponse>> CreateAsync(
        Guid actorUserId,
        CreateHostAnnouncementRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    /// <summary>
    /// 更新未发布草稿公告的标题与正文，使用乐观版本号做 CAS 并发控制。
    /// </summary>
    /// <remarks>
    /// 仅 <c>Draft</c> 状态可更新；SQL 以 <c>Status = Draft AND Version = 期望值</c> 作为守卫，
    /// 影响行数为 0 时返回并发冲突而非静默覆盖。已发布公告不可再编辑。
    /// </remarks>
    public Task<Result<HostAnnouncementResponse>> UpdateAsync(
        Guid actorUserId,
        Guid announcementId,
        UpdateHostAnnouncementRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(actorUserId, announcementId, request, token),
            cancellationToken);

    /// <summary>
    /// 发布草稿公告：CAS 推进状态为已发布，并在同一事务内追加实时修复 Outbox 事件。
    /// </summary>
    /// <remarks>
    /// 事务提交成功后再尝试低延迟广播；广播失败仅告警，不影响已提交事实，
    /// 最终一致性由 Outbox 消费者保证。CAS 失败（版本或状态不符）返回并发/状态错误。
    /// </remarks>
    public async Task<Result<HostAnnouncementResponse>> PublishAsync(
        Guid actorUserId,
        Guid announcementId,
        int version,
        CancellationToken cancellationToken = default)
    {
        var result = await transaction.ExecuteAsync(
                token => PublishCoreAsync(actorUserId, announcementId, version, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await TryPublishAnnouncementAsync(result.Value!, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }

    private async Task<Result<HostAnnouncementResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateHostAnnouncementRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateDraftContent(request.Title, request.Content);
        if (validation is not null)
        {
            return validation;
        }

        var now = clock.UtcNow;
        var announcementId = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                AnnouncementSql.Insert,
                new Dictionary<string, object?>
                {
                    ["Id"] = announcementId,
                    ["Title"] = request.Title.Trim(),
                    ["Content"] = request.Content.Trim(),
                    ["Status"] = AnnouncementStatuses.Draft,
                    ["CreatedAtUtc"] = now,
                    ["CreatedByUserId"] = actorUserId,
                    ["Version"] = 1,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(announcementId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostAnnouncementResponse>> UpdateCoreAsync(
        Guid actorUserId,
        Guid announcementId,
        UpdateHostAnnouncementRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateDraftContent(request.Title, request.Content);
        if (validation is not null)
        {
            return validation;
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<AnnouncementRecord>(
                AnnouncementSql.FindHostById,
                new Dictionary<string, object?> { ["Id"] = announcementId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (!string.Equals(existing.Status, AnnouncementStatuses.Draft, StringComparison.Ordinal))
        {
            return InvalidStatus();
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                AnnouncementSql.UpdateDraft,
                new Dictionary<string, object?>
                {
                    ["Id"] = announcementId,
                    ["Title"] = request.Title.Trim(),
                    ["Content"] = request.Content.Trim(),
                    ["UpdatedAtUtc"] = now,
                    ["UpdatedByUserId"] = actorUserId,
                    ["NextVersion"] = request.Version + 1,
                    ["DraftStatus"] = AnnouncementStatuses.Draft,
                    ["Version"] = request.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyConflict();
        }

        return await queries.GetByIdAsync(announcementId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostAnnouncementResponse>> PublishCoreAsync(
        Guid actorUserId,
        Guid announcementId,
        int version,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<AnnouncementRecord>(
                AnnouncementSql.FindHostById,
                new Dictionary<string, object?> { ["Id"] = announcementId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (!string.Equals(existing.Status, AnnouncementStatuses.Draft, StringComparison.Ordinal))
        {
            return InvalidStatus();
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                AnnouncementSql.Publish,
                new Dictionary<string, object?>
                {
                    ["Id"] = announcementId,
                    ["PublishedStatus"] = AnnouncementStatuses.Published,
                    ["DraftStatus"] = AnnouncementStatuses.Draft,
                    ["PublishedAtUtc"] = now,
                    ["UpdatedAtUtc"] = now,
                    ["UpdatedByUserId"] = actorUserId,
                    ["NextVersion"] = version + 1,
                    ["Version"] = version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyConflict();
        }

        await outboxWriter.AddAsync(
                NotificationRealtimeEventTypes.AnnouncementPublished,
                1,
                new AnnouncementPublishedIntegrationEvent(
                    announcementId,
                    existing.Title),
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(announcementId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task TryPublishAnnouncementAsync(
        HostAnnouncementResponse announcement,
        CancellationToken cancellationToken)
    {
        try
        {
            await realtimeDelivery.PublishAnnouncementAsync(
                    new AnnouncementPublishedIntegrationEvent(
                        announcement.Id,
                        announcement.Title),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to publish announcement {AnnouncementId} after the database commit.",
                announcement.Id);
        }
    }

    private static Result<HostAnnouncementResponse>? ValidateDraftContent(string title, string content)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        var normalizedContent = content?.Trim() ?? string.Empty;
        if (normalizedTitle.Length is < 1 or > 200)
        {
            return ValidationFailure("Announcement title must be between 1 and 200 characters.");
        }

        if (normalizedContent.Length is < 1 or > 4000)
        {
            return ValidationFailure("Announcement content must be between 1 and 4000 characters.");
        }

        return null;
    }

    private static Result<HostAnnouncementResponse> ValidationFailure(string message) =>
        Result<HostAnnouncementResponse>.Failure(new Error(
            NotificationsErrorCodes.AnnouncementValidationFailed,
            message,
            ErrorType.Validation));

    private static Result<HostAnnouncementResponse> NotFound() =>
        Result<HostAnnouncementResponse>.Failure(new Error(
            NotificationsErrorCodes.AnnouncementNotFound,
            "The host announcement was not found.",
            ErrorType.NotFound));

    private static Result<HostAnnouncementResponse> ConcurrencyConflict() =>
        Result<HostAnnouncementResponse>.Failure(new Error(
            NotificationsErrorCodes.AnnouncementConcurrencyConflict,
            "The host announcement changed concurrently.",
            ErrorType.Conflict));

    private static Result<HostAnnouncementResponse> InvalidStatus() =>
        Result<HostAnnouncementResponse>.Failure(new Error(
            NotificationsErrorCodes.AnnouncementInvalidStatus,
            "Only draft announcements can be updated or published.",
            ErrorType.Validation));
}
