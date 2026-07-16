namespace Full.NET.Data.Abstractions;

public interface IIntegrationEventSerializer
{
    string ContentType { get; }

    byte[] Serialize<TEvent>(TEvent payload);

    TEvent Deserialize<TEvent>(ReadOnlyMemory<byte> payload);
}
