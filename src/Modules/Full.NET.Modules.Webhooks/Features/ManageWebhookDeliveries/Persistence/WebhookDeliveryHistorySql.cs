using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Webhooks.Features.ManageWebhookDeliveries.Persistence;

/// <summary>同模块订阅与投递只读关联；每条查询始终包含可信企业及明确订阅。</summary>
internal static class WebhookDeliveryHistorySql
{
    private const string From = """
        FROM fn_webhooks_delivery delivery
        INNER JOIN fn_webhooks_subscription subscription ON subscription.Id=delivery.SubscriptionId
        WHERE subscription.TenantId=@TenantId AND subscription.Id=@SubscriptionId
        """;
    private const string Columns = """
        delivery.Id,subscription.TenantId,delivery.SubscriptionId,delivery.EventId,delivery.Status,
        delivery.AttemptCount,delivery.NextAttemptAtUtc,delivery.LastError,delivery.CreatedAtUtc,delivery.UpdatedAtUtc,delivery.Version
        """;
    public static readonly SqlStatement Count = new("webhooks.deliveries.history_count",
        $"SELECT COUNT(*) {From} AND (@Status IS NULL OR delivery.Status=@Status)", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement PageSqlServer = new("webhooks.deliveries.history_page.sql_server", $"""
        SELECT {Columns} {From} AND (@Status IS NULL OR delivery.Status=@Status)
        ORDER BY delivery.CreatedAtUtc DESC,delivery.Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement PageMySql = new("webhooks.deliveries.history_page.mysql", $"""
        SELECT {Columns} {From} AND (@Status IS NULL OR delivery.Status=@Status)
        ORDER BY delivery.CreatedAtUtc DESC,delivery.Id DESC LIMIT @PageSize OFFSET @Offset
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement Find = new("webhooks.deliveries.history_find",
        $"SELECT {Columns} {From} AND delivery.Id=@DeliveryId", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}

/// <summary>原始错误仅用于本模块分类，不登记到 HTTP JSON 或直接返回给客户端。</summary>
internal sealed record WebhookDeliveryHistoryRecord(Guid Id, Guid TenantId, Guid SubscriptionId, Guid EventId,
    string Status, int AttemptCount, DateTimeOffset? NextAttemptAtUtc, string? LastError, DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc, int Version);
