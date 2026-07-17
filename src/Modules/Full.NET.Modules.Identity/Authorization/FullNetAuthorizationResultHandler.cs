using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Identity.Authorization;

internal sealed class FullNetAuthorizationResultHandler(IApiResultMapper resultMapper)
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

        return resultMapper.Map(
                Result<object?>.Failure(new Error(
                    Code: CommonErrorCodes.PermissionDenied,
                    Message:
                        "The current identity does not have the required permission.",
                    Type: ErrorType.Forbidden)),
                context)
            .ExecuteAsync(context);
    }
}
