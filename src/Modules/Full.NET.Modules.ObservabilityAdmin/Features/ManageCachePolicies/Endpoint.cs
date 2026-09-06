using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ObservabilityAdmin.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.ObservabilityAdmin.Features.ManageCachePolicies;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/observability/cache-policies")
            .WithTags("ObservabilityCachePolicies");

        group.MapGet("/", (CachePolicyControlPlane controlPlane) =>
                Results.Ok(controlPlane.List()))
            .WithName("observabilityListCachePolicies")
            .Produces<IReadOnlyList<CachePolicySummary>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization(FullNetPermissionPolicies.For(
                ObservabilityCachePolicyPermissions.Read));

        group.MapGet("/{entryName}", (
            string entryName,
            CachePolicyControlPlane controlPlane,
            IApiResultMapper mapper,
            HttpContext httpContext) =>
        {
            var policy = controlPlane.Get(entryName);
            return policy is null
                ? NotFound(mapper, httpContext, ObservabilityAdminErrorCodes.CachePolicyNotFound)
                : Results.Ok(policy);
        })
        .WithName("observabilityGetCachePolicy")
        .Produces<CachePolicySummary>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ObservabilityCachePolicyPermissions.Read));

        group.MapPost("/{entryName}/invalidations", async (
            string entryName,
            CacheInvalidationRequest request,
            CachePolicyControlPlane controlPlane,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await controlPlane.InvalidateAsync(
                    entryName,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
            {
                var policy = controlPlane.Get(entryName);
                if (policy is null)
                {
                    return NotFound(mapper, httpContext, ObservabilityAdminErrorCodes.CachePolicyNotFound);
                }

                if (!policy.CanInvalidate)
                {
                    return BadRequest(
                        mapper,
                        httpContext,
                        ObservabilityAdminErrorCodes.CachePolicyNotInvalidatable);
                }

                return BadRequest(
                    mapper,
                    httpContext,
                    ObservabilityAdminErrorCodes.CacheInvalidationInvalid);
            }

            return Results.Ok(result);
        })
        .WithName("observabilityInvalidateCachePolicy")
        .Produces<CacheInvalidationResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireAuthorization(FullNetPermissionPolicies.For(
            ObservabilityCachePolicyPermissions.Invalidate));
    }

    private static IResult NotFound(
        IApiResultMapper mapper,
        HttpContext httpContext,
        string code) =>
        mapper.Map(
            Result<object?>.Failure(new Error(
                code,
                "The cache policy was not found.",
                ErrorType.NotFound)),
            httpContext);

    private static IResult BadRequest(
        IApiResultMapper mapper,
        HttpContext httpContext,
        string code) =>
        mapper.Map(
            Result<object?>.Failure(new Error(
                code,
                "The cache invalidation request is invalid.",
                ErrorType.Validation)),
            httpContext);
}
