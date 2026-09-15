using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Full.NET.Modules.Identity.Oidc;

internal sealed class IdentityOidcServerOptionsConfigurer(IdentityOidcSigningKeyRing keyRing)
    : IConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options)
    {
        options.SigningCredentials.Add(keyRing.SigningCredentials);
    }
}
