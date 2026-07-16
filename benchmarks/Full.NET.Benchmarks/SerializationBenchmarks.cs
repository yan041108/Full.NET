using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Serialization;
using Full.NET.Serialization.MessagePack;

namespace Full.NET.Benchmarks;

[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private readonly TenantSummary _summary = new(
        Guid.Parse("018f3f78-4d7a-7c16-9f0f-8a7ce6d5a001"),
        "acme",
        "Acme Corporation",
        "acme.localhost",
        true,
        1);

    private readonly TenantProvisionedIntegrationEvent _event = new(
        Guid.Parse("018f3f78-4d7a-7c16-9f0f-8a7ce6d5a001"),
        "acme",
        "acme.localhost");

    private readonly IIntegrationEventSerializer _messagePack =
        new MessagePackIntegrationEventSerializer();

    private byte[] _messagePackPayload = [];

    [GlobalSetup]
    public void Setup() =>
        _messagePackPayload = _messagePack.Serialize(_event);

    [Benchmark(Baseline = true)]
    public byte[] SystemTextJsonSourceGenerated() =>
        JsonSerializer.SerializeToUtf8Bytes(
            _summary,
            TenancyJsonSerializerContext.Default.TenantSummary);

    [Benchmark]
    public byte[] MessagePackSerialize() =>
        _messagePack.Serialize(_event);

    [Benchmark]
    public TenantProvisionedIntegrationEvent MessagePackDeserialize() =>
        _messagePack.Deserialize<TenantProvisionedIntegrationEvent>(
            _messagePackPayload);
}
