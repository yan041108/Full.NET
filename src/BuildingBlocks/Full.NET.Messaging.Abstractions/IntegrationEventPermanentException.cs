namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// 涓嶅彲閫氳繃 Broker 閲嶈瘯鎭㈠鐨勯泦鎴愪簨浠跺绾︽垨瀹夊叏澶辫触銆?/// </summary>
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
