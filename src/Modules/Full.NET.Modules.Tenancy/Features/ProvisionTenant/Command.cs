using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.ProvisionTenant;

internal sealed record ProvisionTenantCommand(
    string Identifier,
    string Name,
    string Domain) : ITransactionalCommand<TenantSummary>;
