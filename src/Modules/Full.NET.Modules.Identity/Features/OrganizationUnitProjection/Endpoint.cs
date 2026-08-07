using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.OrganizationUnitProjection;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/identity/organization-unit-projections/reconcile",
                async (
                    ReconcileOrganizationUnitProjectionRequest request,
                    ClaimsPrincipal principal,
                    PermissionClaimEvaluator permissionEvaluator,
                    OrganizationUnitProjectionReconciliationService reconciliation,
                    IApiResultMapper mapper,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    var permission = ResolvePermission(request.Mode);
                    if (permission is null)
                    {
                        return mapper.Map(
                            Result<ReconcileOrganizationUnitProjectionResponse>.Failure(
                                new Error(
                                    IdentityErrorCodes.OrganizationUnitProjectionInvalidMode,
                                    IdentityErrorCodes.OrganizationUnitProjectionInvalidMode,
                                    ErrorType.Validation)),
                            httpContext);
                    }

                    if (!permissionEvaluator.HasPermission(principal, permission))
                    {
                        return Results.Forbid();
                    }

                    var result = await reconciliation.ReconcileAsync(
                            request,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return mapper.Map(result, httpContext);
                })
            .WithTags("Identity")
            .Produces<ReconcileOrganizationUnitProjectionResponse>(StatusCodes.Status200OK)
            .RequireAuthorization();
    }

    private static string? ResolvePermission(string mode)
    {
        if (string.Equals(
                mode,
                IdentityOrganizationUnitProjectionReconciliationModes.DryRun,
                StringComparison.Ordinal))
        {
            return IdentityOrganizationUnitProjectionPermissions.ReconcileDryRun;
        }

        if (string.Equals(
                mode,
                IdentityOrganizationUnitProjectionReconciliationModes.Apply,
                StringComparison.Ordinal))
        {
            return IdentityOrganizationUnitProjectionPermissions.ReconcileApply;
        }

        return null;
    }
}