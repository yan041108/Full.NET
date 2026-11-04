using Full.NET.Data.Abstractions;

/// <summary>Webhooks 模块显式 SQL 语句；参数化访问，作用域在声明处标注。</summary>
namespace Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions.Persistence;

internal static class WebhookSubscriptionSql
{
    /// <summary>明确定址到当前租户，不返回签名密钥。</summary>
    public static readonly SqlStatement FindById = new("webhooks.subscriptions.find_by_id", """
        SELECT Id,TenantId,EventType,TargetUrl,IsActive,Version,SigningKeyId FROM fn_webhooks_subscription
        WHERE TenantId=@TenantId AND Id=@Id
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

    /// <summary>原版本 CAS 只改变状态，不释放扇出名额或删除历史投递。</summary>
    public static readonly SqlStatement SetStatus = new("webhooks.subscriptions.set_status", """
        UPDATE fn_webhooks_subscription SET IsActive=@IsActive,UpdatedAtUtc=@NowUtc,Version=Version+1
        WHERE TenantId=@TenantId AND Id=@Id AND Version=@Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);

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
        SELECT Id, TenantId, EventType, TargetUrl, IsActive, Version, SigningKeyId
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
             CreatedAtUtc, UpdatedAtUtc, Version, SigningKeyId)
        VALUES
            (@Id, @TenantId, @EventType, @TargetUrl, @SigningSecretHash, 1,
             @CreatedAtUtc, @UpdatedAtUtc, 1, @SigningKeyId)
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
    int Version,
    Guid SigningKeyId);
