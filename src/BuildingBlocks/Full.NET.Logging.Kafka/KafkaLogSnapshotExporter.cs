using Full.NET.Hosting.Observability;
using Microsoft.Extensions.Configuration;

namespace Full.NET.Logging.Kafka;

/// <summary>将宿主后台快照提交到日志专用的普通与优先 Kafka Producer。</summary>
public sealed class KafkaLogSnapshotExporter : IHostLogSnapshotExporter
{
    /// <summary>低基数发送诊断 Meter 名称。</summary>
    public const string MeterName = "Full.NET.Logging.Kafka";

    private readonly KafkaLogProducerPair _pair;

    private KafkaLogSnapshotExporter(KafkaLogProducerPair pair) => _pair = pair;

    /// <summary>仅在宿主明确选择 ApplicationKafka 时绑定配置和创建双 Producer。</summary>
    /// <param name="configuration">宿主配置；Kafka 节只在调用本方法时读取。</param>
    /// <returns>由宿主日志 Sink 持有并在排空后释放的出口。</returns>
    public static KafkaLogSnapshotExporter Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = configuration.GetSection(KafkaLogProducerOptions.SectionName)
            .Get<KafkaLogProducerOptions>() ?? new KafkaLogProducerOptions();
        return new KafkaLogSnapshotExporter(KafkaLogProducerPair.Create(options));
    }

    /// <inheritdoc />
    public void Emit(HostLogSnapshot snapshot)
    {
        var result = _pair.TryProduce(snapshot);
        if (result == KafkaLogProduceResult.Accepted)
        {
            return;
        }

        KafkaLogDeliveryTelemetry.Rejected.Add(1,
            new KeyValuePair<string, object?>("priority", snapshot.IsHighPriority ? "high" : "general"),
            new KeyValuePair<string, object?>("reason", result.ToString()));
    }

    /// <inheritdoc />
    public void Dispose() => _pair.Dispose();
}
