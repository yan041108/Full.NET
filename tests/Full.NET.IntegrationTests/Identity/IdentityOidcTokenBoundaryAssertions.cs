using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcTokenBoundaryAssertions
{
    private const string BoundaryKeyId = "fixture-oidc-boundary-key";
    private const string WrongAudience = "https://evil.example/resources";
    private const string WrongIssuer = "https://evil.example/identity";

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
        var validAudience = sourceToken.Audiences.FirstOrDefault()
            ?? IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(flow.AccessToken, "aud")
            ?? "Full.NET.Api";
        var validIssuer = sourceToken.Issuer
            ?? IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(flow.AccessToken, "iss")
            ?? "https://localhost/identity";

        await VerifyMeRejectsTokenAsync(
            client,
            ResignAccessToken(
                flow.AccessToken,
                signingKey,
                BoundaryKeyId,
                validIssuer,
                WrongAudience),
            "wrong audience",
            cancellationToken);
        await VerifyMeRejectsTokenAsync(
            client,
            ResignAccessToken(
                flow.AccessToken,
                signingKey,
                BoundaryKeyId,
                WrongIssuer,
                validAudience),
            "unknown issuer",
            cancellationToken);
        await VerifyMeRejectsTokenAsync(
            client,
            ResignAccessToken(
                flow.AccessToken,
                signingKey,
                BoundaryKeyId,
                validIssuer,
                validAudience,
                expiresUtc: DateTime.UtcNow.AddHours(-1),
                notBeforeUtc: DateTime.UtcNow.AddHours(-2)),
            "expired lifetime",
            cancellationToken);
        await VerifyMeRejectsTokenAsync(
            client,
            "not-a-jwt",
            "malformed bearer token",
            cancellationToken);
        await VerifyMeRejectsTokenAsync(
            client,
            ResignAccessToken(
                flow.AccessToken,
                signingKey,
                BoundaryKeyId,
                validIssuer,
                validAudience,
                claimReplacements: new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [FullNetIdentityClaimTypes.ApplicationSessionId] = Guid.CreateVersion7().ToString("D"),
                }),
            "forged application session id",
            cancellationToken);
        await VerifyMeRejectsTokenAsync(
            client,
            ResignAccessToken(
                flow.AccessToken,
                signingKey,
                BoundaryKeyId,
                validIssuer,
                validAudience,
                claimReplacements: new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [JwtRegisteredClaimNames.Sub] = Guid.CreateVersion7().ToString("D"),
                }),
            "forged subject",
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
        DateTime? expiresUtc = null,
        DateTime? notBeforeUtc = null,
        IReadOnlyDictionary<string, object>? claimReplacements = null)
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

        if (claimReplacements is not null)
        {
            foreach (var (claimType, claimValue) in claimReplacements)
            {
                claims[claimType] = claimValue;
            }
        }

        var signingKey = new RsaSecurityKey(privateKey) { KeyId = keyId };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
            Expires = expiresUtc ?? token.ValidTo,
            NotBefore = notBeforeUtc ?? token.ValidFrom,
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