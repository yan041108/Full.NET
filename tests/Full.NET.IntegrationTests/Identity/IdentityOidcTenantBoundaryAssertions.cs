using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcTenantBoundaryAssertions
{
    private const string BoundaryKeyId = "fixture-oidc-tenant-boundary-key";

    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        using var signingKey = RSA.Create(3072);
        using var factory = new FullNetApiFactory(
            provider,
            connectionString,
            BuildSettings(signingKey));
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.AccessToken));

        var sourceToken = new JsonWebToken(flow.AccessToken);
        var audience = sourceToken.Audiences.FirstOrDefault()
            ?? IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(flow.AccessToken, "aud")
            ?? "Full.NET.Api";
        var issuer = sourceToken.Issuer
            ?? IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(flow.AccessToken, "iss")
            ?? "https://localhost/identity";

        await VerifyMeRejectsTokenAsync(
            client,
            ResignAccessToken(
                flow.AccessToken,
                signingKey,
                BoundaryKeyId,
                issuer,
                audience,
                Guid.CreateVersion7()),
            "forged tenant claim",
            cancellationToken);

        using var validMeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        validMeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var validMeResponse = await client.SendAsync(validMeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, validMeResponse.StatusCode);
    }

    private static async Task VerifyMeRejectsTokenAsync(
        HttpClient client,
        string accessToken,
        string scenario,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            response.StatusCode,
            $"Resource API must reject tokens with {scenario}.");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.IsTrue(body.Contains("type", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(body.Contains("\"error\":\"invalid_token\"", StringComparison.Ordinal));
        IdentityOidcErrorResponseAssertions.AssertDoesNotLeakInternalDetails(
            body,
            $"Resource API rejection for {scenario}");
    }

    private static string ResignAccessToken(
        string accessToken,
        RSA privateKey,
        string keyId,
        string issuer,
        string audience,
        Guid forgedTenantId)
    {
        var token = new JsonWebToken(accessToken);
        var claims = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var claim in token.Claims)
        {
            if (claim.Type is JwtRegisteredClaimNames.Iss or JwtRegisteredClaimNames.Aud)
            {
                continue;
            }

            claims[claim.Type] = claim.Value;
        }

        claims[FullNetIdentityClaimTypes.TenantId] = forgedTenantId.ToString("D");

        var signingKey = new RsaSecurityKey(privateKey) { KeyId = keyId };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
            Expires = token.ValidTo,
            NotBefore = token.ValidFrom,
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static IReadOnlyDictionary<string, string?> BuildSettings(RSA key)
    {
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "false",
            ["Identity:Oidc:ActiveSigningKeyId"] = BoundaryKeyId,
            [$"Identity:Oidc:SigningKeys:{BoundaryKeyId}:PublicKeyPem"] = key.ExportRSAPublicKeyPem(),
            [$"Identity:Oidc:SigningKeys:{BoundaryKeyId}:PrivateKeyPem"] = key.ExportRSAPrivateKeyPem(),
        };
        return settings;
    }
}