using global::MessagePack;

namespace Full.NET.Modules.Tenancy.Contracts;

[MessagePackObject]
public sealed record TenantProvisionedIntegrationEvent(
    [property: Key(0)] Guid TenantId,
    [property: Key(1)] string Identifier,
    [property: Key(2)] string Domain);
