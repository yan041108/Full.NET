using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;

namespace Full.NET.Modules.Notifications.Features.IntentAttachments;

/// <summary>在 Intent 受理时同步邮件附件 claim 与本地投影，并在投递结束后释放 claim。</summary>
internal sealed class NotificationIntentAttachmentCoordinator(
    IHostFileReferenceClaimService hostFileReferenceClaimService,
    IHostFileDescriptorReader hostFileDescriptorReader,
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>在事务外校验附件元数据与上传人归属。</summary>
    public async Task<Result<IReadOnlyList<Guid>>> ValidateAsync(
        Guid actorUserId,
        string channelKey,
        IReadOnlyList<Guid>? attachmentFileIds,
        CancellationToken cancellationToken)
    {
        var normalized = NotificationIntentAttachmentRules.NormalizeFileIds(attachmentFileIds);
        if (attachmentFileIds is { Count: > 0 } && normalized.Count == 0)
        {
            return AttachmentInvalid();
        }

        if (normalized.Count == 0)
        {
            return Result<IReadOnlyList<Guid>>.Success([]);
        }

        if (!string.Equals(channelKey, "email", StringComparison.Ordinal))
        {
            return AttachmentInvalid();
        }

        long totalBytes = 0;
        foreach (var fileId in normalized)
        {
            var descriptor = await hostFileDescriptorReader
                .GetReadyDescriptorAsync(fileId, cancellationToken)
                .ConfigureAwait(false);
            if (descriptor is null
                || descriptor.CreatedByUserId != actorUserId
                || descriptor.SizeBytes > NotificationIntentAttachmentRules.MaxAttachmentSizeBytes
                || !NotificationIntentAttachmentRules.MatchesAllowedExtension(descriptor.OriginalFileName))
            {
                return AttachmentInvalid();
            }

            totalBytes += descriptor.SizeBytes;
            if (totalBytes > NotificationIntentAttachmentRules.MaxTotalAttachmentSizeBytes)
            {
                return AttachmentInvalid();
            }
        }

        return Result<IReadOnlyList<Guid>>.Success(normalized);
    }

    /// <summary>在 Intent 持久化成功后 claim 附件并写入投影；失败时回滚 Pending claim。</summary>
    public async Task<Result<bool>> SynchronizeAsync(
        Guid intentId,
        IReadOnlyList<Guid> attachmentFileIds,
        CancellationToken cancellationToken)
    {
        if (attachmentFileIds.Count == 0)
        {
            return Result<bool>.Success(true);
        }

        var claimedKeys = new List<string>(attachmentFileIds.Count);
        try
        {
            var now = clock.UtcNow;
            for (var index = 0; index < attachmentFileIds.Count; index++)
            {
                var fileId = attachmentFileIds[index];
                var idempotencyKey = HostFileReferenceClaimIdempotencyKeys.NotificationIntentAttachment(
                    intentId,
                    fileId);
                var claimResult = await hostFileReferenceClaimService
                    .ClaimAsync(
                        new HostFileReferenceClaimRequest(
                            idempotencyKey,
                            HostFileReferenceClaimConsumerModules.Notifications,
                            intentId,
                            fileId),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!claimResult.IsSuccess)
                {
                    return AttachmentInvalidBool();
                }

                claimedKeys.Add(idempotencyKey);
                await commandExecutor.ExecuteAsync(
                        NotificationIntentAttachmentSql.Insert,
                        NotificationPlatformSqlParameters.Create(
                            ("Id", idGenerator.NewId()),
                            ("IntentId", intentId),
                            ("FileId", fileId),
                            ("SortOrder", index),
                            ("CreatedAtUtc", now)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            await ReleaseClaimedAsync(claimedKeys, cancellationToken).ConfigureAwait(false);
            throw;
        }

        foreach (var idempotencyKey in claimedKeys)
        {
            var confirmResult = await hostFileReferenceClaimService
                .ConfirmAsync(idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (!confirmResult.IsSuccess)
            {
                await ReleaseClaimedAsync(claimedKeys, cancellationToken).ConfigureAwait(false);
                return AttachmentInvalidBool();
            }
        }

        return Result<bool>.Success(true);
    }

    /// <summary>当 Intent 下不再有待重试投递时释放全部附件 claim。</summary>
    public async Task TryReleaseIfTerminalAsync(
        Guid intentId,
        CancellationToken cancellationToken)
    {
        var pending = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                NotificationIntentAttachmentSql.CountPendingDeliveriesByIntent,
                NotificationPlatformSqlParameters.Create(("IntentId", intentId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (pending > 0)
        {
            return;
        }

        var attachments = await queryExecutor.QueryAsync<NotificationIntentAttachmentRecord>(
                NotificationIntentAttachmentSql.ListByIntent,
                NotificationPlatformSqlParameters.Create(("IntentId", intentId)),
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var attachment in attachments)
        {
            var idempotencyKey = HostFileReferenceClaimIdempotencyKeys.NotificationIntentAttachment(
                intentId,
                attachment.FileId);
            _ = await hostFileReferenceClaimService
                .ReleaseAsync(idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task ReleaseClaimedAsync(
        IReadOnlyList<string> idempotencyKeys,
        CancellationToken cancellationToken)
    {
        foreach (var idempotencyKey in idempotencyKeys)
        {
            _ = await hostFileReferenceClaimService
                .ReleaseAsync(idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static Result<IReadOnlyList<Guid>> AttachmentInvalid() =>
        Result<IReadOnlyList<Guid>>.Failure(new Error(
            NotificationsErrorCodes.IntentAttachmentInvalid,
            "The notification intent attachment is invalid or unauthorized.",
            ErrorType.Validation));

    private static Result<bool> AttachmentInvalidBool() =>
        Result<bool>.Failure(new Error(
            NotificationsErrorCodes.IntentAttachmentInvalid,
            "The notification intent attachment is invalid or unauthorized.",
            ErrorType.Validation));
}
