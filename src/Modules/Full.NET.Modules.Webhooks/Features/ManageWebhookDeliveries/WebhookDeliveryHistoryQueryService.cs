using System.Globalization;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Delivery;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks;
using Full.NET.Modules.Webhooks.Features.ManageWebhookDeliveries.Persistence;
using Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;
using Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Webhooks.Features.ManageWebhookDeliveries;

/// <summary>投递历史在当前企业中明确定位，失败只输出安全分类，不暴露载荷或目标返回正文。</summary>
internal sealed class WebhookDeliveryHistoryQueryService(IQueryExecutor queries, ICurrentTenant tenant, IOptions<DatabaseOptions> database,
    IClock clock, IOptions<WebhookDeliveryWorkerOptions> workerOptions)
{
    internal async Task<Result<PagedResult<WebhookDeliveryAttemptResponse>>> ListAttemptsAsync(Guid subscriptionId, Guid deliveryId, int page, int pageSize, CancellationToken token)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return Result<PagedResult<WebhookDeliveryAttemptResponse>>.Failure(Invalid());
        var owned = await GetAsync(subscriptionId, deliveryId, token).ConfigureAwait(false);
        if (!owned.IsSuccess) return Result<PagedResult<WebhookDeliveryAttemptResponse>>.Failure(owned.Error!);
        var parameters = WebhookSqlParameters.Create(("SubscriptionId", subscriptionId), ("DeliveryId", deliveryId), ("PageSize", pageSize), ("Offset", (page - 1) * pageSize));
        var count = await queries.QuerySingleOrDefaultAsync<long>(WebhookDeliveryAttemptSql.Count, parameters, token).ConfigureAwait(false);
        var sql = database.Value.Provider switch { DatabaseProvider.SqlServer => WebhookDeliveryAttemptSql.PageSqlServer, DatabaseProvider.MySql => WebhookDeliveryAttemptSql.PageMySql, _ => throw new InvalidOperationException("Unsupported database provider.") };
        var rows = await queries.QueryAsync<WebhookDeliveryAttemptRecord>(sql, parameters, token).ConfigureAwait(false);
        var deadline = clock.UtcNow.AddSeconds(-Math.Clamp(workerOptions.Value.LeaseSeconds, 60, 3600));
        var items = rows.Select(row => {
            var outcome = row.OutcomeKey == "started" ? row.CurrentStatus == "processing" && row.ClaimVersion == row.CurrentVersion && row.CurrentUpdatedAtUtc > deadline ? "processing" : "unconfirmed" : row.OutcomeKey;
            var error = WebhookDeliveryBatchProcessor.IsAdmissionCode(row.ErrorCode)
                || row.ErrorCode is null or "webhooks.delivery.http_error" or "webhooks.delivery.timeout" or "webhooks.delivery.target_invalid" or "webhooks.delivery.target_blocked" or "webhooks.delivery.transport_error"
                ? row.ErrorCode : "webhooks.delivery.failed";
            return new WebhookDeliveryAttemptResponse(row.Id, row.TenantId, row.SubscriptionId, row.DeliveryId, outcome, row.StartedAtUtc, row.CompletedAtUtc, row.HttpStatus, error, row.AppliedToDelivery);
        }).ToArray();
        return Result<PagedResult<WebhookDeliveryAttemptResponse>>.Success(new(items, page, pageSize, count));
    }

    internal async Task<Result<PagedResult<WebhookDeliveryHistoryResponse>>> ListAsync(Guid subscriptionId, int page, int pageSize, string? statusKey, CancellationToken token)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return Result<PagedResult<WebhookDeliveryHistoryResponse>>.Failure(Invalid());
        var status = statusKey?.Trim().ToLowerInvariant();
        var storedStatus = status switch { null or "" => null, "pending" => WebhookDeliveryStatuses.Pending, "processing" => "processing", "delivered" => WebhookDeliveryStatuses.Delivered, "failed" => WebhookDeliveryStatuses.Failed, _ => "invalid" };
        if (storedStatus == "invalid") return Result<PagedResult<WebhookDeliveryHistoryResponse>>.Failure(Invalid());
        var owned = await OwnedAsync(subscriptionId, token).ConfigureAwait(false);
        if (owned is not null) return Result<PagedResult<WebhookDeliveryHistoryResponse>>.Failure(owned);
        var parameters = WebhookSqlParameters.Create(("SubscriptionId", subscriptionId), ("Status", storedStatus), ("PageSize", pageSize), ("Offset", (page - 1) * pageSize));
        var count = await queries.QuerySingleOrDefaultAsync<long>(WebhookDeliveryHistorySql.Count, parameters, token).ConfigureAwait(false);
        var sql = database.Value.Provider switch { DatabaseProvider.SqlServer => WebhookDeliveryHistorySql.PageSqlServer, DatabaseProvider.MySql => WebhookDeliveryHistorySql.PageMySql, _ => throw new InvalidOperationException("Unsupported database provider.") };
        var rows = await queries.QueryAsync<WebhookDeliveryHistoryRecord>(sql, parameters, token).ConfigureAwait(false);
        return Result<PagedResult<WebhookDeliveryHistoryResponse>>.Success(new(rows.Select(Map).ToArray(), page, pageSize, count));
    }

    internal async Task<Result<WebhookDeliveryHistoryResponse>> GetAsync(Guid subscriptionId, Guid deliveryId, CancellationToken token)
    {
        var owned = await OwnedAsync(subscriptionId, token).ConfigureAwait(false);
        if (owned is not null) return Result<WebhookDeliveryHistoryResponse>.Failure(owned);
        var row = await queries.QuerySingleOrDefaultAsync<WebhookDeliveryHistoryRecord>(WebhookDeliveryHistorySql.Find,
            WebhookSqlParameters.Create(("SubscriptionId", subscriptionId), ("DeliveryId", deliveryId)), token).ConfigureAwait(false);
        return row is null ? Result<WebhookDeliveryHistoryResponse>.Failure(new Error(WebhookErrorCodes.DeliveryNotFound, "The delivery was not found.", ErrorType.NotFound))
            : Result<WebhookDeliveryHistoryResponse>.Success(Map(row));
    }

    // 存在性核对和最终 SQL 均保留企业谓词；不能用第一步的查询结果替代后续隔离。
    private async Task<Error?> OwnedAsync(Guid id, CancellationToken token)
    {
        if (!tenant.IsAvailable || tenant.IsHost || tenant.Id is null)
            return new("tenancy.context_not_found", "Tenant context was not found.", ErrorType.Validation);
        return await queries.QuerySingleOrDefaultAsync<WebhookSubscriptionRecord>(WebhookSubscriptionSql.FindById, WebhookSqlParameters.Create(("Id", id)), token).ConfigureAwait(false) is null
            ? new Error(WebhookErrorCodes.SubscriptionNotFound, "The subscription was not found.", ErrorType.NotFound) : null;
    }

    // 旧 LastError 可能包含内部地址或异常文本，只识别严格的已知形式，其余统一分类而不转发原文。
    internal static WebhookDeliveryHistoryResponse Map(WebhookDeliveryHistoryRecord row)
    {
        string? error = null; int? httpStatus = null;
        if (!string.IsNullOrEmpty(row.LastError))
        {
            if (row.LastError.Length == 8 && row.LastError.StartsWith("HTTP ", StringComparison.Ordinal)
                && int.TryParse(row.LastError.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 100 and <= 599)
            { error = "webhooks.delivery.http_error"; httpStatus = parsed; }
            else if (WebhookDeliveryBatchProcessor.IsAdmissionCode(row.LastError))
                error = row.LastError;
            else error = row.LastError == "Webhook delivery timed out." ? "webhooks.delivery.timeout" : "webhooks.delivery.failed";
        }
        return new(row.Id, row.TenantId, row.SubscriptionId, row.EventId, row.Status.ToLowerInvariant(), row.AttemptCount,
            row.NextAttemptAtUtc, error, httpStatus, row.CreatedAtUtc, row.UpdatedAtUtc, row.Version);
    }
    private static Error Invalid() => new(ValidationErrorCodes.Failed, "The page or status filter is invalid.", ErrorType.Validation);
}
