using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;

namespace Full.NET.Modules.Notifications.Execution;

/// <summary>在 Worker 发送前按 Intent 投影有界装载附件内容。</summary>
internal sealed class NotificationAttachmentLoader(
    IQueryExecutor queryExecutor,
    IHostFileContentReader hostFileContentReader)
{
    /// <summary>装载指定 Intent 的全部附件；任一文件超限或不可读时失败关闭。</summary>
    public async Task<Result<IReadOnlyList<NotificationProviderAttachment>>> LoadForIntentAsync(
        Guid intentId,
        CancellationToken cancellationToken)
    {
        var records = await queryExecutor.QueryAsync<NotificationIntentAttachmentRecord>(
                NotificationIntentAttachmentSql.ListByIntent,
                NotificationPlatformSqlParameters.Create(("IntentId", intentId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (records.Count == 0)
        {
            return Result<IReadOnlyList<NotificationProviderAttachment>>.Success([]);
        }

        var attachments = new List<NotificationProviderAttachment>(records.Count);
        long totalBytes = 0;
        foreach (var record in records)
        {
            var opened = await hostFileContentReader
                .OpenReadyContentAsync(record.FileId, cancellationToken)
                .ConfigureAwait(false);
            if (!opened.IsSuccess)
            {
                return AttachmentLoadFailed();
            }

            var content = opened.Value!;
            try
            {
                var bytes = await ReadBoundedAsync(
                        content.Content,
                        NotificationIntentAttachmentRules.MaxAttachmentSizeBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!bytes.IsSuccess)
                {
                    return AttachmentLoadFailed();
                }

                totalBytes += bytes.Value!.Length;
                if (totalBytes > NotificationIntentAttachmentRules.MaxTotalAttachmentSizeBytes)
                {
                    return AttachmentLoadFailed();
                }

                attachments.Add(new NotificationProviderAttachment(
                    content.OriginalFileName,
                    content.ContentType,
                    bytes.Value));
            }
            finally
            {
                await content.Content.DisposeAsync().ConfigureAwait(false);
            }
        }

        return Result<IReadOnlyList<NotificationProviderAttachment>>.Success(attachments);
    }

    private static async Task<Result<byte[]>> ReadBoundedAsync(
        Stream stream,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                return Result<byte[]>.Failure(new Error(
                    NotificationsErrorCodes.IntentAttachmentLoadFailed,
                    "The notification attachment exceeds the allowed size.",
                    ErrorType.BusinessRule));
            }

            buffer.Write(chunk, 0, read);
        }

        return Result<byte[]>.Success(buffer.ToArray());
    }

    private static Result<IReadOnlyList<NotificationProviderAttachment>> AttachmentLoadFailed() =>
        Result<IReadOnlyList<NotificationProviderAttachment>>.Failure(new Error(
            NotificationsErrorCodes.IntentAttachmentLoadFailed,
            "The notification attachment could not be loaded.",
            ErrorType.BusinessRule));
}
