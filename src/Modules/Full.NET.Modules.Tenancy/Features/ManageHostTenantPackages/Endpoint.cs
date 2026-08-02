using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenancy/tenant-packages")
            .WithTags("Tenancy");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            HostTenantPackageQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantPackagePermissions.Read));

        group.MapGet("/{packageId:guid}", async (
            Guid packageId,
            HostTenantPackageQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(packageId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantPackagePermissions.Read));

        group.MapPost("/", async (
            CreateHostTenantPackageRequest request,
            HostTenantPackageManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return mapper.Map(result, httpContext);
            }

            return Results.Created(
                $"/api/v1/tenancy/tenant-packages/{result.Value!.Id:D}",
                result.Value);
        })
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantPackagePermissions.Create));

        group.MapPut("/{packageId:guid}", async (
            Guid packageId,
            UpdateHostTenantPackageRequest request,
            HostTenantPackageManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(packageId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantPackagePermissions.Update));

        group.MapPost("/{packageId:guid}/disable", async (
            Guid packageId,
            HostTenantPackageManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(packageId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireAuthorization(FullNetPermissionPolicies.For(
            TenancyTenantPackagePermissions.Disable));
    }
}
