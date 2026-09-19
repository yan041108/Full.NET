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
        var items = await ClaimAsync(cancellationToken).ConfigureAwait(false);
        if (items.Count == 0)
        {
            return 0;
        }

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeliverOneAsync(item, cancellationToken).ConfigureAwait(false);
        }

        return items.Count;
    }

    private async Task<IReadOnlyList<WebhookDeliveryWorkItem>> ClaimAsync(
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(_options.BatchSize, 1, 50);
        var now = clock.UtcNow;
        var parameters = WebhookSqlParameters.Create(
            ("BatchSize", batchSize),
            ("Now", now),
            ("PendingStatus", WebhookDeliveryStatuses.Pending));
        if (databaseOptions.Value.Provider == DatabaseProvider.SqlServer)
        {
            var rows = await queryExecutor.QueryAsync<WebhookDeliveryWorkItem>(
                    WebhookDeliverySql.ClaimPendingSqlServer,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
            return rows.ToArray();
        }

        return await transaction.ExecuteAsync(
                async token =>
                {
                    var rows = await queryExecutor.QueryAsync<WebhookDeliveryWorkItem>(
                            WebhookDeliverySql.ClaimPendingMySql,
                            parameters,
                            token)
                        .ConfigureAwait(false);
                    return (IReadOnlyList<WebhookDeliveryWorkItem>)rows.ToArray();
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DeliverOneAsync(
        WebhookDeliveryWorkItem item,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
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
                    cancellationToken)
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
                    cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                await MarkDeliveredAsync(item, now, cancellationToken).ConfigureAwait(false);
                return;
            }

            await MarkFailureAsync(
                    item,
                    now,
                    $"HTTP {(int)response.StatusCode}",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Webhook delivery {DeliveryId} to {TargetUrl} failed.",
                item.Id,
                item.TargetUrl);
            await MarkFailureAsync(item, now, exception.Message, cancellationToken).ConfigureAwait(false);
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