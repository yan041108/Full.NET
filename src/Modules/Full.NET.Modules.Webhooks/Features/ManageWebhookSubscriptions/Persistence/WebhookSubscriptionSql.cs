using Full.NET.Data.Abstractions;

/// <summary>Webhooks 模块显式 SQL 语句；参数化访问，作用域在声明处标注。</summary>
namespace Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions.Persistence;

internal static class WebhookSubscriptionSql
{
    public static readonly SqlStatement EnsureFanoutBudgetSqlServer = new(
        "webhooks.subscriptions.ensure_budget.sql_server",
        """
        INSERT INTO fn_webhooks_fanout_budget (TenantId, EventType, UsedCount)
        SELECT @TenantId, @EventType, 0
        WHERE NOT EXISTS (SELECT 1 FROM fn_webhooks_fanout_budget WITH (UPDLOCK, HOLDLOCK)
            WHERE TenantId = @TenantId AND EventType = @EventType)
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement EnsureFanoutBudgetMySql = new(
        "webhooks.subscriptions.ensure_budget.mysql",
        """
        INSERT INTO fn_webhooks_fanout_budget (TenantId, EventType, UsedCount)
        VALUES (@TenantId, @EventType, 0) ON DUPLICATE KEY UPDATE UsedCount = UsedCount
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement ReserveFanoutSlot = new(
        "webhooks.subscriptions.reserve_fanout_slot",
        """
        UPDATE fn_webhooks_fanout_budget SET UsedCount = UsedCount + 1
        WHERE TenantId = @TenantId AND EventType = @EventType AND UsedCount < @MaximumTargets
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

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
