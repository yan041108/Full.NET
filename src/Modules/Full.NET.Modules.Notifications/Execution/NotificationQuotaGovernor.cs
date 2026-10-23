using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Execution;

/// <summary>一次配额预留的结果：已预留、缺失配置或窗口已满。</summary>
internal enum NotificationQuotaReservationStatus
{
    /// <summary>四个维度均已原子预留。</summary>
    Reserved,

    /// <summary>存在未声明有界额度的维度，发送失败关闭。</summary>
    Missing,

    /// <summary>至少一个维度的窗口已满，发送进入延迟状态。</summary>
    Exceeded,

    /// <summary>旧世代调用结果未决，保持额度占用并暂挂投递。</summary>
    PendingReconciliation,

    /// <summary>投递世代已被接管或租约已过期，当前 Worker 必须退出。</summary>
    LostLease,
}

/// <summary>配额预留结果，携带失败关闭与延迟所需的最小信息。</summary>
internal readonly record struct NotificationQuotaReservationOutcome(
    NotificationQuotaReservationStatus Status,
    string? ErrorCode,
    int RetryAfterSeconds)
{
    public bool IsReserved => Status == NotificationQuotaReservationStatus.Reserved;
}

/// <summary>
/// 发送配额治理：在调用 Provider 前原子预留 Provider/租户/Producer/Scene 四个维度的有界额度，
/// 并在结果确定后核销、释放或暂挂对账。
/// </summary>
/// <remarks>
/// 预留与窗口计数在同一事务内完成：先按 PolicyId 排序锁定政策行以串行化竞争，再条件递增计数器，
/// 条件 <c>ReservedCount - ReleasedCount &lt; MaxCount</c> 保证窗口内不超卖。任一维度缺失启用声明时
/// 失败关闭。旧世代孤儿预留不能证明未发送，转为待对账并保持占额。
/// </remarks>
internal sealed class NotificationQuotaGovernor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>单次领取最多回收的孤儿预留行数，避免长时间异常导致的长事务。</summary>
    private const int MaxAbandonedReservationsPerClaim = 64;

    /// <summary>
    /// 为一次外发原子预留四个维度的额度；窗口已满返回 <see cref="NotificationQuotaReservationStatus.Exceeded"/>，
    /// 维度缺失返回 <see cref="NotificationQuotaReservationStatus.Missing"/>。
    /// </summary>
    public async Task<NotificationQuotaReservationOutcome> ReserveAsync(
        Guid deliveryId,
        long leaseGeneration,
        string tenantScopeKey,
        string providerTypeKey,
        string producerKey,
        string sceneKey,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteAsync(
                    token => ReserveCoreAsync(
                        deliveryId, leaseGeneration, tenantScopeKey,
                        providerTypeKey, producerKey, sceneKey, token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (QuotaReservationRejectedException rejected)
        {
            return new NotificationQuotaReservationOutcome(
                rejected.Status, rejected.ErrorCode, rejected.RetryAfterSeconds);
        }
    }

    /// <summary>
    /// 在调用方事务内把本轮预留收敛到目标状态：成功核销、确定未发送释放、Unknown 暂挂对账。
    /// </summary>
    /// <param name="deliveryId">投递标识。</param>
    /// <param name="leaseGeneration">产生预留的租约世代，用于精确命中本轮预留行。</param>
    /// <param name="stateKey">目标预留状态键，取值见 <see cref="NotificationQuotaReservationStates"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 只有释放会调整窗口释放计数；核销与暂挂都保持额度占用，避免用释放掩盖已发生的发送或未决结果。
    /// </remarks>
    public async Task ResolveAsync(
        Guid deliveryId,
        long leaseGeneration,
        string stateKey,
        CancellationToken cancellationToken)
    {
        var reserved = await queryExecutor.QueryAsync<NotificationQuotaReservationRecord>(
                NotificationPlatformSql.ListReservedQuotaReservations,
                NotificationPlatformSqlParameters.Create(
                    ("DeliveryId", deliveryId),
                    ("LeaseGeneration", leaseGeneration)),
                cancellationToken)
            .ConfigureAwait(false);
        if (reserved.Count == 0)
        {
            return;
        }

        await ResolveRowsAsync(reserved, stateKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>在回执应用事务内按原消息号收敛未决配额，重复或并发回执不重复计数。</summary>
    public async Task ReconcileForMessageAsync(
        Guid deliveryId,
        string providerMessageId,
        string mappedStatusKey,
        CancellationToken cancellationToken)
    {
        if (mappedStatusKey is not ("sent" or "delivered" or "read" or "failed"))
        {
            return;
        }

        var rows = await queryExecutor.QueryAsync<NotificationQuotaReservationRecord>(
                NotificationPlatformSql.ListQuotaReservationsForMessage,
                NotificationPlatformSqlParameters.Create(
                    ("DeliveryId", deliveryId),
                    ("ProviderMessageId", providerMessageId)),
                cancellationToken)
            .ConfigureAwait(false);
        await ResolveRowsAsync(
                rows,
                // 负面回执可能是厂商受理后的退信，不能据此断言未占用发送额度。
                // 只有同步调用的确定未发送结果才释放；可信回执证明厂商已处理过该次请求，保守核销。
                NotificationQuotaReservationStates.Consumed,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task ResolveRowsAsync(
        IReadOnlyList<NotificationQuotaReservationRecord> rows,
        string stateKey,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        foreach (var row in rows.OrderBy(row => row.PolicyId.ToString("N"), StringComparer.Ordinal))
        {
            // 先以行状态 CAS 认领收敛，再调整计数；两者同事务，只有赢家执行计数更新。
            var changed = await commandExecutor.ExecuteAsync(
                    NotificationPlatformSql.ResolveQuotaReservationById,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", row.Id), ("StateKey", stateKey), ("Now", now)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (changed == 0 || stateKey == NotificationQuotaReservationStates.PendingReconciliation)
            {
                continue;
            }

            await commandExecutor.ExecuteAsync(
                    stateKey == NotificationQuotaReservationStates.Released
                        ? NotificationPlatformSql.ReleaseQuotaCounter
                        : NotificationPlatformSql.ConsumeQuotaCounter,
                    NotificationPlatformSqlParameters.Create(
                        ("PolicyId", row.PolicyId),
                        ("WindowStartUtc", row.WindowStartUtc),
                        ("Now", now)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<NotificationQuotaReservationOutcome> ReserveCoreAsync(
        Guid deliveryId,
        long leaseGeneration,
        string tenantScopeKey,
        string providerTypeKey,
        string producerKey,
        string sceneKey,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var lockedDelivery = await queryExecutor.QuerySingleOrDefaultAsync<Guid>(
                databaseOptions.Value.Provider == DatabaseProvider.MySql
                    ? NotificationPlatformSql.LockDeliveryForQuotaMySql
                    : NotificationPlatformSql.LockDeliveryForQuotaSqlServer,
                NotificationPlatformSqlParameters.Create(
                    ("Id", deliveryId), ("LeaseGeneration", leaseGeneration), ("Now", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (lockedDelivery == Guid.Empty)
        {
            return new NotificationQuotaReservationOutcome(NotificationQuotaReservationStatus.LostLease, null, 0);
        }

        // 已被同一世代预留过时直接复用，避免重复调用时二次占额。
        var existing = await queryExecutor.QueryAsync<NotificationQuotaReservationRecord>(
                NotificationPlatformSql.ListReservedQuotaReservations,
                NotificationPlatformSqlParameters.Create(
                    ("DeliveryId", deliveryId),
                    ("LeaseGeneration", leaseGeneration)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return new NotificationQuotaReservationOutcome(
                NotificationQuotaReservationStatus.Reserved, null, 0);
        }

        if (await RecycleAbandonedReservationsAsync(deliveryId, leaseGeneration, now, cancellationToken)
            .ConfigureAwait(false))
        {
            return new NotificationQuotaReservationOutcome(
                NotificationQuotaReservationStatus.PendingReconciliation, null, 0);
        }

        var resolutions = await queryExecutor.QueryAsync<NotificationQuotaResolutionRecord>(
                NotificationPlatformSql.ResolveQuotaPolicies,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", tenantScopeKey),
                    ("ProviderValue", providerTypeKey),
                    ("TenantValue", tenantScopeKey),
                    ("ProducerValue", producerKey),
                    ("SceneValue", sceneKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (resolutions.Count < NotificationQuotaDimensions.All.Count)
        {
            // 任一维度缺少启用声明即失败关闭，绝不退化为无界并发。
            throw new QuotaReservationRejectedException(
                NotificationQuotaReservationStatus.Missing,
                NotificationsErrorCodes.QuotaPolicyMissing,
                retryAfterSeconds: 0);
        }

        // 按 PolicyId 排序后加锁，保证多维度竞争下的全局加锁顺序一致，避免互相死锁。
        var ordered = resolutions
            .OrderBy(resolution => resolution.PolicyId.ToString("N"), StringComparer.Ordinal)
            .ToArray();
        var lockStatement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? NotificationPlatformSql.LockQuotaPolicyMySql
            : NotificationPlatformSql.LockQuotaPolicySqlServer;

        foreach (var resolution in ordered)
        {
            await commandExecutor.ExecuteAsync(
                    lockStatement,
                    NotificationPlatformSqlParameters.Create(("Id", resolution.PolicyId)),
                    cancellationToken)
                .ConfigureAwait(false);

            var windowStart = AlignWindowStart(now, resolution.WindowSeconds);
            var counter = await queryExecutor.QuerySingleOrDefaultAsync<NotificationQuotaCounterRecord>(
                    NotificationPlatformSql.FindQuotaCounter,
                    NotificationPlatformSqlParameters.Create(
                        ("PolicyId", resolution.PolicyId),
                        ("WindowStartUtc", windowStart)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (counter is null)
            {
                await commandExecutor.ExecuteAsync(
                        NotificationPlatformSql.InsertQuotaCounter,
                        NotificationPlatformSqlParameters.Create(
                            ("Id", idGenerator.NewId()),
                            ("PolicyId", resolution.PolicyId),
                            ("WindowStartUtc", windowStart),
                            ("WindowSeconds", resolution.WindowSeconds),
                            ("Now", now)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var reserved = await commandExecutor.ExecuteAsync(
                    NotificationPlatformSql.IncrementQuotaCounterReserved,
                    NotificationPlatformSqlParameters.Create(
                        ("PolicyId", resolution.PolicyId),
                        ("WindowStartUtc", windowStart),
                        ("Now", now),
                        ("MaxCount", resolution.MaxCount)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (reserved == 0)
            {
                var retryAfter = (int)Math.Max(
                    1,
                    (windowStart.AddSeconds(resolution.WindowSeconds) - now).TotalSeconds);
                throw new QuotaReservationRejectedException(
                    NotificationQuotaReservationStatus.Exceeded,
                    NotificationsErrorCodes.QuotaExceeded,
                    retryAfter);
            }

            await commandExecutor.ExecuteAsync(
                    NotificationPlatformSql.InsertQuotaReservation,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", idGenerator.NewId()),
                        ("DeliveryId", deliveryId),
                        ("PolicyId", resolution.PolicyId),
                        ("DimensionKey", resolution.DimensionKey),
                        ("TenantScopeKey", tenantScopeKey),
                        ("WindowStartUtc", windowStart),
                        ("LeaseGeneration", leaseGeneration),
                        ("StateKey", NotificationQuotaReservationStates.Reserved),
                        ("Now", now)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new NotificationQuotaReservationOutcome(
            NotificationQuotaReservationStatus.Reserved, null, 0);
    }

    /// <summary>旧世代预留无法证明未发送，保留占额并转待对账；返回是否存在未决调用。</summary>
    private async Task<bool> RecycleAbandonedReservationsAsync(
        Guid deliveryId,
        long leaseGeneration,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? NotificationPlatformSql.ListAbandonedQuotaReservationsMySql
            : NotificationPlatformSql.ListAbandonedQuotaReservationsSqlServer;
        var abandoned = await queryExecutor.QueryAsync<NotificationQuotaAbandonedReservationRecord>(
                statement,
                NotificationPlatformSqlParameters.Create(
                    ("DeliveryId", deliveryId),
                    ("LeaseGeneration", leaseGeneration),
                    ("MaxRows", MaxAbandonedReservationsPerClaim)),
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var row in abandoned)
        {
            await commandExecutor.ExecuteAsync(
                    NotificationPlatformSql.ResolveQuotaReservationById,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", row.Id),
                        ("StateKey", NotificationQuotaReservationStates.PendingReconciliation),
                        ("Now", now)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return abandoned.Count > 0;
    }

    /// <summary>按 Unix 时间把当前时刻对齐到固定窗口起点。</summary>
    private static DateTimeOffset AlignWindowStart(DateTimeOffset now, int windowSeconds)
    {
        var epochSeconds = now.ToUnixTimeSeconds();
        var aligned = epochSeconds - (epochSeconds % windowSeconds);
        return DateTimeOffset.FromUnixTimeSeconds(aligned);
    }

    /// <summary>预留被拒绝的内部信号；在事务内抛出以触发回滚，再由 <see cref="ReserveAsync"/> 转换为结果。</summary>
    private sealed class QuotaReservationRejectedException(
        NotificationQuotaReservationStatus status,
        string errorCode,
        int retryAfterSeconds)
        : Exception(errorCode)
    {
        public NotificationQuotaReservationStatus Status { get; } = status;

        public string ErrorCode { get; } = errorCode;

        public int RetryAfterSeconds { get; } = retryAfterSeconds;
    }
}
