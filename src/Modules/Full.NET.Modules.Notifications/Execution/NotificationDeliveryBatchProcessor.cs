using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;
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
        var started = clock.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        string providerType = "unknown";
        string resultCategory;
        string? providerMessageId = null;
        TimeSpan? retryAfter = null;
        try
        {
            var prepared = await PrepareRequestAsync(delivery, cancellationToken).ConfigureAwait(false);
            if (prepared is null)
            {
                resultCategory = NotificationDeliveryRetry.Permanent;
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
                using var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                providerCancellation.CancelAfter(TimeSpan.FromSeconds(_options.LeaseSeconds * 0.8));
                var send = await prepared.Adapter.SendAsync(prepared.Request, providerCancellation.Token)
                    .ConfigureAwait(false);
                resultCategory = NormalizeCategory(send);
                providerMessageId = send.ProviderMessageId;
                retryAfter = send.RetryAfter;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // 本次内部超时无法证明未发送，计入 unknown 和有界退避；宿主取消由上面的分支处理。
            resultCategory = NotificationDeliveryRetry.Unknown;
        }
        catch (Exception exception)
        {
            // 崩溃窗口只释放本轮调用，不提交 Attempt/终态，以便租约过期后按同一幂等键重领。
            logger.LogWarning(exception, "Notification delivery attempt crashed before a provider result.");
            return;
        }

        stopwatch.Stop();
        var attemptNumber = (int)await queryExecutor.QuerySingleOrDefaultAsync<long>(
                NotificationPlatformSql.CountAttemptsByDelivery,
                NotificationPlatformSqlParameters.Create(("DeliveryId", delivery.Id)),
                cancellationToken)
            .ConfigureAwait(false) + 1;
        var now = clock.UtcNow;
        var (status, nextAttempt) = NotificationDeliveryRetry.ResolveDeliveryOutcome(
            resultCategory,
            attemptNumber,
            now,
            retryAfter,
            _options);
        await transaction.ExecuteAsync(
                async token =>
                {
                    var completed = await commandExecutor.ExecuteAsync(
                            NotificationPlatformSql.CompleteDelivery,
                            NotificationPlatformSqlParameters.Create(
                                ("Id", delivery.Id),
                                ("StatusKey", status),
                                ("NextAttemptAtUtc", nextAttempt),
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
                    return completed;
                },
                cancellationToken)
            .ConfigureAwait(false);
        NotificationDeliveryTelemetry.RecordAttempt(
            providerType,
            delivery.ChannelKey,
            resultCategory,
            stopwatch.Elapsed.TotalMilliseconds);
        await attachmentCoordinator
            .TryReleaseIfTerminalAsync(delivery.IntentId, cancellationToken)
            .ConfigureAwait(false);
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

        var rendered = NotificationTemplateCompiler.Render(
            templateVersion.Subject,
            templateVersion.BodyJson,
            intent.ParameterSnapshotJson);
        var subject = rendered.IsSuccess ? rendered.Value!.Title : templateVersion.Subject;
        var body = rendered.IsSuccess ? rendered.Value!.Content : string.Empty;
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
        return new PreparedSend(profileVersion.ProviderTypeKey, adapter, request);
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

    private sealed record PreparedSend(
        string ProviderTypeKey,
        INotificationProviderAdapter Adapter,
        NotificationProviderRequest Request);
}
