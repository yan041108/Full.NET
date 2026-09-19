namespace Full.NET.Modules.Webhooks.Contracts;

public static class WebhookPermissions
{
    public const string SubscriptionsRead = "webhooks.subscriptions.read";
    public const string SubscriptionsManage = "webhooks.subscriptions.manage";
}

public static class WebhookDeliveryStatuses
{
    public const string Pending = "Pending";
    public const string Delivered = "Delivered";
    public const string Failed = "Failed";
}

public static class WebhookErrorCodes
{
    public const string Prefix = "webhooks.";
    public const string SubscriptionNotFound = "webhooks.subscription.not_found";
    public const string TargetUrlInvalid = "webhooks.subscription.target_url_invalid";
}

public sealed record WebhookSubscriptionResponse(
    Guid Id,
    Guid TenantId,
    string EventType,
    string TargetUrl,
    bool IsActive,
    int Version);

public sealed record CreateWebhookSubscriptionRequest(
    string EventType,
    string TargetUrl,
    string SigningSecret);

public sealed record WebhookDeliveryResponse(
    Guid Id,
    Guid SubscriptionId,
    Guid EventId,
    string Status,
    int AttemptCount,
    int Version);

public sealed record WebhookEventEnvelope(
    string EventType,
    Guid EventId,
    Guid TenantId,
    DateTimeOffset OccurredAtUtc,
    WebhookWorkflowInstanceCompletedData Data);

public sealed record WebhookWorkflowInstanceCompletedData(
    Guid InstanceId,
    Guid RecipientUserId,
    string BusinessType,
    string BusinessId);
