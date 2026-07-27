using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;

namespace Full.NET.Host.Worker;

/// <summary>
/// 表示一次退役扫描的非敏感、可机读结果。
/// </summary>
/// <param name="Code">安全或阻断的稳定机器码。</param>
/// <param name="MessageType">用户指定且由当前 Handler 接管的消息类型。</param>
/// <param name="SchemaVersion">准备退役的结构版本。</param>
/// <param name="Routes">实际扫描的 canonical 与 legacy 消息类型。</param>
/// <param name="PendingCount">尚未进入终态的待消费数量。</param>
/// <param name="DeadLetterCount">尚未处理的死信数量。</param>
/// <param name="OldestUnprocessedOccurredAtUtc">目标未处理消息中的最老发生时间。</param>
internal sealed record OutboxVersionRetirementReport(
    string Code,
    string MessageType,
    int SchemaVersion,
    IReadOnlyList<string> Routes,
    long PendingCount,
    long DeadLetterCount,
    DateTimeOffset? OldestUnprocessedOccurredAtUtc)
{
    public bool CanRetire =>
        PendingCount == 0
        && DeadLetterCount == 0;
}

/// <summary>
/// 将当前 Handler 的兼容路由映射为只读数据库扫描，并给出保守退役结论。
/// </summary>
internal sealed class OutboxVersionRetirementScanner(
    IOutboxBacklogReader backlogReader,
    IReadOnlyCollection<IIntegrationEventHandler> handlers)
{
    public async Task<OutboxVersionRetirementReport> ScanAsync(
        OutboxVersionRetirementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var matches = IntegrationEventHandlerMatcher.Match(
            handlers,
            request.MessageType,
            request.SchemaVersion);
        if (matches.Count == 0)
        {
            throw new OutboxVersionRetirementException(
                OutboxVersionRetirementErrorCodes.HandlerNotFound);
        }

        if (matches.Count > 1)
        {
            throw new OutboxVersionRetirementException(
                OutboxVersionRetirementErrorCodes.AmbiguousHandler);
        }

        var handler = matches[0];
        var routes = EnumerateRoutes(handler)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var snapshot = await backlogReader
            .ReadVersionRetirementAsync(
                routes,
                request.SchemaVersion,
                cancellationToken)
            .ConfigureAwait(false);
        var code = snapshot.PendingCount == 0
            && snapshot.DeadLetterCount == 0
                ? OutboxVersionRetirementErrorCodes.Safe
                : OutboxVersionRetirementErrorCodes.Blocked;
        return new OutboxVersionRetirementReport(
            code,
            request.MessageType,
            request.SchemaVersion,
            routes,
            snapshot.PendingCount,
            snapshot.DeadLetterCount,
            snapshot.OldestUnprocessedOccurredAtUtc);
    }

    private static IEnumerable<string> EnumerateRoutes(
        IIntegrationEventHandler handler)
    {
        yield return handler.EventType;
        foreach (var legacyEventType in handler.LegacyEventTypes)
        {
            yield return legacyEventType;
        }
    }
}
