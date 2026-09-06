using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.SelfServiceProfile;

internal static class UpdateEndpoint
{
    /// <summary>映射当前用户自助档案更新端点。</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/v1/me/profile", async (
            UpdateSelfServiceProfileRequest request,
            ClaimsPrincipal principal,
            ICommandDispatcher dispatcher,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await dispatcher.SendAsync<UpdateCommand, SelfServiceProfileResponse>(
                    new UpdateCommand(request, principal),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityUpdateSelfServiceProfile")
        .WithTags("IdentityMe")
        .Produces<SelfServiceProfileResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .RequireAuthorization();
    }
}
