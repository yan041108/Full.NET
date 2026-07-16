using Full.NET.Data.Abstractions;
using global::MessagePack;

namespace Full.NET.Serialization.MessagePack;

public sealed class MessagePackIntegrationEventSerializer : IIntegrationEventSerializer
{
    private static readonly MessagePackSerializerOptions SerializerOptions =
        MessagePackSerializerOptions.Standard
            .WithSecurity(MessagePackSecurity.UntrustedData);

    public string ContentType => "application/x-msgpack";

    public byte[] Serialize<TEvent>(TEvent payload) =>
        MessagePackSerializer.Serialize(payload, SerializerOptions);

    public TEvent Deserialize<TEvent>(ReadOnlyMemory<byte> payload) =>
        MessagePackSerializer.Deserialize<TEvent>(payload, SerializerOptions);
}
