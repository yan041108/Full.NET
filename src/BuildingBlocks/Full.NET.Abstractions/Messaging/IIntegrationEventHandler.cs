namespace Full.NET.Abstractions.Messaging;

public interface IIntegrationEventHandler
{
    string EventType { get; }

    int SchemaVersion { get; }

    Task HandleAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);
}
