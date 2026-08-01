using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Identity.Authorization;

internal sealed class FullNetAuthorizationResultHandler(IApiResultMapper resultMapper)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            if (context.Features.Get<SignatureAuthenticationFailureFeature>() is
                { } signatureFailure)
            {
                await resultMapper.Map(
                        Result<object?>.Failure(signatureFailure.Error),
                        context)
                    .ExecuteAsync(context)
                    .ConfigureAwait(false);
                return;
            }

            // 先执行认证方案的 Challenge 以保留 WWW-Authenticate，再补充统一错误正文。
            await _fallback.HandleAsync(next, context, policy, authorizeResult)
                .ConfigureAwait(false);
            if (context.Response.HasStarted)
            {
                return;
            }

            await resultMapper.Map(
                    Result<object?>.Failure(new Error(
                        Code: IdentityErrorCodes.SessionNotActive,
                        Message: "The current session is no longer active.",
                        Type: ErrorType.Unauthorized)),
                    context)
                .ExecuteAsync(context)
                .ConfigureAwait(false);
            return;
        }

        if (!authorizeResult.Forbidden)
        {
            await _fallback.HandleAsync(next, context, policy, authorizeResult)
                .ConfigureAwait(false);
            return;
        }

        await resultMapper.Map(
                Result<object?>.Failure(new Error(
                    Code: CommonErrorCodes.PermissionDenied,
                    Message:
                        "The current identity does not have the required permission.",
                    Type: ErrorType.Forbidden)),
                context)
            .ExecuteAsync(context)
            .ConfigureAwait(false);
    }
}
