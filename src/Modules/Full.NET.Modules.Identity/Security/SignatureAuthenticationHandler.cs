using System.Text.Encodings.Web;
using Full.NET.Abstractions.Results;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Security;

/// <summary>请求签名认证方案名称。</summary>
internal static class SignatureAuthenticationDefaults
{
    public const string AuthenticationScheme = "FullNET.Signature";
}

/// <summary>
/// 根据 X-FullNET-* 签名头校验 HMAC 并构造主体；仅在 Endpoint 显式声明时启用。
/// </summary>
internal sealed class SignatureAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    SignatureAuthenticationService authenticationService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(
        options,
        loggerFactory,
        encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!HasSignatureHeaders(Request.Headers))
        {
            return AuthenticateResult.NoResult();
        }

        var result = await authenticationService
            .AuthenticateAsync(Context, Context.RequestAborted)
            .ConfigureAwait(false);
        if (result.Succeeded)
        {
            return AuthenticateResult.Success(
                new AuthenticationTicket(
                    result.Principal!,
                    SignatureAuthenticationDefaults.AuthenticationScheme));
        }

        Context.Features.Set(new SignatureAuthenticationFailureFeature
        {
            Error = result.Error ?? new Error(
                IdentitySignatureErrorCodes.InvalidSignature,
                "Signature authentication failed.",
                ErrorType.Unauthorized),
        });
        return AuthenticateResult.Fail(result.Error?.Code ?? "signature_failed");
    }

    private static bool HasSignatureHeaders(IHeaderDictionary headers) =>
        headers.ContainsKey(SignatureAuthenticationOptions.AccessKeyIdHeader)
        || headers.ContainsKey(SignatureAuthenticationOptions.TimestampHeader)
        || headers.ContainsKey(SignatureAuthenticationOptions.NonceHeader)
        || headers.ContainsKey(SignatureAuthenticationOptions.SignatureHeader)
        || headers.ContainsKey(SignatureAuthenticationOptions.SignatureVersionHeader);
}
