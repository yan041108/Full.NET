using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Full.NET.Host.Worker;

/// <summary>
/// Worker 消息交付模式与关联开关的单一配置入口。
/// </summary>
public sealed class MessagingWorkerOptions
{
    public const string SectionName = "Messaging:Worker";

    public MessagingWorkerMode Mode { get; set; } = MessagingWorkerMode.LegacyPolling;
}

internal sealed class MessagingWorkerOptionsValidator(IConfiguration configuration)
    : IValidateOptions<MessagingWorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, MessagingWorkerOptions options)
    {
        var failures = new List<string>();
        var kafkaEnabled = configuration.GetValue<bool>(KafkaMessagingSection.EnabledPath);
        var shadowEnabled = configuration.GetValue<bool>(ShadowComparisonSection.EnabledPath);

        // 将过时的 CdcKafka 别名规范化为 HybridKafka，后续 switch 按 HybridKafka 语义处理。
#pragma warning disable CS0618 // CdcKafka 作为过时别名保留一版，此处显式比较以支持旧配置。
        var effectiveMode = options.Mode == MessagingWorkerMode.CdcKafka
            ? MessagingWorkerMode.HybridKafka
            : options.Mode;
#pragma warning restore CS0618

        switch (effectiveMode)
        {
            case MessagingWorkerMode.LegacyPolling:
                if (kafkaEnabled)
                {
                    failures.Add(
                        $"{MessagingWorkerOptions.SectionName}:Mode LegacyPolling "
                        + "cannot be combined with Messaging:Kafka:Enabled=true.");
                }

                if (shadowEnabled)
                {
                    failures.Add(
                        $"{MessagingWorkerOptions.SectionName}:Mode LegacyPolling "
                        + "cannot be combined with Messaging:ShadowComparison:Enabled=true.");
                }

                break;

            case MessagingWorkerMode.ShadowCdc:
                if (!shadowEnabled)
                {
                    failures.Add(
                        $"{MessagingWorkerOptions.SectionName}:Mode ShadowCdc "
                        + "requires Messaging:ShadowComparison:Enabled=true.");
                }

                if (kafkaEnabled)
                {
                    failures.Add(
                        $"{MessagingWorkerOptions.SectionName}:Mode ShadowCdc "
                        + "cannot be combined with Messaging:Kafka:Enabled=true.");
                }

                break;

            case MessagingWorkerMode.HybridKafka:
                // HybridKafka 允许 Legacy Poller 与 Kafka Consumer 并存。
                // 要求 Kafka:Enabled=true；Shadow 模式与 Hybrid 互斥。
                if (!kafkaEnabled)
                {
                    failures.Add(
                        $"{MessagingWorkerOptions.SectionName}:Mode HybridKafka "
                        + "requires Messaging:Kafka:Enabled=true.");
                }

                if (shadowEnabled)
                {
                    failures.Add(
                        $"{MessagingWorkerOptions.SectionName}:Mode HybridKafka "
                        + "cannot be combined with Messaging:ShadowComparison:Enabled=true.");
                }

                break;

            default:
                failures.Add(
                    $"{MessagingWorkerOptions.SectionName}:Mode '{options.Mode}' is not supported.");
                break;
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static class KafkaMessagingSection
    {
        public const string EnabledPath = "Messaging:Kafka:Enabled";
    }

    private static class ShadowComparisonSection
    {
        public const string EnabledPath = "Messaging:ShadowComparison:Enabled";
    }
}
