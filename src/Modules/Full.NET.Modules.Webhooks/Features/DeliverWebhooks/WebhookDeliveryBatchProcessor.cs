// Webhook 投递批处理器；领取出站记录、重试与毒消息隔离。
using System.Net;
using System.Net.Http.Headers;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Delivery;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks.Persistence;
using Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;
using Full.NET.Modules.Webhooks.Features.ManageWebhookDeliveries.Persistence;
using Full.NET.Modules.Webhooks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Webhooks.Features.DeliverWebhooks;

/// <summary>Webhook 投递批处理器；领取出站记录、重试与毒消息隔离。</summary>
internal sealed class WebhookDeliveryBatchProcessor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IHttpClientFactory httpClientFactory,
    WebhookSigningSecretProtector signingSecretProtector,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<WebhookDeliveryWorkerOptions> workerOptions,
    ILogger<WebhookDeliveryBatchProcessor> logger,
    IIdGenerator idGenerator)
{
    private readonly WebhookDeliveryWorkerOptions _options = workerOptions.Value;

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var processed = 0;
        // 只有实际执行槽空闲才领取下一条，慢目标不会消耗尚未开始记录的租约。
        for (var index = 0; index < Math.Clamp(_options.BatchSize, 1, 50); index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = await ClaimAsync(cancellationToken).ConfigureAwait(false);
            if (items.Count == 0) break;
            await DeliverOneAsync(items[0], cancellationToken).ConfigureAwait(false);
            processed++;
        }
        return processed;
    }

    internal async Task<IReadOnlyList<WebhookDeliveryWorkItem>> ClaimAsync(
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var parameters = WebhookSqlParameters.Create(
            ("BatchSize", 1), ("Now", now),
            ("ExpiredBefore", now.AddSeconds(-LeaseSeconds)), ("PendingStatus", WebhookDeliveryStatuses.Pending));
        return await transaction.ExecuteAsync(async token =>
        {
            var rows = await queryExecutor.QueryAsync<WebhookDeliveryWorkItem>(
                databaseOptions.Value.Provider == DatabaseProvider.SqlServer
                    ? WebhookDeliverySql.ClaimPendingSqlServer : WebhookDeliverySql.ClaimPendingMySql,
                parameters, token).ConfigureAwait(false);
            var claimed = new List<WebhookDeliveryWorkItem>();
            foreach (var row in rows)
            {
                var affected = await commandExecutor.ExecuteAsync(WebhookDeliverySql.Claim,
                    WebhookSqlParameters.Create(("DeliveryId", row.Id), ("Version", row.Version),
                        ("Now", now), ("ExpiredBefore", now.AddSeconds(-LeaseSeconds))), token).ConfigureAwait(false);
                if (affected == 1)
                {
                    var attemptId = idGenerator.NewId();
                    if (attemptId == Guid.Empty) throw new InvalidOperationException("The attempt identifier is invalid.");
                    var recorded = await commandExecutor.ExecuteAsync(WebhookDeliveryAttemptSql.Start,
                        WebhookSqlParameters.Create(("AttemptId", attemptId), ("TenantId", row.TenantId), ("SubscriptionId", row.SubscriptionId),
                            ("DeliveryId", row.Id), ("ClaimVersion", row.Version + 1), ("StartedAtUtc", now)), token).ConfigureAwait(false);
                    if (recorded != 1) throw new InvalidOperationException("The claim has no matching attempt start.");
                    claimed.Add(row with { Version = row.Version + 1, AttemptId = attemptId });
                }
            }
            return (IReadOnlyList<WebhookDeliveryWorkItem>)claimed;
        }, cancellationToken).ConfigureAwait(false);
    }

    private int LeaseSeconds => Math.Clamp(_options.LeaseSeconds, 60, 3600);

    private async Task DeliverOneAsync(
        WebhookDeliveryWorkItem item,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var owned = await commandExecutor.ExecuteAsync(WebhookDeliverySql.RenewLease,
            WebhookSqlParameters.Create(("DeliveryId", item.Id), ("Version", item.Version),
                ("Now", now), ("ExpiredBefore", now.AddSeconds(-LeaseSeconds))), cancellationToken).ConfigureAwait(false);
        if (owned != 1) return;
        using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptCancellation.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, LeaseSeconds - 5)));
        AttemptOutcome outcome;
        try
        {
            outcome = await SendAsync(item, attemptCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            outcome = new("Webhook delivery timed out.", "webhooks.delivery.timeout", null);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Webhook delivery {DeliveryId} to {TargetUrl} failed.",
                item.Id,
                item.TargetUrl);
            outcome = new(exception.Message, "webhooks.delivery.transport_error", null);
        }

        // 落账异常必须向上传播并保留未确认尝试，不能被外发异常分支误记为接收端失败。
        if (outcome.Error is null)
            await MarkDeliveredAsync(item, outcome.HttpStatus!.Value, cancellationToken).ConfigureAwait(false);
        else
            await MarkFailureAsync(item, outcome.Error, outcome.ErrorCode!, outcome.HttpStatus, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AttemptOutcome> SendAsync(WebhookDeliveryWorkItem item, CancellationToken token)
    {
        if (!Uri.TryCreate(item.TargetUrl, UriKind.Absolute, out var targetUri))
            return new("Target URL is invalid.", "webhooks.delivery.target_invalid", null);
        var (allowed, reason) = await WebhookDeliverySsrfGuard.ValidateAsync(targetUri, token).ConfigureAwait(false);
        if (!allowed) return new(reason ?? "Target URL is blocked.", "webhooks.delivery.target_blocked", null);
        var secret = signingSecretProtector.Unprotect(item.SigningSecretHash);
        var stamp = new WebhookSignatureStamp(item.SigningKeyId, item.EventId, item.Id, item.EventType, clock.UtcNow.ToUnixTimeSeconds());
        var signature = WebhookSignatureHelper.ComputeSignature(secret, item.PayloadBody, stamp);
        using var client = httpClientFactory.CreateClient(WebhookHttpClientNames.Delivery);
        using var request = new HttpRequestMessage(HttpMethod.Post, item.TargetUrl)
        {
            Content = new StringContent(item.PayloadBody, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-FullNet-Signature", signature);
        request.Headers.Add("X-FullNet-Signature-Version", WebhookSignatureHelper.Version);
        request.Headers.Add("X-FullNet-Key-Id", stamp.KeyId.ToString("D"));
        request.Headers.Add("X-FullNet-Timestamp", stamp.Timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add("X-FullNet-Event-Id", item.EventId.ToString("D"));
        request.Headers.Add("X-FullNet-Delivery-Id", item.Id.ToString("D"));
        request.Headers.Add("X-FullNet-Event-Type", item.EventType);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? new(null, null, (int)response.StatusCode)
            : new($"HTTP {(int)response.StatusCode}", "webhooks.delivery.http_error", (int)response.StatusCode);
    }

    private sealed record AttemptOutcome(string? Error, string? ErrorCode, int? HttpStatus);

    private Task MarkDeliveredAsync(WebhookDeliveryWorkItem item, int httpStatus, CancellationToken token) =>
        RecordOutcomeAsync(item, WebhookDeliverySql.MarkDelivered, WebhookSqlParameters.Create(
            ("DeliveryId", item.Id), ("Status", WebhookDeliveryStatuses.Delivered), ("UpdatedAtUtc", clock.UtcNow), ("Version", item.Version)),
            "delivered", httpStatus, null, token);

    private async Task MarkFailureAsync(
        WebhookDeliveryWorkItem item,
        string error,
        string errorCode,
        int? httpStatus,
        CancellationToken cancellationToken)
    {
        // 退避从本次失败结束时计算，不能沿用外发前的时间令慢响应立即重试。
        var now = clock.UtcNow;
        var nextAttempt = item.AttemptCount + 1;
        var isTerminal = nextAttempt >= Math.Clamp(_options.MaxAttempts, 1, 20);
        var delaySeconds = Math.Min(3600, Math.Pow(2, Math.Min(nextAttempt, 10)));
        await RecordOutcomeAsync(item,
                WebhookDeliverySql.MarkFailed,
                WebhookSqlParameters.Create(
                    ("DeliveryId", item.Id),
                    ("Status", isTerminal ? WebhookDeliveryStatuses.Failed : WebhookDeliveryStatuses.Pending),
                    ("NextAttemptAtUtc", isTerminal ? null : now.AddSeconds(delaySeconds)),
                    ("LastError", Truncate(error, 512)),
                    ("UpdatedAtUtc", now),
                    ("Version", item.Version)),
                "failed", httpStatus, errorCode, cancellationToken)
            .ConfigureAwait(false);
    }

    // 先按原租约版本推进主记录，再首次完成本次尝试。旧租约仍可记录真实应答，但不能覆盖新投递状态。
    private Task<int> RecordOutcomeAsync(WebhookDeliveryWorkItem item, SqlStatement statement, IReadOnlyDictionary<string, object?> parameters,
        string outcome, int? httpStatus, string? errorCode, CancellationToken token) => transaction.ExecuteAsync(async cancellation =>
    {
        var applied = await commandExecutor.ExecuteAsync(statement, parameters, cancellation).ConfigureAwait(false);
        var recorded = await commandExecutor.ExecuteAsync(WebhookDeliveryAttemptSql.Complete, WebhookSqlParameters.Create(
            ("AttemptId", item.AttemptId), ("TenantId", item.TenantId), ("SubscriptionId", item.SubscriptionId), ("DeliveryId", item.Id),
            ("ClaimVersion", item.Version), ("CompletedAtUtc", clock.UtcNow), ("OutcomeKey", outcome), ("HttpStatus", httpStatus),
            ("ErrorCode", errorCode), ("AppliedToDelivery", applied == 1)), cancellation).ConfigureAwait(false);
        if (applied == 1 && recorded != 1) throw new InvalidOperationException("The delivery outcome has no matching open attempt.");
        return applied;
    }, token);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

internal static class WebhookHttpClientNames
{
    public const string Delivery = "FullNet.Webhooks.Delivery";
}
