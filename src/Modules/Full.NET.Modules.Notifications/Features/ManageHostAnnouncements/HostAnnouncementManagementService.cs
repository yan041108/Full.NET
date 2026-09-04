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

/// <summary>Host 公告创建、更新、发布与撤回；提交后执行低延迟广播并由 Outbox 负责修复。</summary>
internal sealed class HostAnnouncementManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IOutboxWriter outboxWriter,
    HostAnnouncementQueryService queries,
    HostAnnouncementAudienceValidator audienceValidator,
    NotificationRealtimeDelivery realtimeDelivery,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<HostAnnouncementManagementService> logger)
{
    /// <summary>创建一条处于草稿状态的 Host 公告。</summary>
    public Task<Result<HostAnnouncementResponse>> CreateAsync(
        Guid actorUserId,
        CreateHostAnnouncementRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    /// <summary>更新未发布草稿公告；使用乐观版本号做 CAS 并发控制。</summary>
    public Task<Result<HostAnnouncementResponse>> UpdateAsync(
        Guid actorUserId,
        Guid announcementId,
        UpdateHostAnnouncementRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(actorUserId, announcementId, request, token),
            cancellationToken);

    /// <summary>发布草稿公告；已发布时幂等返回当前事实。</summary>
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
        if (result.IsSuccess
            && string.Equals(result.Value!.Status, AnnouncementStatuses.Published, StringComparison.Ordinal))
        {
            await TryPublishAnnouncementAsync(result.Value!, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>撤回已发布公告；已撤回时幂等返回当前事实。</summary>
    public Task<Result<HostAnnouncementResponse>> RetractAsync(
        Guid actorUserId,
        Guid announcementId,
        int version,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RetractCoreAsync(actorUserId, announcementId, version, token),
            cancellationToken);

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

        var audience = await audienceValidator.ValidateAsync(
                request.Kind,
                request.AudienceKind,
                request.TargetUserIds,
                request.TargetOrganizations,
                cancellationToken)
            .ConfigureAwait(false);
        if (!audience.IsSuccess)
        {
            return Result<HostAnnouncementResponse>.Failure(audience.Error!);
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
                    ["Kind"] = audience.Value!.Kind,
                    ["AudienceKind"] = audience.Value.AudienceKind,
                    ["Status"] = AnnouncementStatuses.Draft,
                    ["CreatedAtUtc"] = now,
                    ["CreatedByUserId"] = actorUserId,
                    ["Version"] = 1,
                },
                cancellationToken)
            .ConfigureAwait(false);
        await SyncTargetsAsync(
                announcementId,
                audience.Value,
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
            return InvalidStatus("Only draft announcements can be updated.");
        }

        var existingTargets = await LoadExistingTargetsAsync(announcementId, cancellationToken)
            .ConfigureAwait(false);
        var audience = await audienceValidator.ValidateAsync(
                request.Kind ?? existing.Kind,
                request.AudienceKind ?? existing.AudienceKind,
                request.TargetUserIds ?? existingTargets.TargetUserIds,
                request.TargetOrganizations ?? existingTargets.TargetOrganizations,
                cancellationToken)
            .ConfigureAwait(false);
        if (!audience.IsSuccess)
        {
            return Result<HostAnnouncementResponse>.Failure(audience.Error!);
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                AnnouncementSql.UpdateDraft,
                new Dictionary<string, object?>
                {
                    ["Id"] = announcementId,
                    ["Title"] = request.Title.Trim(),
                    ["Content"] = request.Content.Trim(),
                    ["Kind"] = audience.Value!.Kind,
                    ["AudienceKind"] = audience.Value.AudienceKind,
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

        await ReplaceTargetsAsync(
                announcementId,
                audience.Value,
                cancellationToken)
            .ConfigureAwait(false);

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

        if (string.Equals(existing.Status, AnnouncementStatuses.Published, StringComparison.Ordinal))
        {
            return await queries.GetByIdAsync(announcementId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.Equals(existing.Status, AnnouncementStatuses.Draft, StringComparison.Ordinal))
        {
            return InvalidStatus("Only draft announcements can be published.");
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
                    ["PublishedByUserId"] = actorUserId,
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

    private async Task<Result<HostAnnouncementResponse>> RetractCoreAsync(
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

        if (string.Equals(existing.Status, AnnouncementStatuses.Retracted, StringComparison.Ordinal))
        {
            return await queries.GetByIdAsync(announcementId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.Equals(existing.Status, AnnouncementStatuses.Published, StringComparison.Ordinal))
        {
            return InvalidStatus("Only published announcements can be retracted.");
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                AnnouncementSql.Retract,
                new Dictionary<string, object?>
                {
                    ["Id"] = announcementId,
                    ["RetractedStatus"] = AnnouncementStatuses.Retracted,
                    ["PublishedStatus"] = AnnouncementStatuses.Published,
                    ["RetractedAtUtc"] = now,
                    ["RetractedByUserId"] = actorUserId,
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

        return await queries.GetByIdAsync(announcementId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SyncTargetsAsync(
        Guid announcementId,
        HostAnnouncementAudienceState audience,
        CancellationToken cancellationToken)
    {
        foreach (var userId in audience.TargetUserIds)
        {
            await commandExecutor.ExecuteAsync(
                    AnnouncementTargetSql.InsertUser,
                    new Dictionary<string, object?>
                    {
                        ["Id"] = idGenerator.NewId(),
                        ["AnnouncementId"] = announcementId,
                        ["UserId"] = userId,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var organization in audience.TargetOrganizations)
        {
            await commandExecutor.ExecuteAsync(
                    AnnouncementTargetSql.InsertOrganization,
                    new Dictionary<string, object?>
                    {
                        ["Id"] = idGenerator.NewId(),
                        ["AnnouncementId"] = announcementId,
                        ["TenantId"] = organization.TenantId,
                        ["OrganizationUnitId"] = organization.OrganizationUnitId,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<HostAnnouncementAudienceState> LoadExistingTargetsAsync(
        Guid announcementId,
        CancellationToken cancellationToken)
    {
        var users = await queryExecutor.QueryAsync<AnnouncementTargetUserRecord>(
                AnnouncementTargetSql.ListUsersByAnnouncementIds,
                new Dictionary<string, object?> { ["AnnouncementIds"] = new[] { announcementId } },
                cancellationToken)
            .ConfigureAwait(false);
        var organizations = await queryExecutor.QueryAsync<AnnouncementTargetOrganizationRecord>(
                AnnouncementTargetSql.ListOrganizationsByAnnouncementIds,
                new Dictionary<string, object?> { ["AnnouncementIds"] = new[] { announcementId } },
                cancellationToken)
            .ConfigureAwait(false);
        return new HostAnnouncementAudienceState(
            AnnouncementKinds.Announcement,
            AnnouncementAudienceKinds.All,
            users.Select(row => row.UserId).ToArray(),
            organizations
                .Select(row => new HostAnnouncementTargetOrganization(row.TenantId, row.OrganizationUnitId))
                .ToArray());
    }

    private async Task ReplaceTargetsAsync(
        Guid announcementId,
        HostAnnouncementAudienceState audience,
        CancellationToken cancellationToken)
    {
        await commandExecutor.ExecuteAsync(
                AnnouncementTargetSql.DeleteUsersByAnnouncementId,
                new Dictionary<string, object?> { ["AnnouncementId"] = announcementId },
                cancellationToken)
            .ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
                AnnouncementTargetSql.DeleteOrganizationsByAnnouncementId,
                new Dictionary<string, object?> { ["AnnouncementId"] = announcementId },
                cancellationToken)
            .ConfigureAwait(false);
        await SyncTargetsAsync(announcementId, audience, cancellationToken)
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

    private static Result<HostAnnouncementResponse> InvalidStatus(string message) =>
        Result<HostAnnouncementResponse>.Failure(new Error(
            NotificationsErrorCodes.AnnouncementInvalidStatus,
            message,
            ErrorType.Validation));
}
