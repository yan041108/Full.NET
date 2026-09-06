using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.K3Cloud.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.K3Cloud.Features.ManageConnectionConfigs;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/k3cloud/connection-configs")
            .WithTags("K3CloudConnectionConfigs");

        group.MapGet("/", async (
            K3CloudConnectionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("k3cloudListConnectionConfigs")
        .Produces<IReadOnlyList<K3CloudConnectionConfigResponse>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(K3CloudConnectionPermissions.Read));

        group.MapGet("/{connectionConfigId:guid}", async (
            Guid connectionConfigId,
            K3CloudConnectionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(connectionConfigId, cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("k3cloudGetConnectionConfig")
        .Produces<K3CloudConnectionConfigResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(K3CloudConnectionPermissions.Read));

        group.MapPost("/", async (
            CreateK3CloudConnectionConfigRequest request,
            K3CloudConnectionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created($"/api/v1/k3cloud/connection-configs/{result.Value!.Id:D}", result.Value);
        })
        .WithName("k3cloudCreateConnectionConfig")
        .Produces<K3CloudConnectionConfigResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(FullNetPermissionPolicies.For(K3CloudConnectionPermissions.Create));

        group.MapPut("/{connectionConfigId:guid}", async (
            Guid connectionConfigId,
            UpdateK3CloudConnectionConfigRequest request,
            K3CloudConnectionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service
                .UpdateAsync(connectionConfigId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("k3cloudUpdateConnectionConfig")
        .Produces<K3CloudConnectionConfigResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(K3CloudConnectionPermissions.Update));

        group.MapPost("/{connectionConfigId:guid}/test", async (
            Guid connectionConfigId,
            K3CloudConnectionOperationsService operations,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await operations
                .TestConnectivityAsync(connectionConfigId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("k3cloudTestConnectionConfig")
        .Produces<TestK3CloudConnectionConfigResult>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(K3CloudConnectionPermissions.Test));
    }
}
