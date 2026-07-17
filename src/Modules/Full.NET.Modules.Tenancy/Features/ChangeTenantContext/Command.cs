using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Tenancy.Features.ChangeTenantContext;

internal sealed record Command(
    Guid? TenantId,
    ClaimsPrincipal Principal)
    : ITransactionalCommand<TenantContextTokenResponse>;
