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
        CancellationToken cancellationToken = default,
        Guid? requestKey = null)
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

        return transaction.ExecuteAsync(async token =>
        {
            await RecordInTransactionAsync(documentItemId, documentTitle, accessTypeKey, sourceKey,
                actorUserId, clientIpFingerprint, token, requestKey).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    /// <summary>复用调用方已持有的 Document 本地事务，避免分享扣次与访问事实分离。</summary>
    internal async Task RecordInTransactionAsync(Guid documentItemId, string documentTitle, string accessTypeKey,
        string sourceKey, Guid? actorUserId, string? clientIpFingerprint, CancellationToken cancellationToken, Guid? requestKey = null)
    {
        var now = clock.UtcNow;
        // 根行串行化同一文档的逻辑事件检查与计数；不延长首次事件的发生时间。
        var locked = await commandExecutor.ExecuteAsync(DocumentAccessLogSql.LockItem,
            DocumentSqlParameters.Create(("DocumentItemId", documentItemId)), cancellationToken).ConfigureAwait(false);
        if (locked != 1) return;
        var operationKey = requestKey is null || requestKey == Guid.Empty ? null
            : $"{documentItemId:N}:{actorUserId?.ToString("N") ?? "anonymous"}:{sourceKey}:{accessTypeKey}:{requestKey:N}";
        var inserted = await commandExecutor.ExecuteAsync(
                DocumentAccessLogSql.Insert,
                DocumentSqlParameters.Create(
            ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                    ("Id", idGenerator.NewId()), ("OperationKey", operationKey),
                    ("DocumentItemId", documentItemId),
                    ("DocumentTitle", documentTitle.Trim()),
                    ("AccessTypeKey", accessTypeKey),
                    ("SourceKey", sourceKey),
                    ("ActorUserId", actorUserId),
                    ("OccurredAtUtc", now),
                    ("ClientIpFingerprint", clientIpFingerprint)),
                cancellationToken)
            .ConfigureAwait(false);

        if (inserted != 1) return;
        _ = await commandExecutor.ExecuteAsync(
                DocumentAccessLogSql.TouchItemAccess,
                DocumentSqlParameters.Create(
                    ("DocumentItemId", documentItemId),
                    ("OccurredAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

    }
}
