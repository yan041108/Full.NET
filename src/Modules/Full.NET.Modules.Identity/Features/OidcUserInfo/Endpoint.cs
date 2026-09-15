using Full.NET.Modules.Identity.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.Modules.Identity.Features.OidcUserInfo;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints, IdentityOidcOptions options)
    {
        if (!options.Enable)
        {
            return;
        }

        endpoints.MapGet("/connect/userinfo", HandleUserInfoAsync)
            .WithName("identityOidcUserInfoGet")
            .WithTags("IdentityOidcProtocol");
        endpoints.MapPost("/connect/userinfo", HandleUserInfoAsync)
            .WithName("identityOidcUserInfo")
            .WithTags("IdentityOidcProtocol");
    }

    private static async Task<IResult> HandleUserInfoAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var authenticateResult = await httpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
        if (!authenticateResult.Succeeded || authenticateResult.Principal is null)
        {
            return Results.Challenge(
                authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var principal = authenticateResult.Principal;
        var response = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [Claims.Subject] = principal.GetClaim(Claims.Subject),
        };
        if (principal.HasScope(Scopes.Profile))
        {
            response[Claims.Name] = principal.GetClaim(Claims.Name);
            response[Claims.PreferredUsername] = principal.GetClaim(Claims.PreferredUsername);
        }

        return Results.Json(response);
    }
}
