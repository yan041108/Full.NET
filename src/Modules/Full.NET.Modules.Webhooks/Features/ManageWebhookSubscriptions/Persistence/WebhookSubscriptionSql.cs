using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions.Persistence;

internal static class WebhookSubscriptionSql
{
    public static readonly SqlStatement ListByTenant = new(
        "webhooks.subscriptions.list_by_tenant",
        @"
        SELECT Id, TenantId, EventType, TargetUrl, IsActive, Version
        FROM fn_webhooks_subscription
        WHERE TenantId = @TenantId
        ORDER BY EventType, Id
        ",
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement Insert = new(
        "webhooks.subscriptions.insert",
        @"
        INSERT INTO fn_webhooks_subscription
            (Id, TenantId, EventType, TargetUrl, SigningSecretHash, IsActive,
             CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @EventType, @TargetUrl, @SigningSecretHash, 1,
             @CreatedAtUtc, @UpdatedAtUtc, 1)
        ",
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}

internal sealed record WebhookSubscriptionRecord(
    Guid Id,
    Guid TenantId,
    string EventType,
    string TargetUrl,
    bool IsActive,
    int Version);
