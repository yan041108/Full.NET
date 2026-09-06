using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageLdapConnections;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/ldap-connections")
            .WithTags("IdentityLdapConnections");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            Guid? tenantId,
            string? nameContains,
            bool? isEnabled,
            LdapConnectionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                    page ?? 1,
                    pageSize ?? 20,
                    tenantId,
                    nameContains,
                    isEnabled,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityListLdapConnections")
        .Produces<PagedResult<LdapConnectionResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityLdapConnectionPermissions.Read);

        group.MapGet("/{connectionId:guid}", async (
            Guid connectionId,
            LdapConnectionQueryService queries,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.GetByIdAsync(connectionId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityGetLdapConnection")
        .Produces<LdapConnectionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityLdapConnectionPermissions.Read);

        group.MapPost("/", async (
            CreateLdapConnectionRequest request,
            LdapConnectionManagementService service,
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
                $"/api/v1/identity/ldap-connections/{result.Value!.Id:D}",
                result.Value);
        })
        .WithName("identityCreateLdapConnection")
        .Produces<LdapConnectionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .RequireFullNetPermission(IdentityLdapConnectionPermissions.Create);

        group.MapPut("/{connectionId:guid}", async (
            Guid connectionId,
            UpdateLdapConnectionRequest request,
            LdapConnectionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(connectionId, request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityUpdateLdapConnection")
        .Produces<LdapConnectionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireFullNetPermission(IdentityLdapConnectionPermissions.Update);

        group.MapPost("/{connectionId:guid}/disable", async (
            Guid connectionId,
            LdapConnectionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DisableAsync(connectionId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityDisableLdapConnection")
        .Produces<LdapConnectionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireFullNetPermission(IdentityLdapConnectionPermissions.Update);

        group.MapDelete("/{connectionId:guid}", async (
            Guid connectionId,
            LdapConnectionManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(connectionId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityDeleteLdapConnection")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityLdapConnectionPermissions.Delete);

        group.MapPost("/{connectionId:guid}/test-connection", async (
            Guid connectionId,
            LdapConnectionOperationsService operations,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await operations.TestConnectionAsync(connectionId, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityTestLdapConnection")
        .Produces<TestLdapConnectionResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityLdapConnectionPermissions.Test);

        group.MapPost("/{connectionId:guid}/test-authentication", async (
            Guid connectionId,
            TestLdapAuthenticationRequest request,
            LdapConnectionOperationsService operations,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await operations.TestAuthenticationAsync(
                    connectionId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityTestLdapAuthentication")
        .Produces<TestLdapAuthenticationResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityLdapConnectionPermissions.Test);

        group.MapPost("/{connectionId:guid}/preview-sync", async (
            Guid connectionId,
            PreviewLdapSyncRequest request,
            LdapConnectionOperationsService operations,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await operations.PreviewSyncAsync(
                    connectionId,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityPreviewLdapSync")
        .Produces<PreviewLdapSyncResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityLdapConnectionPermissions.PreviewSync);
    }
}
