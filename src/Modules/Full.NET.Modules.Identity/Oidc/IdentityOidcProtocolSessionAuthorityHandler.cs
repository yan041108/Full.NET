using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerHandlers;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>
/// 在 token 端点签发新令牌前校验 OIDC 应用会话权威状态；
/// 覆盖授权码兑换与 refresh grant，避免仅依赖 OpenIddict 存储而绕过会话撤销。
/// </summary>
internal sealed class IdentityOidcProtocolSessionAuthorityHandler(
    IdentityOidcAccessSessionValidator sessionValidator)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor
            .CreateBuilder<OpenIddictServerEvents.ProcessSignInContext>()
            .UseScopedHandler<IdentityOidcProtocolSessionAuthorityHandler>()
            .SetOrder(IdentityOidcSignInHandler.Descriptor.Order + 1)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessSignInContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.EndpointType is not OpenIddictServerEndpointType.Token
            || context.Request is null
            || context.Principal is null
            || (!context.Request.IsAuthorizationCodeGrantType()
                && !context.Request.IsRefreshTokenGrantType()))
        {
            return;
        }

        if (!await sessionValidator.IsValidAsync(context.Principal, context.CancellationToken).ConfigureAwait(false))
        {
            context.Reject(
                error: Errors.InvalidGrant,
                description: "The user session is no longer active.");
        }
    }
}