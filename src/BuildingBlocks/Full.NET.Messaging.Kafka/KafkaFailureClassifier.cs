using Confluent.Kafka;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Messaging.Kafka;

/// <summary>
/// 将异常与稳定原因码映射到规格 §8 的失败分类。
/// </summary>
internal sealed class KafkaFailureClassifier
{
    public IntegrationEventFailure Classify(Exception exception, string? envelopeFailureCode = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is IntegrationEventPermanentException permanent)
        {
            return permanent.Failure;
        }

        if (!string.IsNullOrWhiteSpace(envelopeFailureCode))
        {
            return new IntegrationEventFailure(
                IntegrationEventFailure.ResolveKind(envelopeFailureCode),
                envelopeFailureCode,
                "Kafka envelope contract validation failed.");
        }

        if (exception is TimeoutException or IOException)
        {
            return CreateTransient("broker_or_io", "Transient broker or I/O failure.");
        }

        if (exception is KafkaException kafkaException
            && (kafkaException.Error.IsLocalError || kafkaException.Error.IsBrokerError))
        {
            return CreateTransient("broker_error", "Transient Kafka broker failure.");
        }

        return CreateTransient("consumer_dispatch", "Transient consumer dispatch failure.");
    }

    public bool ShouldRetry(IntegrationEventFailure failure) =>
        failure.Kind == IntegrationEventFailureKind.Transient;

    private static IntegrationEventFailure CreateTransient(string suffix, string summary) =>
        new(
            IntegrationEventFailureKind.Transient,
            IntegrationEventFailureCodes.TransientPrefix + suffix,
            summary);
}
