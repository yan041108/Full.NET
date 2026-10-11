using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Features.ReadIntentDelivery;

/// <summary>只读聚合当前可信作用域的通知状态，跨模块调用不参与写事务。</summary>
internal sealed class NotificationIntentDeliveryDirectory(IQueryExecutor queries, ICurrentTenant tenant)
    : INotificationIntentDeliveryDirectory
{
    public async Task<NotificationIntentDeliverySnapshot?> FindByIdempotencyAsync(
        string producerKey, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(producerKey, nameof(producerKey));
        ValidateKey(idempotencyKey, nameof(idempotencyKey));
        // Host 由明确的 IsHost 标记识别；实际访问器在 Host 下也会报告 IsAvailable。
        // Host 与 Tenant 必须互斥且完整，不能把尚未解析的上下文降级成 Host。
        var scope = tenant.IsHost && tenant.Id is null
            ? "host"
            : !tenant.IsHost && tenant.IsAvailable && tenant.Id is { } tenantId && tenantId != Guid.Empty
                ? $"tenant:{tenantId:N}"
                : throw new InvalidOperationException("The trusted notification scope is unavailable.");
        var row = await queries.QuerySingleOrDefaultAsync<NotificationIntentDeliveryRecord>(
            NotificationIntentDeliverySql.FindByIdempotency,
            new Dictionary<string, object?>
            {
                ["TenantScopeKey"] = scope,
                ["ProducerKey"] = producerKey,
                ["IdempotencyKey"] = idempotencyKey
            }, cancellationToken).ConfigureAwait(false);
        if (row is null) return null;
        // 防止矛盾统计或未来未知状态被折算成成功；未知类别保持单独可见。
        if (row.Id == Guid.Empty || row.TenantScopeKey != scope || row.ProducerKey != producerKey ||
            row.IdempotencyKey != idempotencyKey || row.StatusKey != "accepted" ||
            row.TotalDeliveryCount < 0 || row.PendingDeliveryCount < 0 || row.SentDeliveryCount < 0 ||
            row.FailedDeliveryCount < 0 || row.DeadLetteredDeliveryCount < 0 || row.UnknownDeliveryCount < 0 || row.OtherDeliveryCount < 0 ||
            row.PersistedDeliveryCount < 0 || row.DeliveredDeliveryCount < 0 || row.ReadDeliveryCount < 0 || row.SuppressedDeliveryCount < 0 ||
            (row.PendingDeliveryCount == 0 && row.NextAttemptAtUtc is not null) ||
            (long)row.PendingDeliveryCount + row.SentDeliveryCount + row.FailedDeliveryCount +
                row.DeadLetteredDeliveryCount + row.UnknownDeliveryCount + row.OtherDeliveryCount +
                row.PersistedDeliveryCount + row.DeliveredDeliveryCount + row.ReadDeliveryCount + row.SuppressedDeliveryCount != row.TotalDeliveryCount)
            throw new InvalidOperationException("The notification delivery snapshot is inconsistent.");
        return new(row.Id, row.CreatedAtUtc, row.TotalDeliveryCount, row.PendingDeliveryCount,
            row.SentDeliveryCount, row.FailedDeliveryCount, row.DeadLetteredDeliveryCount,
            row.UnknownDeliveryCount, row.OtherDeliveryCount, row.NextAttemptAtUtc,
            row.PersistedDeliveryCount, row.DeliveredDeliveryCount, row.ReadDeliveryCount, row.SuppressedDeliveryCount);
    }

    private static void ValidateKey(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 ||
            value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is '/' or '\\'))
            throw new ArgumentException("A stable notification key is required.", parameterName);
    }
}

/// <summary>聚合记录包含作用域和幂等身份，读取后再次检查来源边界。</summary>
internal sealed record NotificationIntentDeliveryRecord
{
    public Guid Id { get; init; }
    public string TenantScopeKey { get; init; } = string.Empty;
    public string ProducerKey { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    public string StatusKey { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public int TotalDeliveryCount { get; init; }
    public int PendingDeliveryCount { get; init; }
    public int SentDeliveryCount { get; init; }
    public int FailedDeliveryCount { get; init; }
    public int DeadLetteredDeliveryCount { get; init; }
    public int UnknownDeliveryCount { get; init; }
    public int OtherDeliveryCount { get; init; }
    public DateTimeOffset? NextAttemptAtUtc { get; init; }
    public int PersistedDeliveryCount { get; init; }
    public int DeliveredDeliveryCount { get; init; }
    public int ReadDeliveryCount { get; init; }
    public int SuppressedDeliveryCount { get; init; }

    // MySQL COUNT 返回 Int64；JIT 必须走属性转换，不能要求物理类型匹配 Int32 构造签名。
    public NotificationIntentDeliveryRecord() { }

    internal NotificationIntentDeliveryRecord(Guid id, string tenantScopeKey, string producerKey, string idempotencyKey,
        string statusKey, DateTimeOffset createdAtUtc, int totalDeliveryCount, int pendingDeliveryCount,
        int sentDeliveryCount, int failedDeliveryCount, int deadLetteredDeliveryCount, int unknownDeliveryCount,
        int otherDeliveryCount, DateTimeOffset? nextAttemptAtUtc, int persistedDeliveryCount = 0,
        int deliveredDeliveryCount = 0, int readDeliveryCount = 0, int suppressedDeliveryCount = 0)
    {
        Id = id; TenantScopeKey = tenantScopeKey; ProducerKey = producerKey; IdempotencyKey = idempotencyKey;
        StatusKey = statusKey; CreatedAtUtc = createdAtUtc; TotalDeliveryCount = totalDeliveryCount;
        PendingDeliveryCount = pendingDeliveryCount; SentDeliveryCount = sentDeliveryCount;
        FailedDeliveryCount = failedDeliveryCount; DeadLetteredDeliveryCount = deadLetteredDeliveryCount;
        UnknownDeliveryCount = unknownDeliveryCount; OtherDeliveryCount = otherDeliveryCount; NextAttemptAtUtc = nextAttemptAtUtc;
        PersistedDeliveryCount = persistedDeliveryCount; DeliveredDeliveryCount = deliveredDeliveryCount;
        ReadDeliveryCount = readDeliveryCount; SuppressedDeliveryCount = suppressedDeliveryCount;
    }
}

internal static class NotificationIntentDeliverySql
{
    public static readonly SqlStatement FindByIdempotency = new(
        "notifications.platform.intent.read_delivery_progress",
        """
        SELECT i.Id, i.TenantScopeKey, i.ProducerKey, i.IdempotencyKey, i.StatusKey, i.CreatedAtUtc,
               COUNT(d.Id) AS TotalDeliveryCount,
               COUNT(CASE WHEN d.StatusKey = 'accepted' OR (d.StatusKey = 'unknown' AND d.NextAttemptAtUtc IS NOT NULL) THEN 1 END) AS PendingDeliveryCount,
               COUNT(CASE WHEN d.StatusKey = 'sent' THEN 1 END) AS SentDeliveryCount,
               COUNT(CASE WHEN d.StatusKey = 'failed' THEN 1 END) AS FailedDeliveryCount,
               COUNT(CASE WHEN d.StatusKey = 'dead_lettered' THEN 1 END) AS DeadLetteredDeliveryCount,
               COUNT(CASE WHEN d.StatusKey = 'unknown' AND d.NextAttemptAtUtc IS NULL THEN 1 END) AS UnknownDeliveryCount,
               COUNT(CASE WHEN d.Id IS NOT NULL AND (d.StatusKey IS NULL OR d.StatusKey NOT IN ('persisted', 'accepted', 'sent', 'delivered', 'read', 'suppressed', 'failed', 'dead_lettered', 'unknown')) THEN 1 END) AS OtherDeliveryCount,
               MIN(CASE WHEN d.StatusKey = 'accepted' OR (d.StatusKey = 'unknown' AND d.NextAttemptAtUtc IS NOT NULL) THEN d.NextAttemptAtUtc END) AS NextAttemptAtUtc,
               COUNT(CASE WHEN d.StatusKey = 'persisted' THEN 1 END) AS PersistedDeliveryCount,
               COUNT(CASE WHEN d.StatusKey = 'delivered' THEN 1 END) AS DeliveredDeliveryCount,
               COUNT(CASE WHEN d.StatusKey = 'read' THEN 1 END) AS ReadDeliveryCount,
               COUNT(CASE WHEN d.StatusKey = 'suppressed' THEN 1 END) AS SuppressedDeliveryCount
        FROM fn_notifications_intent i
        LEFT JOIN fn_notifications_delivery d ON d.IntentId = i.Id
        WHERE i.TenantScopeKey = @TenantScopeKey
          AND i.ProducerKey = @ProducerKey
          AND i.IdempotencyKey = @IdempotencyKey
        GROUP BY i.Id, i.TenantScopeKey, i.ProducerKey, i.IdempotencyKey, i.StatusKey, i.CreatedAtUtc
        """,
        SqlDataScope.Global);
}
