using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity/registration-policy")
            .WithTags("IdentityRegistrationPolicy");

        group.MapGet("/", async (
            RegistrationPolicyService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(cancellationToken).ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityGetRegistrationPolicy")
        .Produces<RegistrationPolicyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireFullNetPermission(IdentityRegistrationPolicyPermissions.Read);

        group.MapPut("/", async (
            UpdateRegistrationPolicyRequest request,
            RegistrationPolicyService service,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityUpdateRegistrationPolicy")
        .Produces<RegistrationPolicyResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireFullNetPermission(IdentityRegistrationPolicyPermissions.Update);
    }
}
