namespace Full.NET.Modules.Tenancy.Contracts;

public sealed record TenantSummary(
    Guid Id,
    string Identifier,
    string Name,
    string Domain,
    bool IsActive,
    int Version);
