namespace Full.NET.Abstractions.Tenancy;

public sealed record TenantContext(Guid Id, string Identifier, string Name);
