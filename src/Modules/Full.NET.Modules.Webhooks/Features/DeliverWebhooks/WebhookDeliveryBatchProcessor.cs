// Webhook 投递批处理器；领取出站记录、重试与毒消息隔离。
using System.Net;
using System.Net.Http.Headers;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Delivery;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks.Persistence;
using Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;
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
    ILogger<WebhookDeliveryBatchProcessor> logger)
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
                if (affected == 1) claimed.Add(row with { Version = row.Version + 1 });
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
        try
        {
            if (!Uri.TryCreate(item.TargetUrl, UriKind.Absolute, out var targetUri))
            {
                await MarkFailureAsync(item, now, "Target URL is invalid.", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var (allowed, reason) = await WebhookDeliverySsrfGuard.ValidateAsync(
                    targetUri,
                    attemptCancellation.Token)
                .ConfigureAwait(false);
            if (!allowed)
            {
                await MarkFailureAsync(
                        item,
                        now,
                        reason ?? "Target URL is blocked.",
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var secret = signingSecretProtector.Unprotect(item.SigningSecretHash);
            var signature = WebhookSignatureHelper.ComputeSignature(secret, item.PayloadBody);
            using var client = httpClientFactory.CreateClient(WebhookHttpClientNames.Delivery);
            using var request = new HttpRequestMessage(HttpMethod.Post, item.TargetUrl)
            {
                Content = new StringContent(item.PayloadBody, System.Text.Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-FullNet-Signature", signature);
            request.Headers.Add("X-FullNet-Event-Id", item.EventId.ToString("D"));
            request.Headers.Add("X-FullNet-Delivery-Id", item.Id.ToString("D"));
            request.Headers.Add("X-FullNet-Event-Type", item.EventType);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    attemptCancellation.Token)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                await MarkDeliveredAsync(item, clock.UtcNow, cancellationToken).ConfigureAwait(false);
                return;
            }

            await MarkFailureAsync(
                    item,
                    now,
                    $"HTTP {(int)response.StatusCode}",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            await MarkFailureAsync(item, clock.UtcNow, "Webhook delivery timed out.", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Webhook delivery {DeliveryId} to {TargetUrl} failed.",
                item.Id,
                item.TargetUrl);
            await MarkFailureAsync(item, clock.UtcNow, exception.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task MarkDeliveredAsync(
        WebhookDeliveryWorkItem item,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await commandExecutor.ExecuteAsync(
                WebhookDeliverySql.MarkDelivered,
                WebhookSqlParameters.Create(
                    ("DeliveryId", item.Id),
                    ("Status", WebhookDeliveryStatuses.Delivered),
                    ("UpdatedAtUtc", now),
                    ("Version", item.Version)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task MarkFailureAsync(
        WebhookDeliveryWorkItem item,
        DateTimeOffset now,
        string error,
        CancellationToken cancellationToken)
    {
        var nextAttempt = item.AttemptCount + 1;
        var isTerminal = nextAttempt >= Math.Clamp(_options.MaxAttempts, 1, 20);
        var delaySeconds = Math.Min(3600, Math.Pow(2, Math.Min(nextAttempt, 10)));
        await commandExecutor.ExecuteAsync(
                WebhookDeliverySql.MarkFailed,
                WebhookSqlParameters.Create(
                    ("DeliveryId", item.Id),
                    ("Status", isTerminal ? WebhookDeliveryStatuses.Failed : WebhookDeliveryStatuses.Pending),
                    ("NextAttemptAtUtc", isTerminal ? null : now.AddSeconds(delaySeconds)),
                    ("LastError", Truncate(error, 512)),
                    ("UpdatedAtUtc", now),
                    ("Version", item.Version)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

internal static class WebhookHttpClientNames
{
    public const string Delivery = "FullNet.Webhooks.Delivery";
}
