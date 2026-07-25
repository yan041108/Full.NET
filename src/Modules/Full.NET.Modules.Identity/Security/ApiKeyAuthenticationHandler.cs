using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Security;

/// <summary>API Key 认证方案名称。</summary>
internal static class ApiKeyAuthenticationDefaults
{
    public const string AuthenticationScheme = "FullNET.ApiKey";
}

/// <summary>
/// 根据 Authorization: ApiKey 头校验哈希凭据并构造 Host 作用域主体。
/// </summary>
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyAuthenticationService authenticationService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(
        options,
        logger,
        encoder)
{
    private const string SchemePrefix = "ApiKey ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values))
        {
            return AuthenticateResult.NoResult();
        }

        var authorization = values.ToString();
        if (!authorization.StartsWith(SchemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var secret = authorization[SchemePrefix.Length..].Trim();
        if (string.IsNullOrEmpty(secret))
        {
            return AuthenticateResult.Fail("The API key is missing.");
        }

        var principal = await authenticationService
            .AuthenticateAsync(secret, Context.RequestAborted)
            .ConfigureAwait(false);
        if (principal is null)
        {
            return AuthenticateResult.Fail("The API key is invalid.");
        }

        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, ApiKeyAuthenticationDefaults.AuthenticationScheme));
    }
}

/// <summary>智能认证方案：按 Authorization 头在 JWT 与 API Key 间转发。</summary>
internal static class SmartAuthenticationDefaults
{
    public const string AuthenticationScheme = "FullNET.Smart";
}
