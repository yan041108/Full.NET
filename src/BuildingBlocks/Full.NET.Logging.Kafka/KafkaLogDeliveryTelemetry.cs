using System.Diagnostics.Metrics;

namespace Full.NET.Logging.Kafka;

/// <summary>日志 Kafka 的低基数提交与投递终态指标，不携带 Topic、事件 ID 或凭据。</summary>
internal static class KafkaLogDeliveryTelemetry
{
    internal static readonly Meter Meter = new(KafkaLogSnapshotExporter.MeterName);
    internal static readonly Counter<long> Rejected = Meter.CreateCounter<long>(
        "fullnet_log_kafka_rejected_total");
    private static readonly Counter<long> Delivery = Meter.CreateCounter<long>(
        "fullnet_log_kafka_delivery_total");
    private static readonly Counter<long> ShutdownFailure = Meter.CreateCounter<long>(
        "fullnet_log_kafka_shutdown_failure_total");

    internal static void RecordDelivery(bool highPriority, string outcome) =>
        Delivery.Add(1,
            new KeyValuePair<string, object?>("priority", highPriority ? "high" : "general"),
            new KeyValuePair<string, object?>("outcome", outcome));

    internal static void RecordShutdownFailure(bool highPriority) =>
        ShutdownFailure.Add(1,
            new KeyValuePair<string, object?>("priority", highPriority ? "high" : "general"));
}
