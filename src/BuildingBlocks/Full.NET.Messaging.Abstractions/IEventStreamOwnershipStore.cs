namespace Full.NET.Messaging.Abstractions;

/// <summary>读取与写入事件流交付所有权持久化记录。</summary>
public interface IEventStreamOwnershipStore
{
    Task<EventStreamOwnershipRecord?> FindAsync(
        string messageType,
        int schemaVersion,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventStreamOwnershipRecord>> ListAsync(
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        EventStreamOwnershipRecord record,
        CancellationToken cancellationToken = default);
}
