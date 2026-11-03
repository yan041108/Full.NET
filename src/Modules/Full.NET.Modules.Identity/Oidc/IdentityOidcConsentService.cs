using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using OpenIddict.Abstractions;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>第三方明确同意按用户/客户端/精确 scope 保存为永久授权；第一方预授权单独判定。</summary>
internal sealed class IdentityOidcConsentService(IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations, ICommandTransaction transaction, IClock clock,
    OidcAuthenticationEventWriter audit)
{
    public async Task<string?> FindAsync(ClaimsPrincipal center, OpenIddictRequest request, CancellationToken token)
    {
        var application = await applications.FindByClientIdAsync(request.ClientId!, token).ConfigureAwait(false);
        if (application is null) return null;
        var id = await applications.GetIdAsync(application, token).ConfigureAwait(false);
        var subject = center.FindFirstValue(IdentityOidcCenterAuthenticationDefaults.UserIdClaim);
        await foreach (var grant in authorizations.FindAsync(subject: subject, client: id,
            status: OpenIddictConstants.Statuses.Valid, type: OpenIddictConstants.AuthorizationTypes.Permanent,
            scopes: request.GetScopes(), cancellationToken: token).ConfigureAwait(false))
            return await authorizations.GetIdAsync(grant, token).ConfigureAwait(false);
        return null;
    }

    public Task<string> GrantAsync(ClaimsPrincipal principal, OpenIddictRequest request, CancellationToken token)
        => transaction.ExecuteAsync(async ct =>
        {
            var descriptor = new OpenIddictAuthorizationDescriptor
            {
                ApplicationId = await applications.GetIdAsync((await applications.FindByClientIdAsync(request.ClientId!, ct)
                    .ConfigureAwait(false))!, ct).ConfigureAwait(false),
                Subject = principal.GetClaim(OpenIddictConstants.Claims.Subject), Principal = principal,
                Type = OpenIddictConstants.AuthorizationTypes.Permanent, Status = OpenIddictConstants.Statuses.Valid,
                CreationDate = clock.UtcNow
            };
            descriptor.Scopes.UnionWith(request.GetScopes());
            var grant = await authorizations.CreateAsync(descriptor, ct).ConfigureAwait(false);
            await audit.WriteAsync(Guid.Parse(descriptor.Subject!), null, "oidc.consent",
                "identity.oidc_consent_granted", true, ct, clientId: request.ClientId).ConfigureAwait(false);
            return (await authorizations.GetIdAsync(grant, ct).ConfigureAwait(false))!;
        }, token);
}
