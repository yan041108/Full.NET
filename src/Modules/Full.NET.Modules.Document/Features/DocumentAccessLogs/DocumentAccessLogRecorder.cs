using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.DocumentAccessLogs;

/// <summary>
/// 文档访问日志写入器：在单一事务内追加访问日志并刷新文档项最后访问时间与累计访问次数。
/// </summary>
internal sealed class DocumentAccessLogRecorder(
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>记录一次成功的文档访问事件。</summary>
    public Task RecordAsync(
        Guid documentItemId,
        string documentTitle,
        string accessTypeKey,
        string sourceKey,
        Guid? actorUserId,
        string? clientIpFingerprint,
        CancellationToken cancellationToken = default)
    {
        if (!HostDocumentAccessTypeKeys.All.Contains(accessTypeKey, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(accessTypeKey));
        }

        if (documentItemId == Guid.Empty
            || string.IsNullOrWhiteSpace(documentTitle)
            || string.IsNullOrWhiteSpace(sourceKey))
        {
            return Task.CompletedTask;
        }

        var now = clock.UtcNow;
        return transaction.ExecuteAsync(
            async token =>
            {
                await commandExecutor.ExecuteAsync(
                        DocumentAccessLogSql.Insert,
                        DocumentSqlParameters.Create(
                            ("Id", idGenerator.NewId()),
                            ("DocumentItemId", documentItemId),
                            ("DocumentTitle", documentTitle.Trim()),
                            ("AccessTypeKey", accessTypeKey),
                            ("SourceKey", sourceKey),
                            ("ActorUserId", actorUserId),
                            ("OccurredAtUtc", now),
                            ("ClientIpFingerprint", clientIpFingerprint)),
                        token)
                    .ConfigureAwait(false);

                _ = await commandExecutor.ExecuteAsync(
                        DocumentAccessLogSql.TouchItemAccess,
                        DocumentSqlParameters.Create(
                            ("DocumentItemId", documentItemId),
                            ("OccurredAtUtc", now)),
                        token)
                    .ConfigureAwait(false);

                return true;
            },
            cancellationToken);
    }
}
