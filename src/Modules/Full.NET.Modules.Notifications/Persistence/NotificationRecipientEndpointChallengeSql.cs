using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>收件端点验证码挑战 SQL；挑战行只保存哈希，不保存验证码原文。</summary>
internal static class NotificationRecipientEndpointChallengeSql
{
    /// <summary>使同一端点下未消费的旧挑战失效，避免并行验证码并存。</summary>
    public static readonly SqlStatement InvalidateActiveByEndpoint = new(
        "notifications.recipient_endpoint_challenge.invalidate_active",
        """
        UPDATE fn_notifications_recipient_endpoint_challenge
        SET ConsumedAtUtc = @ConsumedAtUtc
        WHERE RecipientEndpointId = @RecipientEndpointId
          AND ConsumedAtUtc IS NULL
          AND ExpiresAtUtc > @ConsumedAtUtc
        """,
        SqlDataScope.Global);

    /// <summary>插入新的验证码挑战。</summary>
    public static readonly SqlStatement Insert = new(
        "notifications.recipient_endpoint_challenge.insert",
        """
        INSERT INTO fn_notifications_recipient_endpoint_challenge
            (Id, RecipientEndpointId, TenantScopeKey, UserId, CodeHash,
             AttemptCount, MaxAttempts, ExpiresAtUtc, ConsumedAtUtc, CreatedAtUtc)
        VALUES
            (@Id, @RecipientEndpointId, @TenantScopeKey, @UserId, @CodeHash,
             0, @MaxAttempts, @ExpiresAtUtc, NULL, @CreatedAtUtc)
        """,
        SqlDataScope.Global);

    /// <summary>读取端点当前有效且未消费的挑战。</summary>
    public static readonly SqlStatement FindActiveByEndpoint = new(
        "notifications.recipient_endpoint_challenge.find_active",
        """
        SELECT TOP (1)
               Id, RecipientEndpointId, TenantScopeKey, UserId, CodeHash,
               AttemptCount, MaxAttempts, ExpiresAtUtc, ConsumedAtUtc, CreatedAtUtc
        FROM fn_notifications_recipient_endpoint_challenge
        WHERE RecipientEndpointId = @RecipientEndpointId
          AND TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
          AND ConsumedAtUtc IS NULL
          AND ExpiresAtUtc > @NowUtc
        ORDER BY CreatedAtUtc DESC
        """,
        SqlDataScope.Global);

    /// <summary>MySQL 读取端点当前有效且未消费的挑战。</summary>
    public static readonly SqlStatement FindActiveByEndpointMySql = new(
        "notifications.recipient_endpoint_challenge.find_active.mysql",
        """
        SELECT Id, RecipientEndpointId, TenantScopeKey, UserId, CodeHash,
               AttemptCount, MaxAttempts, ExpiresAtUtc, ConsumedAtUtc, CreatedAtUtc
        FROM fn_notifications_recipient_endpoint_challenge
        WHERE RecipientEndpointId = @RecipientEndpointId
          AND TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
          AND ConsumedAtUtc IS NULL
          AND ExpiresAtUtc > @NowUtc
        ORDER BY CreatedAtUtc DESC
        LIMIT 1
        """,
        SqlDataScope.Global);

    /// <summary>递增挑战尝试次数。</summary>
    public static readonly SqlStatement IncrementAttempt = new(
        "notifications.recipient_endpoint_challenge.increment_attempt",
        """
        UPDATE fn_notifications_recipient_endpoint_challenge
        SET AttemptCount = AttemptCount + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
          AND ConsumedAtUtc IS NULL
        """,
        SqlDataScope.Global);

    /// <summary>标记挑战已消费。</summary>
    public static readonly SqlStatement MarkConsumed = new(
        "notifications.recipient_endpoint_challenge.mark_consumed",
        """
        UPDATE fn_notifications_recipient_endpoint_challenge
        SET ConsumedAtUtc = @ConsumedAtUtc
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
          AND ConsumedAtUtc IS NULL
        """,
        SqlDataScope.Global);

    /// <summary>查询最近一次发送时间，用于应用层冷却判断。</summary>
    public static readonly SqlStatement FindLatestCreatedAtByEndpoint = new(
        "notifications.recipient_endpoint_challenge.find_latest_created",
        """
        SELECT TOP (1) CreatedAtUtc
        FROM fn_notifications_recipient_endpoint_challenge
        WHERE RecipientEndpointId = @RecipientEndpointId
          AND TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
        ORDER BY CreatedAtUtc DESC
        """,
        SqlDataScope.Global);

    /// <summary>MySQL 查询最近一次发送时间。</summary>
    public static readonly SqlStatement FindLatestCreatedAtByEndpointMySql = new(
        "notifications.recipient_endpoint_challenge.find_latest_created.mysql",
        """
        SELECT CreatedAtUtc
        FROM fn_notifications_recipient_endpoint_challenge
        WHERE RecipientEndpointId = @RecipientEndpointId
          AND TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
        ORDER BY CreatedAtUtc DESC
        LIMIT 1
        """,
        SqlDataScope.Global);
}
