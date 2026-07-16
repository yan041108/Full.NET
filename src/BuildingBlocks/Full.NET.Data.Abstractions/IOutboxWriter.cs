namespace Full.NET.Data.Abstractions;

public interface IOutboxWriter
{
    Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        CancellationToken cancellationToken = default);
}
