using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Domain;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>在协议错误响应写出前保存拒绝事实；不读取请求里的令牌或猜测拒绝请求的用户身份。</summary>
internal sealed class IdentityOidcProtocolRejectionAuditHandler(IOpenIddictApplicationManager applications,
    OidcAuthenticationEventWriter audit, ICommandTransaction transaction)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessErrorContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.ProcessErrorContext>()
            .UseScopedHandler<IdentityOidcProtocolRejectionAuditHandler>()
            .SetOrder(int.MinValue + 100_000)
            .SetType(OpenIddictServerHandlerType.Custom).Build();

    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessErrorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        const string writtenKey = "fullnet.identity.protocol_rejection_audited";
        if (context.Transaction.GetProperty<string>(writtenKey) is not null) return;
        string? clientId = null;
        var requested = context.Transaction.Request?.ClientId;
        if (requested is { Length: > 0 and <= 128 }
            && await applications.FindByClientIdAsync(requested, context.CancellationToken).ConfigureAwait(false) is not null)
            clientId = requested;
        await transaction.ExecuteAsync(async token =>
        {
            await audit.WriteAsync(null, null, AuthenticationEventCatalog.ProtocolRejected,
                AuthenticationEventCatalog.ProtocolResult(context.Error), false, token,
                clientId: clientId, authenticationMethod: "oidc").ConfigureAwait(false);
            return true;
        }, context.CancellationToken).ConfigureAwait(false);
        context.Transaction.SetProperty(writtenKey, "written");
    }
}
