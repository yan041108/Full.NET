using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageSuperAdministrators;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/super-administrators")
            .WithTags("Identity");

        group.MapGet("/", async (
            SuperAdministratorQueryService queries,
            CancellationToken cancellationToken) =>
            Results.Ok(await queries.ListAsync(cancellationToken)
                .ConfigureAwait(false)))
            .RequireFullNetPermission(
                IdentityAuthorizationContributor.SuperAdministratorsRead);

        group.MapGet("/audits", async (
            int? limit,
            SuperAdministratorQueryService queries,
            CancellationToken cancellationToken) =>
            Results.Ok(await queries.ListAuditsAsync(
                    limit ?? 50,
                    cancellationToken)
                .ConfigureAwait(false)))
            .RequireFullNetPermission(
                IdentityAuthorizationContributor.SuperAdministratorsRead);

        group.MapPost("/grant", async (
            GrantSuperAdministratorRequest request,
            ClaimsPrincipal principal,
            SuperAdministratorManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username)
                || string.IsNullOrEmpty(request.CurrentPassword))
            {
                return mapper.Map(InvalidRequest(), httpContext);
            }

            var result = await service.GrantAsync(
                    principal,
                    request.Username,
                    request.CurrentPassword,
                    request.TotpCode,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireFullNetPermission(
            IdentityAuthorizationContributor.SuperAdministratorsManage)
        .RequireRateLimiting("identity-super-administrator-write");

        group.MapPost("/{targetUserId:guid}/revoke", async (
            Guid targetUserId,
            RevokeSuperAdministratorRequest request,
            ClaimsPrincipal principal,
            SuperAdministratorManagementService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrEmpty(request.CurrentPassword))
            {
                return mapper.Map(InvalidRequest(), httpContext);
            }

            var result = await service.RevokeAsync(
                    principal,
                    targetUserId,
                    request.CurrentPassword,
                    request.TotpCode,
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .RequireFullNetPermission(
            IdentityAuthorizationContributor.SuperAdministratorsManage)
        .RequireRateLimiting("identity-super-administrator-write");
    }

    private static Result<SuperAdministratorChangeResponse> InvalidRequest() =>
        Result<SuperAdministratorChangeResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "Username and current password are required.",
            ErrorType.Validation));
}
