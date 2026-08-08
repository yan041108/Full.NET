namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 不可通过 Broker 重试恢复的集成事件契约或安全失败。
/// </summary>
public sealed class IntegrationEventPermanentException : Exception
{
    public IntegrationEventPermanentException(IntegrationEventFailure failure)
        : base(failure.Summary)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
    }

    public IntegrationEventFailure Failure { get; }
}