using Full.NET.Abstractions.Messaging;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.RecoverAccount;

internal static class Endpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/recover-password", async (
            RequestPasswordRecoveryRequest request,
            ICommandDispatcher dispatcher,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await dispatcher.SendAsync<RequestCommand, AccountChallengeAcceptedResponse>(
                    new RequestCommand(request),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityRequestPasswordRecovery")
        .Produces<AccountChallengeAcceptedResponse>(StatusCodes.Status200OK)
        .AllowAnonymous()
        .RequireRateLimiting(IdentityModule.SessionMutationRateLimitPolicy);

        group.MapPost("/recover-password/confirm", async (
            ConfirmPasswordRecoveryRequest request,
            ICommandDispatcher dispatcher,
            IApiResultMapper mapper,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await dispatcher.SendAsync<ConfirmCommand, bool>(
                    new ConfirmCommand(request),
                    cancellationToken)
                .ConfigureAwait(false);
            return mapper.Map(result, httpContext);
        })
        .WithName("identityConfirmPasswordRecovery")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .AllowAnonymous()
        .RequireRateLimiting(IdentityModule.SessionMutationRateLimitPolicy);
    }
}
