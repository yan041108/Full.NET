namespace Full.NET.Abstractions.Messaging;

public interface IIntegrationEventHandler
{
    string EventType { get; }

    IReadOnlyList<string> LegacyEventTypes => [];

    int SchemaVersion { get; }

    Task HandleAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);
}
