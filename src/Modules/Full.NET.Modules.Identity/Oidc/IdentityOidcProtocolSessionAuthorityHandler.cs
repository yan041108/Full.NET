using System.Security.Claims;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerHandlers;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>
/// 在 token 端点签发新令牌前校验 OIDC 应用会话权威状态；
/// refresh grant 会先滑动延长应用会话，避免新令牌绑定已过期应用会话。
/// </summary>
internal sealed class IdentityOidcProtocolSessionAuthorityHandler(
    IdentityOidcAccessSessionValidator sessionValidator,
    IdentityOidcSessionService sessionService,
    IOpenIddictApplicationManager applications,
    IActiveTenantContextResolver tenants,
    IClock clock,
    IOptions<IdentityOptions> identityOptions)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>
{
    private readonly IdentityOptions _identityOptions = identityOptions.Value;
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

        // 换码与刷新必须重验当前客户端策略，不能使用收紧前授权码里的旧 scope。
        var application = await applications.FindByClientIdAsync(context.Request.ClientId ?? string.Empty,
            context.CancellationToken).ConfigureAwait(false);
        if (application is null) { context.Reject(Errors.InvalidGrant); return; }
        var client = IdentityOidcClientMetadata.Read(await applications.GetPropertiesAsync(application,
            context.CancellationToken).ConfigureAwait(false));
        if (client.IsDisabled) { context.Reject(Errors.InvalidGrant); return; }
        if (!IdentityOidcResourceIndicator.IsAllowed(context.Request, client.ResourceAudience, _identityOptions.Audience))
        { context.Reject("invalid_target", "The requested resource is not allowed for this client."); return; }
        if (client.RequireMfa && context.Principal.GetClaim("acr") != "urn:fullnet:acr:mfa")
        { context.Reject(Errors.InvalidGrant, "The client requires multifactor authentication."); return; }
        foreach (var scope in context.Principal.GetScopes())
        {
            if (!await applications.HasPermissionAsync(application, Permissions.Prefixes.Scope + scope,
                    context.CancellationToken).ConfigureAwait(false))
            {
                context.Reject(Errors.InvalidGrant, "The client authorization policy has changed.");
                return;
            }
        }

        // 令牌中的租户必须仍由权威租户服务判定为活动，不能只比较会话中的旧 TenantId。
        var tenantClaim = context.Principal.FindFirstValue(FullNetIdentityClaimTypes.TenantId);
        if (!string.IsNullOrEmpty(tenantClaim))
        {
            if (!Guid.TryParse(tenantClaim, out var tenantId) || tenantId == Guid.Empty)
            {
                context.Reject(Errors.InvalidGrant, "The user session is no longer active.");
                return;
            }
            try
            {
                if (await tenants.ResolveActiveByIdAsync(tenantId, context.CancellationToken).ConfigureAwait(false) is null)
                {
                    context.Reject(Errors.InvalidGrant, "The user session is no longer active.");
                    return;
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                context.Reject(Errors.InvalidGrant, "The user session is no longer active.");
                return;
            }
        }

        if (context.Request.IsRefreshTokenGrantType()
            && TryReadApplicationSessionId(context.Principal, out var applicationSessionId))
        {
            try
            {
                await TryExtendApplicationSessionAsync(
                        applicationSessionId,
                        context.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // 会话权威源不可用时不得继续签发 refresh 令牌，协议错误与普通校验失败保持一致。
                context.Reject(
                    error: Errors.InvalidGrant,
                    description: "The user session is no longer active.");
                return;
            }
        }

        // 换码和刷新以当前注册客户端的资源受众为准，普通 API 校验仍使用自己的受众。
        var expectedAudience = string.IsNullOrWhiteSpace(client.ResourceAudience)
            ? _identityOptions.Audience
            : client.ResourceAudience;
        if (!await sessionValidator.IsValidAsync(context.Principal, context.CancellationToken, expectedAudience).ConfigureAwait(false))
        {
            context.Reject(
                error: Errors.InvalidGrant,
                description: "The user session is no longer active.");
        }
    }

    private async Task TryExtendApplicationSessionAsync(
        Guid applicationSessionId,
        CancellationToken cancellationToken)
    {
        var record = await sessionService.FindApplicationSessionValidationAsync(
                applicationSessionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null
            || record.ApplicationRevokedAtUtc.HasValue
            || record.CenterRevokedAtUtc.HasValue
            || record.CenterExpiresAtUtc <= clock.UtcNow)
        {
            return;
        }

        var now = clock.UtcNow;
        var slidingExpiry = now.AddMinutes(_identityOptions.AccessTokenMinutes);
        var cappedExpiry = slidingExpiry < record.CenterExpiresAtUtc
            ? slidingExpiry
            : record.CenterExpiresAtUtc;
        if (cappedExpiry <= now)
        {
            return;
        }

        await sessionService.ExtendApplicationSessionAsync(
                applicationSessionId,
                cappedExpiry,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool TryReadApplicationSessionId(ClaimsPrincipal principal, out Guid applicationSessionId)
    {
        applicationSessionId = Guid.Empty;
        return Guid.TryParse(
            principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId),
            out applicationSessionId);
    }
}
