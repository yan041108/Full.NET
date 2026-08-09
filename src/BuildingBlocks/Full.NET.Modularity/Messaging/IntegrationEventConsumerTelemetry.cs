using System.Diagnostics;

namespace Full.NET.Modularity.Messaging;

/// <summary>
/// Inbox 本地事务 Activity；只记录稳定契约码，不记录 MessageId、TenantId 或 Payload。
/// </summary>
public static class IntegrationEventConsumerTelemetry
{
    public const string ActivitySourceName = "Full.NET.Messaging.Inbox";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static Activity? StartTransaction(
        string consumerName,
        string messageType,
        int schemaVersion)
    {
        var activity = ActivitySource.StartActivity(
            "fullnet.messaging.inbox.transaction",
            ActivityKind.Internal);
        activity?.SetTag("messaging.consumer.group.name", consumerName);
        activity?.SetTag("messaging.message.type", messageType);
        activity?.SetTag("messaging.message.schema_version", schemaVersion);
        return activity;
    }
}
