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
    public Task<Result<HostAnnouncementResponse>> CreateAsync(
        Guid actorUserId,
        CreateHostAnnouncementRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    public Task<Result<HostAnnouncementResponse>> UpdateAsync(
        Guid actorUserId,
        Guid announcementId,
        UpdateHostAnnouncementRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(actorUserId, announcementId, request, token),
            cancellationToken);

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
                new
                {
                    Id = announcementId,
                    Title = request.Title.Trim(),
                    Content = request.Content.Trim(),
                    Status = AnnouncementStatuses.Draft,
                    CreatedAtUtc = now,
                    CreatedByUserId = actorUserId,
                    Version = 1,
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
                new { Id = announcementId },
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
                new
                {
                    Id = announcementId,
                    Title = request.Title.Trim(),
                    Content = request.Content.Trim(),
                    UpdatedAtUtc = now,
                    UpdatedByUserId = actorUserId,
                    NextVersion = request.Version + 1,
                    DraftStatus = AnnouncementStatuses.Draft,
                    Version = request.Version,
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
                new { Id = announcementId },
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
                new
                {
                    Id = announcementId,
                    PublishedStatus = AnnouncementStatuses.Published,
                    DraftStatus = AnnouncementStatuses.Draft,
                    PublishedAtUtc = now,
                    UpdatedAtUtc = now,
                    UpdatedByUserId = actorUserId,
                    NextVersion = version + 1,
                    Version = version,
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
