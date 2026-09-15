using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerHandlers;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>
/// OpenIddict 在 refresh grant 上会忽略 <see cref="IOpenIddictTokenManager.TryRedeemAsync"/> 的并发失败；
/// 该处理器在签发新令牌前强制原子 redeem，失败时按 invalid_grant 拒绝（V09）。
/// </summary>
internal sealed class IdentityOidcRefreshTokenReuseHandler(IOpenIddictTokenManager tokenManager)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor
            .CreateBuilder<OpenIddictServerEvents.ProcessSignInContext>()
            .UseScopedHandler<IdentityOidcRefreshTokenReuseHandler>()
            .SetOrder(RedeemTokenEntry.Descriptor.Order - 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessSignInContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.EndpointType is not OpenIddictServerEndpointType.Token
            || context.Request is null
            || !context.Request.IsRefreshTokenGrantType())
        {
            return;
        }

        var notification = context.Transaction.GetProperty<OpenIddictServerEvents.ProcessAuthenticationContext>(
            typeof(OpenIddictServerEvents.ProcessAuthenticationContext).FullName!)
            ?? throw new InvalidOperationException("The OpenIddict authentication context cannot be resolved.");

        var principal = notification.RefreshTokenPrincipal;
        var identifier = principal?.GetTokenId();
        if (string.IsNullOrEmpty(identifier))
        {
            return;
        }

        var token = await tokenManager.FindByIdAsync(identifier, context.CancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return;
        }

        if (!await tokenManager.TryRedeemAsync(token, context.CancellationToken).ConfigureAwait(false))
        {
            context.Reject(
                error: Errors.InvalidGrant,
                description: "The specified refresh token is no longer valid.",
                uri: "https://documentation.openiddict.com/errors/ID2012");
        }
    }
}