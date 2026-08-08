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

        switch (options.Mode)
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

            case MessagingWorkerMode.CdcKafka:
                if (!kafkaEnabled)
                {
                    failures.Add(
                        $"{MessagingWorkerOptions.SectionName}:Mode CdcKafka "
                        + "requires Messaging:Kafka:Enabled=true.");
                }

                if (shadowEnabled)
                {
                    failures.Add(
                        $"{MessagingWorkerOptions.SectionName}:Mode CdcKafka "
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
