using Full.NET.Abstractions.Messaging;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Tenancy.Features.GetAvailableTenants;

internal static class Endpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/available", async (
                IQueryDispatcher dispatcher,
                IApiResultMapper mapper,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.SendAsync<Query, TenantContextSummary[]>(
                        new Query(),
                        cancellationToken)
                    .ConfigureAwait(false);
                return mapper.Map(result, httpContext);
            })
            .RequireAuthorization(FullNetPermissionPolicies.For(
                TenancyAuthorizationContributor.TenantsRead));
    }
}
