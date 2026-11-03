using OpenIddict.Abstractions;
using OpenIddict.Server;
using Full.NET.Modules.Identity.Configuration;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>内置 openid/offline_access 也受客户端精确策略约束，不能依赖组件的内置 scope 例外。</summary>
internal sealed class IdentityOidcClientScopeHandler(IOpenIddictApplicationManager applications, IOptions<IdentityOptions> options)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ValidateAuthorizationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } = OpenIddictServerHandlerDescriptor
        .CreateBuilder<OpenIddictServerEvents.ValidateAuthorizationRequestContext>()
        // 在组件默认 scope 权限检查之前统一检查内置与自定义 scope，保持一致的协议错误。
        .UseScopedHandler<IdentityOidcClientScopeHandler>()
        .SetOrder(OpenIddictServerHandlers.Authentication.ValidateGrantTypePermissions.Descriptor.Order - 500)
        .SetType(OpenIddictServerHandlerType.Custom).Build();

    public async ValueTask HandleAsync(OpenIddictServerEvents.ValidateAuthorizationRequestContext context)
    {
        var application = await applications.FindByClientIdAsync(context.ClientId ?? string.Empty, context.CancellationToken)
            .ConfigureAwait(false);
        if (application is null) { context.Reject(OpenIddictConstants.Errors.InvalidClient); return; }
        var client = IdentityOidcClientMetadata.Read(await applications.GetPropertiesAsync(application, context.CancellationToken).ConfigureAwait(false));
        if (!IdentityOidcResourceIndicator.IsAllowed(context.Request, client.ResourceAudience, options.Value.Audience))
        {
            context.Reject("invalid_target", "The requested resource is not allowed for this client.");
            return;
        }
        foreach (var scope in context.Request.GetScopes())
        {
            if (!await applications.HasPermissionAsync(application,
                OpenIddictConstants.Permissions.Prefixes.Scope + scope, context.CancellationToken).ConfigureAwait(false))
            {
                context.Reject(OpenIddictConstants.Errors.InvalidScope, "The requested scope is not allowed for this client.");
                return;
            }
        }
    }
}
