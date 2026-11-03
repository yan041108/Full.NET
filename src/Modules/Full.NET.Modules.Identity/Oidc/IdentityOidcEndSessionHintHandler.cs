using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>退出请求携带 hint 时必须通过组件验签；不能把无效 hint 静默降级成无 hint。</summary>
internal sealed class IdentityOidcEndSessionHintHandler
    : IOpenIddictServerHandler<OpenIddictServerEvents.ValidateEndSessionRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } = OpenIddictServerHandlerDescriptor
        .CreateBuilder<OpenIddictServerEvents.ValidateEndSessionRequestContext>()
        .UseSingletonHandler<IdentityOidcEndSessionHintHandler>()
        .SetOrder(OpenIddictServerHandlers.Session.ValidateAuthentication.Descriptor.Order + 500)
        .SetType(OpenIddictServerHandlerType.Custom).Build();

    public ValueTask HandleAsync(OpenIddictServerEvents.ValidateEndSessionRequestContext context)
    {
        if (!string.IsNullOrEmpty(context.Request.IdTokenHint) && context.IdentityTokenHintPrincipal is null)
            context.Reject(OpenIddictConstants.Errors.InvalidRequest, "The identity token hint is invalid.");
        return default;
    }
}
