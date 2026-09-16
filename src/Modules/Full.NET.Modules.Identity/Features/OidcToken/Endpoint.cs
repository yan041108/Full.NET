using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Serialization;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Full.NET.Modules.Identity.Features.OidcToken;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints, IdentityOidcOptions options)
    {
        if (!options.Enable)
        {
            return;
        }

        endpoints.MapPost("/connect/token", HandleTokenAsync)
            .WithName("identityOidcToken")
            .WithTags("IdentityOidcProtocol");
    }

    private static async Task<IResult> HandleTokenAsync(
        HttpContext httpContext,
        IdentityOidcClientConfigResolver clientConfigResolver,
        CancellationToken cancellationToken)
    {
        var request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict request cannot be resolved.");
        if (!string.IsNullOrWhiteSpace(request.ClientId)
            && await clientConfigResolver.IsDisabledAsync(request.ClientId, cancellationToken)
                .ConfigureAwait(false))
        {
            return Results.Json(
                new IdentityOidcProtocolError("unauthorized_client", "The OIDC client is disabled."),
                IdentityJsonSerializerContext.Default.IdentityOidcProtocolError,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var authenticateResult = await httpContext.AuthenticateAsync(
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
                .ConfigureAwait(false);
            var principal = authenticateResult.Principal
                ?? throw new InvalidOperationException("The user details cannot be resolved.");
            return Results.SignIn(
                principal,
                authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            throw new InvalidOperationException("Client credentials grants are not supported.");
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }
}
