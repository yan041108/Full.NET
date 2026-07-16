using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.GetCurrentTenant;

internal sealed record GetCurrentTenantQuery : IQuery<TenantSummary>;
