using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Webhooks.Features.ManageWebhookDeliveries.Persistence;

/// <summary>开始与领取在同一事务内提交，结果只允许首次写入，已完成尝试不再改变。</summary>
internal static class WebhookDeliveryAttemptSql
{
    private const string OwnedFrom = """
        FROM fn_webhooks_delivery_attempt attempt
        INNER JOIN fn_webhooks_delivery delivery ON delivery.Id=attempt.DeliveryId AND delivery.SubscriptionId=attempt.SubscriptionId
        INNER JOIN fn_webhooks_subscription subscription ON subscription.Id=attempt.SubscriptionId AND subscription.TenantId=attempt.TenantId
        WHERE attempt.TenantId=@TenantId AND attempt.SubscriptionId=@SubscriptionId AND attempt.DeliveryId=@DeliveryId
        """;
    private const string Columns = """
        attempt.Id,attempt.TenantId,attempt.SubscriptionId,attempt.DeliveryId,attempt.ClaimVersion,attempt.StartedAtUtc,
        attempt.CompletedAtUtc,attempt.OutcomeKey,attempt.HttpStatus,attempt.ErrorCode,attempt.AppliedToDelivery,
        delivery.Status AS CurrentStatus,delivery.Version AS CurrentVersion,delivery.UpdatedAtUtc AS CurrentUpdatedAtUtc
        """;
    public static readonly SqlStatement Count = new("webhooks.attempts.count", $"SELECT COUNT(*) {OwnedFrom}", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement PageSqlServer = new("webhooks.attempts.page.sql_server", $"""
        SELECT {Columns} {OwnedFrom} ORDER BY attempt.ClaimVersion DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement PageMySql = new("webhooks.attempts.page.mysql", $"""
        SELECT {Columns} {OwnedFrom} ORDER BY attempt.ClaimVersion DESC LIMIT @PageSize OFFSET @Offset
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    public static readonly SqlStatement Start = new("webhooks.attempts.start", """
        INSERT INTO fn_webhooks_delivery_attempt
            (Id,TenantId,SubscriptionId,DeliveryId,ClaimVersion,StartedAtUtc,OutcomeKey)
        VALUES (@AttemptId,@TenantId,@SubscriptionId,@DeliveryId,@ClaimVersion,@StartedAtUtc,'started')
        """, SqlDataScope.HostOnly);

    public static readonly SqlStatement Complete = new("webhooks.attempts.complete", """
        UPDATE fn_webhooks_delivery_attempt
        SET CompletedAtUtc=@CompletedAtUtc,OutcomeKey=@OutcomeKey,HttpStatus=@HttpStatus,ErrorCode=@ErrorCode,AppliedToDelivery=@AppliedToDelivery
        WHERE Id=@AttemptId AND TenantId=@TenantId AND SubscriptionId=@SubscriptionId AND DeliveryId=@DeliveryId
          AND ClaimVersion=@ClaimVersion AND OutcomeKey='started' AND CompletedAtUtc IS NULL
        """, SqlDataScope.HostOnly);
}

/// <summary>当前租约事实只用于识别未确认尝试，不改写已完成结果。</summary>
internal sealed record WebhookDeliveryAttemptRecord(Guid Id, Guid TenantId, Guid SubscriptionId, Guid DeliveryId, int ClaimVersion,
    DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, string OutcomeKey, int? HttpStatus, string? ErrorCode,
    bool? AppliedToDelivery, string CurrentStatus, int CurrentVersion, DateTimeOffset CurrentUpdatedAtUtc);
