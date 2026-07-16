namespace Full.NET.Data.Abstractions;

public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxEnvelope>> AcquireAsync(
        int batchSize,
        TimeSpan lease,
        CancellationToken cancellationToken);

    Task MarkProcessedAsync(
        Guid id,
        Guid lockId,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        Guid id,
        Guid lockId,
        string error,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken);
}

public sealed class OutboxConcurrencyException(Guid id, Guid lockId)
    : InvalidOperationException(
        $"Outbox message '{id:D}' is no longer owned by lock '{lockId:D}'.");
