using Full.NET.Abstractions.Messaging;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.RegisterAccount;

internal static class Endpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/register", async (
            RegisterAccountRequest request,
            ICommandDispatcher dispatcher,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await dispatcher.SendAsync<Command, RegisterAccountResponse>(
                    new Command(request),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityRegisterAccount")
        .Produces<RegisterAccountResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .AllowAnonymous()
        .RequireRateLimiting(IdentityModule.SessionMutationRateLimitPolicy);
    }
}
