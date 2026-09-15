using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Full.NET.Modules.Identity.Oidc;

internal sealed class IdentityOidcServerOptionsConfigurer(IdentityOidcSigningKeyRing keyRing)
    : IConfigureOptions<OpenIddictServerOptions>
{
    public void Configure(OpenIddictServerOptions options) =>
        IdentityOidcSigningCredentialSync.Apply(options, keyRing);
}
