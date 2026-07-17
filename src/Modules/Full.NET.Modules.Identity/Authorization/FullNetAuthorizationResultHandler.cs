using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Identity.Authorization;

internal sealed class FullNetAuthorizationResultHandler
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden)
        {
            return _fallback.HandleAsync(next, context, policy, authorizeResult);
        }

        return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "The current identity does not have the required permission.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "authorization.permission_denied",
                    ["traceId"] = context.TraceIdentifier,
                })
            .ExecuteAsync(context);
    }
}
