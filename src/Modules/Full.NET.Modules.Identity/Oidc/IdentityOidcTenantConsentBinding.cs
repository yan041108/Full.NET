using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using OpenIddict.Abstractions;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>将交互同意绑定到中心会话、目标租户和经协议校验的授权请求。</summary>
internal sealed class IdentityOidcTenantConsentBinding(IDataProtectionProvider provider)
{
    private readonly ITimeLimitedDataProtector _protector = provider
        .CreateProtector("Full.NET.Identity.Oidc.TenantConsent.v1")
        .ToTimeLimitedDataProtector();

    public string Create(OpenIddictRequest request, Guid centerSessionId, Guid tenantId, Guid consentId) =>
        _protector.Protect(Fingerprint(request, centerSessionId, tenantId, consentId), TimeSpan.FromMinutes(5));

    public bool Matches(string? proof, OpenIddictRequest request, Guid centerSessionId, Guid tenantId, Guid consentId)
    {
        if (string.IsNullOrEmpty(proof) || proof.Length > 4096) return false;
        try
        {
            var issued = _protector.Unprotect(proof);
            var expected = Fingerprint(request, centerSessionId, tenantId, consentId);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(issued), Encoding.ASCII.GetBytes(expected));
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Fingerprint(OpenIddictRequest request, Guid centerSessionId, Guid tenantId, Guid consentId)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (centerSessionId == Guid.Empty || tenantId == Guid.Empty || consentId == Guid.Empty) throw new ArgumentException("会话、租户和同意标识必须有效。");
        var values = new List<string?>
        {
            centerSessionId.ToString("D"), tenantId.ToString("D"), consentId.ToString("D"), request.ClientId,
            request.RedirectUri, request.ResponseType, request.ResponseMode, request.Scope, request.State,
            request.Nonce, request.CodeChallenge, request.CodeChallengeMethod,
            request.Prompt, request.MaxAge?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.AcrValues, request.LoginHint,
        };
        var resources = request.GetResources().Order(StringComparer.Ordinal).ToArray();
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true);
        foreach (var value in values)
        {
            writer.Write(value is not null);
            if (value is not null) writer.Write(value);
        }
        writer.Write(resources.Length);
        foreach (var resource in resources) writer.Write(resource);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }
}
