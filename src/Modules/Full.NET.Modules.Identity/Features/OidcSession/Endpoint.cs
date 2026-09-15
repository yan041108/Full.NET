using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Identity.Features.OidcSession;

internal sealed class IdentityOidcCenterLoginRequest
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ReturnUrl { get; set; } = string.Empty;
}

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints, IdentityOidcOptions options)
    {
        if (!options.Enable)
        {
            return;
        }

        endpoints.MapPost("/api/v1/identity/oidc/login", HandleLoginAsync)
            .WithName("identityOidcCenterLogin")
            .WithTags("IdentityOidcSession")
            .AllowAnonymous();
        endpoints.MapPost("/api/v1/identity/oidc/logout", HandleLogoutAsync)
            .WithName("identityOidcCenterLogout")
            .WithTags("IdentityOidcSession")
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleLoginAsync(
        IdentityOidcCenterLoginRequest request,
        IdentityOidcAuthorizationService authorizationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var signInResult = await authorizationService.SignInCenterAsync(
                request.Username,
                request.Password,
                cancellationToken)
            .ConfigureAwait(false);
        if (!signInResult.Succeeded)
        {
            return Results.Unauthorized();
        }

        await httpContext.SignInAsync(
                IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme,
                signInResult.Principal!,
                signInResult.Properties)
            .ConfigureAwait(false);
        return Results.Ok(new { returnUrl = request.ReturnUrl });
    }

    private static async Task<IResult> HandleLogoutAsync(
        HttpContext httpContext,
        IdentityOidcAuthorizationService authorizationService,
        CancellationToken cancellationToken)
    {
        await authorizationService.SignOutCenterAsync(httpContext, cancellationToken)
            .ConfigureAwait(false);
        return Results.NoContent();
    }
}
