using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.WeChatMiniProgram;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Notifications.Execution;

/// <summary>
/// 短事务领取租约，事务外调用 Adapter，再用 LeaseGeneration/Revision 提交 Attempt 与终态。
/// </summary>
internal sealed class NotificationDeliveryBatchProcessor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IEnumerable<INotificationProviderAdapter> adapters,
    NotificationAttachmentLoader attachmentLoader,
    Features.IntentAttachments.NotificationIntentAttachmentCoordinator attachmentCoordinator,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<NotificationDeliveryWorkerOptions> workerOptions,
    NotificationRecipientEndpointProtector recipientEndpointProtector,
    NotificationQuotaGovernor quotaGovernor,
    ILogger<NotificationDeliveryBatchProcessor> logger,
    IServiceScopeFactory executionScopeFactory)
{
    private readonly NotificationDeliveryWorkerOptions _options = workerOptions.Value;
    private readonly IReadOnlyDictionary<string, INotificationProviderAdapter> _adapters =
        adapters.ToDictionary(item => item.Descriptor.ProviderTypeKey, StringComparer.Ordinal);

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var processed = 0;
        while (processed < _options.BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var slots = Math.Min(_options.MaxConcurrency, _options.BatchSize - processed);
            var now = clock.UtcNow;
            // 只为可以立即执行的槽位领取租约，不让尚未获得执行机会的投递在本地排队过期。
            var claimed = await ClaimAsync(idGenerator.NewId().ToString("N"), now,
                now.AddSeconds(_options.LeaseSeconds), slots, cancellationToken).ConfigureAwait(false);
            if (claimed.Count == 0) break;
            if (claimed.Count > slots) throw new InvalidOperationException("Notification claim exceeded available execution slots.");
            if (_options.MaxConcurrency == 1)
            {
                await ProcessOneAsync(claimed[0], cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // 每个活动槽位拥有独立 DbSession 和可信 Host 上下文。
                await Parallel.ForEachAsync(claimed,
                    new ParallelOptions { MaxDegreeOfParallelism = slots, CancellationToken = cancellationToken },
                    async (delivery, token) =>
                    {
                        await using var scope = executionScopeFactory.CreateAsyncScope();
                        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                        currentTenant.SetHost();
                        try
                        {
                            await scope.ServiceProvider.GetRequiredService<NotificationDeliveryBatchProcessor>()
                                .ProcessOneAsync(delivery, token).ConfigureAwait(false);
                        }
                        finally { currentTenant.Clear(); }
                    }).ConfigureAwait(false);
            }
            processed += claimed.Count;
            if (claimed.Count < slots) break;
        }
        return processed;
    }

    public async Task SampleBacklogAsync(CancellationToken cancellationToken)
    {
        // 沿采样周期有界补偿，空闲发送队列也会执行；每条回执独立短事务，不拖长领取事务。
        await using (var receiptScope = executionScopeFactory.CreateAsyncScope())
        {
            receiptScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            try
            {
                await receiptScope.ServiceProvider.GetRequiredService<Features.ReceiveProviderReceipts.NotificationReceiptProcessor>()
                    .RecoverPendingAsync(databaseOptions.Value.Provider, cancellationToken).ConfigureAwait(false);
            }
            finally { receiptScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().Clear(); }
        }
        var now = clock.UtcNow;
        var count = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                NotificationPlatformSql.CountDeliveryBacklog,
                NotificationPlatformSqlParameters.Create(("Now", now)),
                cancellationToken)
            .ConfigureAwait(false);
        var oldest = await queryExecutor.QuerySingleOrDefaultAsync<DateTimeOffset?>(
                NotificationPlatformSql.OldestDeliveryBacklog,
                NotificationPlatformSqlParameters.Create(("Now", now)),
                cancellationToken)
            .ConfigureAwait(false);
        var age = oldest is { } created ? (now - created).TotalSeconds : 0d;
        NotificationDeliveryTelemetry.RecordBacklog(count, age);
        // 暂挂 unknown 与可自动执行的积压分开计数，供运维识别需要人工对账的工作。
        var awaitingReconciliation = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                NotificationPlatformSql.CountDeliveryAwaitingReconciliation,
                NotificationPlatformSqlParameters.Create(),
                cancellationToken)
            .ConfigureAwait(false);
        NotificationDeliveryTelemetry.RecordAwaitingReconciliation(awaitingReconciliation);
        var deadLetters = await queryExecutor.QuerySingleOrDefaultAsync<long>(NotificationLifecycleSql.CountDeadLetters,
            NotificationPlatformSqlParameters.Create(), cancellationToken).ConfigureAwait(false);
        NotificationDeliveryTelemetry.RecordDeadLetters(deadLetters);
    }

    private async Task<IReadOnlyList<NotificationDeliveryRecord>> ClaimAsync(
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        int take,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(take, 1, 50);
        if (databaseOptions.Value.Provider == DatabaseProvider.SqlServer)
        {
            var rows = await queryExecutor.QueryAsync<NotificationDeliveryRecord>(
                    NotificationPlatformSql.ClaimDeliveriesSqlServer,
                    NotificationPlatformSqlParameters.Create(
                        ("BatchSize", batchSize),
                        ("Now", now),
                        ("LeaseOwnerKey", leaseOwner),
                        ("LeaseExpiresAtUtc", leaseExpiresAt)),
                    cancellationToken)
                .ConfigureAwait(false);
            return rows.ToArray();
        }

        return await transaction.ExecuteAsync(
                async token =>
                {
                    var ids = await queryExecutor.QueryAsync<Guid>(
                            NotificationPlatformSql.SelectClaimableDeliveryIdsMySql,
                            NotificationPlatformSqlParameters.Create(
                                ("BatchSize", batchSize),
                                ("Now", now)),
                            token)
                        .ConfigureAwait(false);
                    if (ids.Count == 0)
                    {
                        return Array.Empty<NotificationDeliveryRecord>();
                    }

                    await commandExecutor.ExecuteAsync(
                            NotificationPlatformSql.ClaimDeliveriesByIdsMySql,
                            NotificationPlatformSqlParameters.Create(
                                ("Ids", ids.ToArray()),
                                ("LeaseOwnerKey", leaseOwner),
                                ("LeaseExpiresAtUtc", leaseExpiresAt),
                                ("Now", now)),
                            token)
                        .ConfigureAwait(false);
                    var rows = await queryExecutor.QueryAsync<NotificationDeliveryRecord>(
                            NotificationPlatformSql.SelectDeliveriesByLease,
                            NotificationPlatformSqlParameters.Create(("LeaseOwnerKey", leaseOwner)),
                            token)
                        .ConfigureAwait(false);
                    return rows.ToArray();
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>执行单条投递，在准备前和外发前确认所有权，防止批次排队耗尽租约后继续发送。</summary>
    /// <param name="delivery">领取时的投递和租约快照。</param>
    /// <param name="cancellationToken">停止处理的取消令牌。</param>
    private async Task ProcessOneAsync(
        NotificationDeliveryRecord delivery,
        CancellationToken cancellationToken)
    {
        var owned = await RenewLeaseAsync(delivery, cancellationToken).ConfigureAwait(false);
        if (owned is null)
        {
            return;
        }

        delivery = owned;
        var budgetUsed = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                NotificationPlatformSql.FindAttemptBudget,
                NotificationPlatformSqlParameters.Create(("Id", delivery.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        var started = clock.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        string providerType = "unknown";
        string resultCategory;
        string? providerMessageId = null;
        string? suppressionReasonCode = null;
        TimeSpan? retryAfter = null;
        var providerCallStarted = false;
        // 由草拟调用的 Adapter 声明是否具备幂等/对账能力，决定结果未知时能否自动重试。
        var allowsRetryAfterUnknown = false;
        // 标记本轮是否真正预留了配额，决定提交事务内是否需要核销、释放或暂挂对账。
        var quotaReserved = false;
        var subscriptionReserved = false;
        // 静默时段等治理延迟的下一可发送时间；由政策求值给出，直接持久化而不走指数退避。
        DateTimeOffset? deferUntil = null;
        try
        {
            if (budgetUsed > _options.MaxAttempts)
            {
                // 中断的领取也计入预算；耗尽后不得再进入外部调用。
                resultCategory = NotificationDeliveryRetry.Transient;
            }
            else
            {
            var prepared = await PrepareRequestAsync(delivery, cancellationToken).ConfigureAwait(false);
            if (prepared is null)
            {
                resultCategory = NotificationDeliveryRetry.Permanent;
            }
            else if (prepared.DeferUntilUtc is { } deferredAt)
            {
                // 外发前复检发现已进入静默时段：不调用厂商，按声明时区延后到下一可发送时间。
                resultCategory = NotificationDeliveryRetry.QuietHoursDeferred;
                deferUntil = deferredAt;
            }
            else if (prepared.SuppressionReasonCode is not null)
            {
                // 既有 Unknown 不能因撤回或过期而变成“未外发”；只停止后续自动尝试。
                resultCategory = delivery.StatusKey == "unknown" ? NotificationDeliveryRetry.Unknown : "suppressed";
                suppressionReasonCode = prepared.SuppressionReasonCode;
            }
            else
            {
                // 附件准备也可能耗时；外发前再次确认数据库仍承认本轮所有权。
                owned = await RenewLeaseAsync(delivery, cancellationToken).ConfigureAwait(false);
                if (owned is null)
                {
                    return;
                }

                delivery = owned;
                providerType = prepared.ProviderTypeKey;
                // 外发前最后一道治理闸门：按 Provider/租户/Producer/Scene 原子预留有界配额。
                // 缺失声明失败关闭，窗口已满进入延后重试，两者都不调用厂商。
                var quota = await quotaGovernor.ReserveAsync(
                        delivery.Id,
                        delivery.LeaseGeneration,
                        prepared.TenantScopeKey,
                        prepared.ProviderTypeKey,
                        prepared.ProducerKey,
                        prepared.SceneKey,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (quota.Status == NotificationQuotaReservationStatus.LostLease)
                {
                    return;
                }
                if (quota.Status == NotificationQuotaReservationStatus.Missing)
                {
                    resultCategory = NotificationDeliveryRetry.QuotaMissing;
                }
                else if (quota.Status == NotificationQuotaReservationStatus.PendingReconciliation)
                {
                    await commandExecutor.ExecuteAsync(NotificationSubscriptionGrantSql.SuspendAbandoned,
                        NotificationPlatformSqlParameters.Create(("DeliveryId", delivery.Id),
                            ("LeaseGeneration", delivery.LeaseGeneration)), cancellationToken).ConfigureAwait(false);
                    // 旧世代预留不能证明未发送，当前投递暂挂，禁止凭租约过期再次调用厂商。
                    resultCategory = NotificationDeliveryRetry.Unknown;
                }
                else if (quota.Status == NotificationQuotaReservationStatus.Exceeded)
                {
                    resultCategory = NotificationDeliveryRetry.QuotaBlocked;
                    retryAfter = TimeSpan.FromSeconds(quota.RetryAfterSeconds);
                }
                else
                {
                    quotaReserved = true;
                    // 附件读取和配额事务可能等待；在这些等待之后重新读取权威意愿与目录，
                    // 不能把准备阶段的同意快照当作外发许可。确定未调用时在完成事务内释放本轮配额。
                    prepared = await RecheckPreparedSendAsync(delivery, prepared, cancellationToken)
                        .ConfigureAwait(false);
                    if (prepared is null)
                    {
                        resultCategory = NotificationDeliveryRetry.Permanent;
                    }
                    else if (prepared.SuppressionReasonCode is not null)
                    {
                        resultCategory = delivery.StatusKey == "unknown" ? NotificationDeliveryRetry.Unknown : "suppressed";
                        suppressionReasonCode = prepared.SuppressionReasonCode;
                    }
                    else if (prepared.DeferUntilUtc is { } finalDeferredAt)
                    {
                        resultCategory = NotificationDeliveryRetry.QuietHoursDeferred;
                        deferUntil = finalDeferredAt;
                    }
                    else
                    {
                        owned = await RenewLeaseAsync(delivery, cancellationToken).ConfigureAwait(false);
                        if (owned is null) return;
                        delivery = owned;
                        var subscription = await ReserveSubscriptionAsync(delivery, prepared, cancellationToken)
                            .ConfigureAwait(false);
                        if (subscription < 0)
                        {
                            resultCategory = NotificationDeliveryRetry.Unknown;
                        }
                        else if (subscription == 0)
                        {
                            resultCategory = "suppressed";
                            suppressionReasonCode = NotificationsErrorCodes.SubscriptionNotAuthorized;
                        }
                        else
                        {
                        subscriptionReserved = prepared.ProviderTypeKey == WeChatMiniProgramNotificationProviderAdapter.ProviderTypeKeyValue;
                        if (!await EnterSendBoundaryAsync(delivery, prepared, cancellationToken).ConfigureAwait(false))
                        {
                            // 尚未开始外发，撤回或过期的赢家阻止本轮调用；释放本轮确定未使用的许可。
                            await transaction.ExecuteAsync(async token =>
                            {
                                await quotaGovernor.ResolveAsync(delivery.Id, delivery.LeaseGeneration,
                                    NotificationQuotaReservationStates.Released, token).ConfigureAwait(false);
                                await commandExecutor.ExecuteAsync(NotificationSubscriptionGrantSql.Resolve,
                                    NotificationPlatformSqlParameters.Create(("DeliveryId", delivery.Id),
                                        ("LeaseGeneration", delivery.LeaseGeneration), ("StateKey", "available")), token).ConfigureAwait(false);
                                return 0;
                            }, cancellationToken).ConfigureAwait(false);
                            return;
                        }
                        allowsRetryAfterUnknown = prepared.Adapter.AllowsAutomaticRetryAfterUnknownResult;
                        using var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        providerCancellation.CancelAfter(TimeSpan.FromSeconds(_options.LeaseSeconds * 0.8));
                        providerCallStarted = true;
                        var send = await prepared.Adapter.SendAsync(prepared.Request!, providerCancellation.Token)
                            .ConfigureAwait(false);
                        resultCategory = NormalizeCategory(send);
                        providerMessageId = send.ProviderMessageId;
                        retryAfter = send.RetryAfter;
                        }
                    }
                }
            }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !providerCallStarted)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // 外部调用启动后的超时或宿主取消都无法证明未发送，必须计入 unknown 和预算。
            resultCategory = NotificationDeliveryRetry.Unknown;
        }
        catch (Exception exception)
        {
            // 调用前失败仍计入瞬时预算；调用后异常按 unknown 暂挂，避免无限绕过 MaxAttempts。
            resultCategory = providerCallStarted
                ? NotificationDeliveryRetry.Unknown
                : NotificationDeliveryRetry.Transient;
            logger.LogWarning(exception, "Notification delivery attempt failed with category {ResultCategory}.", resultCategory);
        }

        stopwatch.Stop();
        var lastAttemptNumber = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                NotificationPlatformSql.FindLastAttemptNumber,
                NotificationPlatformSqlParameters.Create(("DeliveryId", delivery.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        // 尝试序号保留历史记录顺序；自动重试上限独立使用持久化预算，不受治理领取或人工重试影响。
        var attemptNumber = NotificationDeliveryRetry.ResolveAttemptNumber(
            lastAttemptNumber,
            budgetUsed);
        var now = clock.UtcNow;
        var (status, nextAttempt) = resultCategory == "suppressed"
            ? ("suppressed", (DateTimeOffset?)null)
            : NotificationDeliveryRetry.ResolveDeliveryOutcome(
                resultCategory,
                (int)Math.Min(budgetUsed, int.MaxValue),
                now,
                retryAfter,
                _options,
                allowsRetryAfterUnknown,
                deferUntil);
        var failoverTarget = await ResolveFailoverTargetAsync(delivery, resultCategory, cancellationToken)
            .ConfigureAwait(false);
        if (failoverTarget is not null)
        {
            // 故障转移只推进备用目标，当前目标进入失败终态，防止同一 Profile 与备用同时重试。
            status = "failed";
            nextAttempt = null;
        }

        var committed = await transaction.ExecuteAsync(
                async token =>
                {
                    var completed = await commandExecutor.ExecuteAsync(
                            NotificationPlatformSql.CompleteDelivery,
                            NotificationPlatformSqlParameters.Create(
                                ("Id", delivery.Id),
                                ("StatusKey", status),
                                ("NextAttemptAtUtc", nextAttempt),
                                ("SuppressionReasonCode", suppressionReasonCode),
                                ("Now", now),
                                ("LeaseOwnerKey", delivery.LeaseOwnerKey),
                                ("LeaseGeneration", delivery.LeaseGeneration),
                                ("Revision", delivery.Revision)),
                            token)
                        .ConfigureAwait(false);
                    if (completed == 0)
                    {
                        return 0;
                    }

                    if (resultCategory is NotificationDeliveryRetry.QuotaBlocked
                        or NotificationDeliveryRetry.QuotaMissing
                        or NotificationDeliveryRetry.QuietHoursDeferred)
                    {
                        await commandExecutor.ExecuteAsync(
                                NotificationPlatformSql.RefundGovernanceAttemptBudget,
                                NotificationPlatformSqlParameters.Create(
                                    ("Id", delivery.Id),
                                    ("LeaseGeneration", delivery.LeaseGeneration),
                                    ("Revision", delivery.Revision + 1)),
                                token)
                            .ConfigureAwait(false);
                    }

                    // 配额阻塞、缺失配置与静默延迟都没有调用厂商，不写入 Attempt、也不消耗厂商尝试预算。
                    if (resultCategory is not (NotificationDeliveryRetry.QuotaBlocked
                        or NotificationDeliveryRetry.QuotaMissing
                        or NotificationDeliveryRetry.QuietHoursDeferred))
                    {
                        await commandExecutor.ExecuteAsync(
                                NotificationPlatformSql.InsertAttempt,
                                NotificationPlatformSqlParameters.Create(
                                    ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                                    ("Id", idGenerator.NewId()),
                                    ("DeliveryId", delivery.Id),
                                    ("AttemptNumber", attemptNumber),
                                    ("LeaseOwnerKey", delivery.LeaseOwnerKey),
                                    ("LeaseGeneration", delivery.LeaseGeneration),
                                    ("LeaseExpiresAtUtc", delivery.LeaseExpiresAtUtc),
                                    ("ResultCategoryKey", resultCategory),
                                    ("StatusKey", resultCategory == NotificationDeliveryRetry.Succeeded
                                        ? "succeeded"
                                        : "failed"),
                                    ("ProviderMessageId", providerMessageId),
                                    ("ErrorCode", resultCategory == NotificationDeliveryRetry.Succeeded
                                        ? null
                                        : resultCategory),
                                    ("ReceiptDigest", (string?)null),
                                    ("StartedAtUtc", started),
                                    ("FinishedAtUtc", now)),
                                token)
                            .ConfigureAwait(false);
                    }

                    if (failoverTarget is { } target)
                    {
                        await commandExecutor.ExecuteAsync(
                                NotificationPlatformSql.InsertDeliveryIfAbsent,
                                NotificationPlatformSqlParameters.Create(
                                    ("Id", idGenerator.NewId()),
                                    ("IntentId", delivery.IntentId),
                                    ("RecipientId", delivery.RecipientId),
                                    ("ChannelKey", delivery.ChannelKey),
                                    ("ProviderProfileVersionId", target),
                                    ("BindingVersionId", delivery.BindingVersionId),
                                    ("StatusKey", "accepted"),
                                    ("NextAttemptAtUtc", now),
                                    ("CreatedAtUtc", now),
                                    ("ContentSnapshotJson", delivery.ContentSnapshotJson),
                                    ("PreferenceVersion", delivery.PreferenceVersion),
                                    ("PolicyDecisionKey", delivery.PolicyDecisionKey),
                                    ("SuppressionReasonCode", delivery.SuppressionReasonCode)
                                ),
                                token)
                            .ConfigureAwait(false);
                    }

                    // 与投递终态同事务收敛配额：成功核销、确定未发送释放、未知暂挂对账。
                    if (quotaReserved)
                    {
                        await quotaGovernor.ResolveAsync(
                                delivery.Id,
                                delivery.LeaseGeneration,
                                ResolveQuotaReservationState(resultCategory),
                                token)
                            .ConfigureAwait(false);
                    }

                    if (subscriptionReserved)
                    {
                        await commandExecutor.ExecuteAsync(NotificationSubscriptionGrantSql.Resolve,
                            NotificationPlatformSqlParameters.Create(
                                ("DeliveryId", delivery.Id), ("LeaseGeneration", delivery.LeaseGeneration),
                                ("StateKey", resultCategory == NotificationDeliveryRetry.Unknown ? "pending_reconciliation"
                                    : resultCategory == NotificationDeliveryRetry.Succeeded ? "consumed" : "available")), token)
                            .ConfigureAwait(false);
                    }

                    return completed;
                },
                cancellationToken)
            .ConfigureAwait(false);
        NotificationDeliveryTelemetry.RecordAttempt(
            providerType,
            delivery.ChannelKey,
            resultCategory,
            stopwatch.Elapsed.TotalMilliseconds);
        if (resultCategory is NotificationDeliveryRetry.QuotaBlocked or NotificationDeliveryRetry.QuotaMissing)
        {
            // 受配额阻塞或缺失配置的投递数量单独计数，供运维定位受影响范围并补配额度。
            NotificationDeliveryTelemetry.RecordQuotaRejected(resultCategory);
        }

        // Attempt 已提交：若同一提供程序消息号的回执先到，这里做有界补偿重关联，
        // 避免提前回执永久停留在待关联。补偿失败不影响已提交的投递终态。
        if (committed > 0 && providerCallStarted && !string.IsNullOrEmpty(providerMessageId))
        {
            await TryReconcilePendingReceiptsAsync(providerType, providerMessageId, cancellationToken)
                .ConfigureAwait(false);
        }

        await attachmentCoordinator
            .TryReleaseIfTerminalAsync(delivery.IntentId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>在独立作用域内补偿重关联提前到达的回执，失败只记录日志不影响投递终态。</summary>
    /// <param name="providerTypeKey">渠道类型键。</param>
    /// <param name="providerMessageId">提供程序消息号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task TryReconcilePendingReceiptsAsync(
        string providerTypeKey,
        string providerMessageId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = executionScopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var processor = scope.ServiceProvider
                .GetRequiredService<Features.ReceiveProviderReceipts.NotificationReceiptProcessor>();
            var reconciled = await processor
                .ReconcilePendingForMessageAsync(providerTypeKey, providerMessageId, cancellationToken)
                .ConfigureAwait(false);
            if (reconciled > 0)
            {
                logger.LogInformation(
                    "Reconciled {Count} early provider receipts for {ProviderType}.",
                    reconciled,
                    providerTypeKey);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to reconcile early provider receipts for {ProviderType}.",
                providerTypeKey);
        }
    }

    /// <summary>在 Failover 模式下按不可变路由快照选择下一个备用 ProfileVersion。</summary>
    private async Task<Guid?> ResolveFailoverTargetAsync(
        NotificationDeliveryRecord delivery,
        string resultCategory,
        CancellationToken cancellationToken)
    {
        if (resultCategory is not (NotificationDeliveryRetry.Transient or NotificationDeliveryRetry.RateLimited)
            || delivery.ProviderProfileVersionId is not { } currentProfileVersionId)
        {
            return null;
        }

        var intent = await queryExecutor.QuerySingleOrDefaultAsync<NotificationIntentRecord>(
                NotificationPlatformSql.FindIntentByIdUnscoped,
                NotificationPlatformSqlParameters.Create(("Id", delivery.IntentId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (intent is null || !string.Equals(intent.DispatchModeKey, "failover", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(intent.RouteSnapshotJson);
            // 使用值元组而非匿名类型，避免触发 Native AOT 匿名参数规则；该投影仅用于本地排序，不涉及 SQL 参数绑定。
            var ordered = document.RootElement.GetProperty("targets").EnumerateArray()
                .Select(item => (
                    Order: item.GetProperty("order").GetInt32(),
                    ProfileVersionId: Guid.TryParse(item.GetProperty("profileVersionId").GetString(), out var id)
                        ? id
                        : Guid.Empty))
                .Where(item => item.ProfileVersionId != Guid.Empty)
                .OrderBy(item => item.Order)
                .ToArray();
            return ordered
                .SkipWhile(item => item.ProfileVersionId != currentProfileVersionId)
                .Skip(1)
                .Select(item => (Guid?)item.ProfileVersionId)
                .FirstOrDefault();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>通过条件更新确认并续展当前租约，不复活已经过期或换代的执行权。</summary>
    /// <param name="delivery">当前执行者持有的领取快照。</param>
    /// <param name="cancellationToken">取消数据库操作的令牌。</param>
    private async Task<NotificationDeliveryRecord?> RenewLeaseAsync(
        NotificationDeliveryRecord delivery,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (delivery.LeaseExpiresAtUtc is not { } expiresAt || expiresAt <= now
            || string.IsNullOrEmpty(delivery.LeaseOwnerKey))
        {
            return null;
        }

        var renewedExpiry = now.AddSeconds(_options.LeaseSeconds);
        var renewed = await commandExecutor.ExecuteAsync(
            NotificationPlatformSql.RenewDeliveryLease,
            NotificationPlatformSqlParameters.Create(
                ("Id", delivery.Id), ("LeaseOwnerKey", delivery.LeaseOwnerKey),
                ("LeaseGeneration", delivery.LeaseGeneration), ("Revision", delivery.Revision),
                ("Now", now), ("LeaseExpiresAtUtc", renewedExpiry)), cancellationToken).ConfigureAwait(false);
        return renewed == 1 ? delivery with { LeaseExpiresAtUtc = renewedExpiry } : null;
    }

    private async Task<PreparedSend?> PrepareRequestAsync(
        NotificationDeliveryRecord delivery,
        CancellationToken cancellationToken)
    {
        if (delivery.ProviderProfileVersionId is not { } profileVersionId)
        {
            return null;
        }

        var profileVersion = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(
                NotificationPlatformSql.FindProfileVersionById,
                NotificationPlatformSqlParameters.Create(("Id", profileVersionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (profileVersion is null || !_adapters.TryGetValue(profileVersion.ProviderTypeKey, out var adapter))
        {
            return null;
        }

        var intent = await queryExecutor.QuerySingleOrDefaultAsync<NotificationIntentRecord>(
                NotificationPlatformSql.FindIntentByIdUnscoped,
                NotificationPlatformSqlParameters.Create(("Id", delivery.IntentId)),
                cancellationToken)
            .ConfigureAwait(false);
        var recipient = await queryExecutor.QuerySingleOrDefaultAsync<NotificationRecipientRecord>(
                NotificationPlatformSql.FindRecipientById,
                NotificationPlatformSqlParameters.Create(("Id", delivery.RecipientId)),
                cancellationToken)
            .ConfigureAwait(false);
        var templateVersion = intent is null
            ? null
            : await queryExecutor.QuerySingleOrDefaultAsync<NotificationTemplateVersionRecord>(
                    NotificationPlatformSql.FindTemplateVersionById,
                    NotificationPlatformSqlParameters.Create(("Id", intent.TemplateVersionId)),
                    cancellationToken)
                .ConfigureAwait(false);
        if (intent is null || recipient is null || templateVersion is null)
        {
            return null;
        }
        var metadata = NotificationMessageMetadata.Read(intent.RouteSnapshotJson);
        if (intent.StatusKey == "revoked" || metadata.ExpiresAtUtc <= clock.UtcNow)
            return new PreparedSend(profileVersion.ProviderTypeKey, adapter, null,
                intent.StatusKey == "revoked" ? "notifications.intent_revoked" : "notifications.message_expired",
                intent.TenantScopeKey, intent.ProducerKey, intent.SceneKey);

        // 外发前重新读取受控目录：目录缺失、停用或渠道不再允许时失败关闭，
        // 避免对已撤销声明的场景继续外发。
        var catalog = await Features.ManageProducerScenes.NotificationProducerSceneCatalog.FindAsync(
                queryExecutor,
                intent.TenantScopeKey,
                intent.ProducerKey,
                intent.SceneKey,
                cancellationToken)
            .ConfigureAwait(false);
        if (catalog is null
            || !catalog.IsEnabled
            || !Features.ManageProducerScenes.NotificationProducerSceneCatalog.IsChannelAllowed(catalog, delivery.ChannelKey))
        {
            return null;
        }

        if (recipient.UserId is { } preferenceUserId)
        {
            var latestPreference = await queryExecutor.QuerySingleOrDefaultAsync<NotificationPreferenceRecord>(
                    NotificationPlatformSql.FindPreferenceByUserChannel,
                    NotificationPlatformSqlParameters.Create(
                        ("TenantScopeKey", intent.TenantScopeKey),
                        ("UserId", preferenceUserId),
                        ("ChannelKey", delivery.ChannelKey)),
                    cancellationToken)
                .ConfigureAwait(false);
            var (evaluation, quietHoursResumeAt) = EvaluatePolicy(
                catalog.PolicyCategoryKey,
                catalog.EmergencyOverrideAllowed,
                latestPreference,
                clock.UtcNow);
            if (evaluation.IsSuppressed)
            {
                return new PreparedSend(
                    profileVersion.ProviderTypeKey,
                    adapter,
                    null,
                    evaluation.SuppressionReasonCode,
                    intent.TenantScopeKey,
                    intent.ProducerKey,
                    intent.SceneKey);
            }

            if (evaluation.ShouldDelayForQuietHours)
            {
                // 外发前复检发现已进入静默时段：不调用厂商，按声明时区延后到下一可发送时间。
                return new PreparedSend(
                    profileVersion.ProviderTypeKey,
                    adapter,
                    null,
                    null,
                    intent.TenantScopeKey,
                    intent.ProducerKey,
                    intent.SceneKey,
                    quietHoursResumeAt);
            }
        }

        var recipientEndpoint = recipient.RecipientKey;
        if (adapter.RecipientEndpointKindKey is { } endpointKindKey)
        {
            if (recipient.UserId is not { } userId)
            {
                return null;
            }

            var protectedValue = await queryExecutor.QuerySingleOrDefaultAsync<string>(
                    NotificationRecipientEndpointSql.FindVerifiedProtectedForDelivery,
                    NotificationPlatformSqlParameters.Create(
                        ("TenantScopeKey", intent.TenantScopeKey),
                        ("UserId", userId),
                        ("ProviderProfileVersionId", profileVersionId),
                        ("EndpointKindKey", endpointKindKey)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(protectedValue))
            {
                return null;
            }

            try
            {
                recipientEndpoint = recipientEndpointProtector.Unprotect(protectedValue);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return null;
            }
        }

        var rendered = TryReadContentSnapshot(delivery.ContentSnapshotJson);
        if (rendered is null)
        {
            var fallback = NotificationTemplateCompiler.Render(
                templateVersion.Subject,
                templateVersion.BodyJson,
                intent.ParameterSnapshotJson);
            rendered = fallback.IsSuccess
                ? new RenderedContentSnapshot(fallback.Value!.Title, fallback.Value.Content)
                : new RenderedContentSnapshot(templateVersion.Subject, string.Empty);
        }

        var subject = rendered.Subject;
        var body = rendered.Body;

        // 小程序订阅消息按次消耗：发送前按用户、App 与模板确认仍有有效订阅授权，未授权不发。
        if (string.Equals(
                profileVersion.ProviderTypeKey,
                WeChatMiniProgramNotificationProviderAdapter.ProviderTypeKeyValue,
                StringComparison.Ordinal)
            && !await HasAuthorizedWeChatMiniProgramSubscriptionAsync(
                    intent.TenantScopeKey,
                    recipient.UserId,
                    profileVersion.NonSecretConfigJson,
                    subject,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return new PreparedSend(
                profileVersion.ProviderTypeKey,
                adapter,
                null,
                NotificationsErrorCodes.SubscriptionNotAuthorized,
                intent.TenantScopeKey,
                intent.ProducerKey,
                intent.SceneKey);
        }

        var loadedAttachments = await attachmentLoader
            .LoadForIntentAsync(intent.Id, cancellationToken)
            .ConfigureAwait(false);
        if (!loadedAttachments.IsSuccess)
        {
            return null;
        }

        var request = new NotificationProviderRequest(
            delivery.Id,
            delivery.ChannelKey,
            recipientEndpoint,
            profileVersion.NonSecretConfigJson,
            profileVersion.SecretReference,
            subject,
            body,
            $"{intent.Id:N}:{recipient.Id:N}:{profileVersionId:N}",
            loadedAttachments.Value!);
        return new PreparedSend(
            profileVersion.ProviderTypeKey,
            adapter,
            request,
            null,
            intent.TenantScopeKey,
            intent.ProducerKey,
            intent.SceneKey);
    }

    /// <summary>耗时准备完成后重新校验可撤销的发送许可，不重渲染已冻结内容或重新读取附件。</summary>
    private async Task<PreparedSend?> RecheckPreparedSendAsync(
        NotificationDeliveryRecord delivery,
        PreparedSend prepared,
        CancellationToken cancellationToken)
    {
        var intent = await queryExecutor.QuerySingleOrDefaultAsync<NotificationIntentRecord>(
            NotificationPlatformSql.FindIntentByIdUnscoped,
            NotificationPlatformSqlParameters.Create(("Id", delivery.IntentId)), cancellationToken).ConfigureAwait(false);
        if (intent is null) return null;
        if (intent.StatusKey == "revoked" || NotificationMessageMetadata.Read(intent.RouteSnapshotJson).ExpiresAtUtc <= clock.UtcNow)
            return prepared with { SuppressionReasonCode = intent.StatusKey == "revoked"
                ? "notifications.intent_revoked" : "notifications.message_expired" };
        var catalog = await Features.ManageProducerScenes.NotificationProducerSceneCatalog.FindAsync(
            queryExecutor, prepared.TenantScopeKey, prepared.ProducerKey, prepared.SceneKey, cancellationToken)
            .ConfigureAwait(false);
        if (catalog is null || !catalog.IsEnabled
            || !Features.ManageProducerScenes.NotificationProducerSceneCatalog.IsChannelAllowed(catalog, delivery.ChannelKey))
        {
            return null;
        }

        var recipient = await queryExecutor.QuerySingleOrDefaultAsync<NotificationRecipientRecord>(
            NotificationPlatformSql.FindRecipientById,
            NotificationPlatformSqlParameters.Create(("Id", delivery.RecipientId)), cancellationToken)
            .ConfigureAwait(false);
        if (recipient is null) return null;
        if (recipient.UserId is not { } userId) return prepared;
        var preference = await queryExecutor.QuerySingleOrDefaultAsync<NotificationPreferenceRecord>(
            NotificationPlatformSql.FindPreferenceByUserChannel,
            NotificationPlatformSqlParameters.Create(
                ("TenantScopeKey", prepared.TenantScopeKey), ("UserId", userId), ("ChannelKey", delivery.ChannelKey)),
            cancellationToken).ConfigureAwait(false);
        var (evaluation, nextAllowedAt) = EvaluatePolicy(
            catalog.PolicyCategoryKey, catalog.EmergencyOverrideAllowed, preference, clock.UtcNow);
        return prepared with
        {
            SuppressionReasonCode = evaluation.IsSuppressed ? evaluation.SuppressionReasonCode : null,
            DeferUntilUtc = evaluation.ShouldDelayForQuietHours ? nextAllowedAt : null,
        };
    }

    /// <summary>
    /// 按用户、App 与模板检查小程序订阅授权；客户端上报接受但已撤销或状态非 accepted 时视为未授权。
    /// </summary>
    private async Task<bool> HasAuthorizedWeChatMiniProgramSubscriptionAsync(
        string tenantScopeKey,
        Guid? userId,
        string nonSecretConfigJson,
        string? subject,
        CancellationToken cancellationToken)
    {
        if (userId is not { } recipientUserId
            || !WeChatMiniProgramNotificationProviderAdapter.TryParseConfig(nonSecretConfigJson, out var config)
            || config is null)
        {
            return false;
        }

        // 与适配器一致：模板 Id 优先取内容主题，缺省回落到 Profile 的 defaultTemplateId。
        var templateId = subject?.Trim() ?? string.Empty;
        if (templateId.Length is < 1 or > 128)
        {
            templateId = config.DefaultTemplateId;
        }

        var count = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                WeChatMiniProgramBindingSql.CountAuthorizedSubscription,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", tenantScopeKey),
                    ("UserId", recipientUserId),
                    ("AppId", config.AppId),
                    ("TemplateId", templateId),
                    ("Revoked", WeChatMiniProgramBindingStatusKeys.Revoked),
                    ("Accepted", WeChatMiniProgramSubscriptionStatusKeys.Accepted)),
                cancellationToken)
            .ConfigureAwait(false);
        return count > 0;
    }

    /// <summary>在短事务中预留一份本地同意事实；旧世代的未决授权只能暂挂，不能再次外发。</summary>
    private Task<int> ReserveSubscriptionAsync(
        NotificationDeliveryRecord delivery, PreparedSend prepared, CancellationToken cancellationToken)
    {
        if (prepared.ProviderTypeKey != WeChatMiniProgramNotificationProviderAdapter.ProviderTypeKeyValue)
            return Task.FromResult(1);
        return ReserveCoreAsync();
        async Task<int> ReserveCoreAsync()
        {
            var request = prepared.Request!;
            if (!WeChatMiniProgramNotificationProviderAdapter.TryParseConfig(request.NonSecretConfigJson, out var config)
                || config is null) return 0;
            var recipient = await queryExecutor.QuerySingleOrDefaultAsync<NotificationRecipientRecord>(
                NotificationPlatformSql.FindRecipientById,
                NotificationPlatformSqlParameters.Create(("Id", delivery.RecipientId)), cancellationToken).ConfigureAwait(false);
            if (recipient?.UserId is not { } userId) return 0;
            var templateId = request.Subject.Trim();
            if (templateId.Length is < 1 or > 128) templateId = config.DefaultTemplateId;
            return await transaction.ExecuteAsync(async token =>
            {
                // 与撤回采用意图到投递的锁顺序，防止撤回释放后又登记新预留并随后中断。
                var intentId = await queryExecutor.QuerySingleOrDefaultAsync<Guid>(databaseOptions.Value.Provider == DatabaseProvider.MySql
                    ? NotificationLifecycleSql.LockIntentMySql : NotificationLifecycleSql.LockIntentSqlServer,
                    NotificationPlatformSqlParameters.Create(("Id", delivery.IntentId), ("TenantScopeKey", prepared.TenantScopeKey)), token)
                    .ConfigureAwait(false);
                if (intentId == Guid.Empty) return 0;
                var owned = await queryExecutor.QuerySingleOrDefaultAsync<Guid>(databaseOptions.Value.Provider == DatabaseProvider.MySql
                    ? NotificationPlatformSql.LockDeliveryForQuotaMySql : NotificationPlatformSql.LockDeliveryForQuotaSqlServer,
                    NotificationPlatformSqlParameters.Create(("Id", delivery.Id), ("LeaseGeneration", delivery.LeaseGeneration), ("Now", clock.UtcNow)), token)
                    .ConfigureAwait(false);
                if (owned == Guid.Empty) return 0;
                return await new NotificationSubscriptionGovernor(queryExecutor, commandExecutor, transaction, databaseOptions)
                    .ReserveAsync(delivery.Id, delivery.LeaseGeneration, prepared.TenantScopeKey, userId,
                        config.AppId, templateId, delivery.ProviderProfileVersionId!.Value, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>与业务撤回以同一意图行锁串行；提交外发标记后撤回只能报告无法保证收回。</summary>
    private Task<bool> EnterSendBoundaryAsync(NotificationDeliveryRecord delivery, PreparedSend prepared,
        CancellationToken cancellationToken) => transaction.ExecuteAsync(async token =>
    {
        var parameters = NotificationPlatformSqlParameters.Create(("Id", delivery.IntentId),
            ("TenantScopeKey", prepared.TenantScopeKey));
        var intentId = await queryExecutor.QuerySingleOrDefaultAsync<Guid>(
            databaseOptions.Value.Provider == DatabaseProvider.MySql
                ? NotificationLifecycleSql.LockIntentMySql : NotificationLifecycleSql.LockIntentSqlServer,
            parameters, token).ConfigureAwait(false);
        if (intentId == Guid.Empty) return false;
        var intent = await queryExecutor.QuerySingleOrDefaultAsync<NotificationIntentRecord>(
            NotificationPlatformSql.FindIntentByIdUnscoped, parameters, token).ConfigureAwait(false);
        if (intent is null) return false;
        var expired = NotificationMessageMetadata.Read(intent.RouteSnapshotJson).ExpiresAtUtc <= clock.UtcNow;
        // 意图行锁等待也可能很长；在最终边界内再读一次可撤销政策，随后才允许事务外调用。
        var finalGate = await RecheckPreparedSendAsync(delivery, prepared, token).ConfigureAwait(false);
        var subscriptionRevoked = false;
        if (prepared.ProviderTypeKey == WeChatMiniProgramNotificationProviderAdapter.ProviderTypeKeyValue)
        {
            var request = prepared.Request!;
            var recipient = await queryExecutor.QuerySingleOrDefaultAsync<NotificationRecipientRecord>(
                NotificationPlatformSql.FindRecipientById, NotificationPlatformSqlParameters.Create(("Id", delivery.RecipientId)), token)
                .ConfigureAwait(false);
            if (recipient?.UserId is not { } userId
                || !WeChatMiniProgramNotificationProviderAdapter.TryParseConfig(request.NonSecretConfigJson, out var config)
                || config is null) return false;
            var consent = NotificationPlatformSqlParameters.Create(("TenantScopeKey", prepared.TenantScopeKey),
                ("UserId", userId), ("AppId", config.AppId),
                ("TemplateId", request.Subject.Trim().Length is > 0 and <= 128 ? request.Subject.Trim() : config.DefaultTemplateId),
                ("Revoked", WeChatMiniProgramBindingStatusKeys.Revoked), ("Accepted", WeChatMiniProgramSubscriptionStatusKeys.Accepted));
            await queryExecutor.QuerySingleOrDefaultAsync<Guid>(databaseOptions.Value.Provider == DatabaseProvider.MySql
                ? NotificationSubscriptionGrantSql.LockBindingMySql : NotificationSubscriptionGrantSql.LockBindingSqlServer,
                consent, token).ConfigureAwait(false);
            var currentBinding = await queryExecutor.QuerySingleOrDefaultAsync<WeChatMiniProgramBindingRecord>(
                WeChatMiniProgramBindingSql.FindByUserApp, consent, token).ConfigureAwait(false);
            // 准备后发生账号重绑定时不能向旧 OpenId 发送，即使新账号已重新同意同一模板。
            subscriptionRevoked = currentBinding is null
                || currentBinding.VerificationStatusKey != WeChatMiniProgramBindingStatusKeys.Verified
                || currentBinding.OpenIdSha256Hex != WeChatMiniProgramOpenIdFingerprint.Compute(request.RecipientEndpoint);
            subscriptionRevoked |= await queryExecutor.QuerySingleOrDefaultAsync<long>(
                WeChatMiniProgramBindingSql.CountAuthorizedSubscription, consent, token).ConfigureAwait(false) == 0;
            // 新一次同意不能复活已经被撤销的旧预留，必须验证本轮确切许可仍然有效。
            subscriptionRevoked |= await queryExecutor.QuerySingleOrDefaultAsync<long>(
                NotificationSubscriptionGrantSql.CountSendableReservation,
                NotificationPlatformSqlParameters.Create(("DeliveryId", delivery.Id), ("LeaseGeneration", delivery.LeaseGeneration)), token)
                .ConfigureAwait(false) == 0;
        }
        parameters["Id"] = delivery.Id;
        parameters["IntentId"] = delivery.IntentId;
        parameters["LeaseGeneration"] = delivery.LeaseGeneration;
        parameters["Revision"] = delivery.Revision;
        parameters["LeaseOwnerKey"] = delivery.LeaseOwnerKey;
        parameters["Now"] = clock.UtcNow;
        if (expired || subscriptionRevoked || finalGate is null || finalGate.SuppressionReasonCode is not null
            || finalGate.DeferUntilUtc.HasValue)
        {
            var defer = !expired && !subscriptionRevoked && finalGate?.SuppressionReasonCode is null
                && finalGate?.DeferUntilUtc.HasValue == true;
            parameters["StatusKey"] = delivery.StatusKey == "unknown" ? "unknown" : defer ? "accepted" : "suppressed";
            parameters["NextAttemptAtUtc"] = defer ? finalGate?.DeferUntilUtc : null;
            parameters["SuppressionReasonCode"] = expired ? "notifications.message_expired" : subscriptionRevoked
                ? NotificationsErrorCodes.SubscriptionNotAuthorized : finalGate?.SuppressionReasonCode ?? "notifications.scene_unavailable";
            await commandExecutor.ExecuteAsync(NotificationPlatformSql.CompleteDelivery, parameters, token).ConfigureAwait(false);
            if (defer)
                await commandExecutor.ExecuteAsync(NotificationPlatformSql.RefundGovernanceAttemptBudget,
                    NotificationPlatformSqlParameters.Create(("Id", delivery.Id), ("LeaseGeneration", delivery.LeaseGeneration),
                        ("Revision", delivery.Revision + 1)), token).ConfigureAwait(false);
            return false;
        }
        return await commandExecutor.ExecuteAsync(NotificationLifecycleSql.EnterSendBoundary, parameters, token)
            .ConfigureAwait(false) > 0;
    }, cancellationToken);

    /// <summary>
    /// 求值发送政策：返回抑制判定，并在需要延迟时给出按声明时区换算的下一可发送时间。
    /// </summary>
    /// <param name="categoryKey">Producer/Scene 目录声明的政策类别键。</param>
    /// <param name="emergencyOverrideAllowed">目录是否允许紧急覆盖静默；不能用于强制开启营销。</param>
    /// <param name="preference">当前收件人渠道偏好快照，缺失时按默认意愿处理。</param>
    /// <param name="now">当前 UTC 时刻。</param>
    /// <returns>政策判定结果；静默延迟时携带下一可发送 UTC 时刻。</returns>
    /// <remarks>
    /// 静默延迟时刻直接取 <see cref="NotificationQuietHours.NextAllowedAt"/> 的换算结果，
    /// 避免上游用固定退避重算而在静默窗口内反复领取；声明不可解析时返回 <see langword="null"/>，
    /// 由 <see cref="NotificationDeliveryRetry.ResolveDeliveryOutcome"/> 回落到基础退避。
    /// </remarks>
    private static (NotificationPolicyEvaluation Evaluation, DateTimeOffset? NextAllowedAtUtc) EvaluatePolicy(
        string categoryKey,
        bool emergencyOverrideAllowed,
        NotificationPreferenceRecord? preference,
        DateTimeOffset now)
    {
        var category = categoryKey switch
        {
            "mandatory" => NotificationPolicyCategory.Mandatory,
            "transactional" => NotificationPolicyCategory.Transactional,
            "marketing" => NotificationPolicyCategory.Marketing,
            _ => NotificationPolicyCategory.Informational,
        };
        var inQuiet = preference?.QuietHoursJson is { } json
            && NotificationQuietHours.IsInQuietHours(json, now);
        var evaluation = NotificationPolicy.Evaluate(
            category,
            new NotificationPreferenceSnapshot(
                preference?.ChannelOptedOut ?? false,
                preference?.MarketingConsentGranted ?? false,
                inQuiet),
            emergencyOverride: emergencyOverrideAllowed);
        var nextAllowedAt = evaluation.ShouldDelayForQuietHours && preference?.QuietHoursJson is { } quietHoursJson
            ? NotificationQuietHours.NextAllowedAt(quietHoursJson, now)
            : null;
        return (evaluation, nextAllowedAt);
    }

    private static RenderedContentSnapshot? TryReadContentSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            return new RenderedContentSnapshot(
                root.GetProperty("subject").GetString() ?? string.Empty,
                root.GetProperty("body").GetString() ?? string.Empty);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static string NormalizeCategory(NotificationProviderResult send)
    {
        if (send.Accepted)
        {
            return NotificationDeliveryRetry.Succeeded;
        }

        return send.ResultCategory switch
        {
            NotificationDeliveryRetry.Transient => NotificationDeliveryRetry.Transient,
            NotificationDeliveryRetry.RateLimited => NotificationDeliveryRetry.RateLimited,
            NotificationDeliveryRetry.Permanent => NotificationDeliveryRetry.Permanent,
            _ => NotificationDeliveryRetry.Unknown,
        };
    }

    /// <summary>把厂商调用结果类别映射为配额预留的目标状态。</summary>
    private static string ResolveQuotaReservationState(string resultCategory) => resultCategory switch
    {
        NotificationDeliveryRetry.Succeeded => NotificationQuotaReservationStates.Consumed,
        NotificationDeliveryRetry.Unknown => NotificationQuotaReservationStates.PendingReconciliation,
        _ => NotificationQuotaReservationStates.Released,
    };

    /// <summary>外发前的准备结果；静默延迟时携带下一可发送 UTC 时刻而不是抑制原因。</summary>
    private sealed record PreparedSend(
        string ProviderTypeKey,
        INotificationProviderAdapter Adapter,
        NotificationProviderRequest? Request,
        string? SuppressionReasonCode,
        string TenantScopeKey,
        string ProducerKey,
        string SceneKey,
        DateTimeOffset? DeferUntilUtc = null);

    private sealed record RenderedContentSnapshot(string Subject, string Body);
}
