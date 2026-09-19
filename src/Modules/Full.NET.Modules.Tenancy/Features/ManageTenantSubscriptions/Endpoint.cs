using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenancy/tenants/{tenantId:guid}/subscriptions")
            .WithTags("TenancyTenantSubscriptions");

        group.MapGet("/", async (
            Guid tenantId,
            TenantSubscriptionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyListTenantSubscriptions")
        .Produces<IReadOnlyList<TenantSubscriptionResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantSubscriptionPermissions.Read));

        group.MapPost("/", async (
            Guid tenantId,
            CreateTenantSubscriptionRequest request,
            TenantSubscriptionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(tenantId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyCreateTenantSubscription")
        .Produces<TenantSubscriptionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantSubscriptionPermissions.Manage));

        group.MapPost("/{subscriptionId:guid}/cancel", async (
            Guid tenantId,
            Guid subscriptionId,
            CancelTenantSubscriptionRequest request,
            TenantSubscriptionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CancelAsync(
                    tenantId,
                    subscriptionId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("tenancyCancelTenantSubscription")
        .Produces<TenantSubscriptionResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantSubscriptionPermissions.Manage));
    }
}
