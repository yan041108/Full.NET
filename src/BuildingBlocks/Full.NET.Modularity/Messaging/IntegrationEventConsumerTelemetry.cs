using System.Diagnostics;

namespace Full.NET.Modularity.Messaging;

/// <summary>
/// Inbox 本地事务 Activity；只记录稳定契约码，不记录 MessageId、TenantId 或 Payload。
/// </summary>
public static class IntegrationEventConsumerTelemetry
{
    /// <summary>ActivitySource 稳定名称；供 OpenTelemetry 订阅追踪埋点。</summary>
    public const string ActivitySourceName = "Full.NET.Messaging.Inbox";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>
    /// 创建消费者事务级 Activity；记录 Consumer、EventType、SchemaVersion 等稳定维度标签，
    /// 用于统计单条消费耗时、成功率与重试率。
    /// </summary>
    /// <remarks>
    /// 安全边界：Activity 标签禁止携带 MessageId、TenantId、Payload 等敏感或高基数字段，
    /// 避免追踪系统爆炸或泄露业务数据。
    /// </remarks>
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
