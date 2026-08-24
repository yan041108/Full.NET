using global::MemoryPack;

namespace Full.NET.Modules.Tenancy.Contracts;

[MemoryPackable]
public partial record TenantProvisionedIntegrationEvent(
    Guid TenantId,
    string Identifier,
    string Domain);
