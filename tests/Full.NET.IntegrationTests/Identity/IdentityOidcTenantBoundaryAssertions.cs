using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;
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
                tenantId: Guid.CreateVersion7()),
            "forged tenant claim",
            cancellationToken);

        using var validMeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        validMeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var validMeResponse = await client.SendAsync(validMeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, validMeResponse.StatusCode);

        var applicationSessionId = Guid.Parse(
            IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
                flow.AccessToken,
                FullNetIdentityClaimTypes.ApplicationSessionId)
            ?? throw new InvalidOperationException("OIDC access token is missing application session id."));
        using var acmeClient = factory.CreateClientForHost("acme.localhost");
        var acmeTenant = await acmeClient.GetFromJsonAsync<TenantSummary>(
            "/api/v1/tenancy/current",
            cancellationToken);
        Assert.IsNotNull(acmeTenant);
        var sessionTenantId = acmeTenant.Id;
        await SetApplicationSessionActiveTenantAsync(
            factory,
            applicationSessionId,
            sessionTenantId,
            cancellationToken);

        await VerifyMeRejectsTokenAsync(
            client,
            flow.AccessToken,
            "tenant claim missing while session is tenant-scoped",
            cancellationToken);
        await VerifyMeRejectsTokenAsync(
            client,
            ResignAccessToken(
                flow.AccessToken,
                signingKey,
                BoundaryKeyId,
                issuer,
                audience,
                tenantId: Guid.CreateVersion7()),
            "tenant claim mismatching tenant-scoped session",
            cancellationToken);

        var tenantEffectiveScope = BuildTenantEffectiveScope(sessionTenantId);
        await SetApplicationSessionTenantContextAsync(
            factory,
            applicationSessionId,
            sessionTenantId,
            tenantEffectiveScope,
            cancellationToken);
        await VerifyMeRejectsTokenAsync(
            client,
            ResignAccessToken(
                flow.AccessToken,
                signingKey,
                BoundaryKeyId,
                issuer,
                audience,
                tenantId: sessionTenantId),
            "host effective scope on tenant-scoped session",
            cancellationToken);

        var matchingTenantToken = ResignAccessToken(
            flow.AccessToken,
            signingKey,
            BoundaryKeyId,
            issuer,
            audience,
            tenantId: sessionTenantId,
            effectiveScope: tenantEffectiveScope);
        using var matchingMeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        matchingMeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", matchingTenantToken);
        using var matchingMeResponse = await acmeClient.SendAsync(matchingMeRequest, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.OK,
            matchingMeResponse.StatusCode,
            "Matching tenant claim and effective scope must keep tenant-scoped sessions authorized.");

        using var missingTenantClient = factory.CreateClientForHost("missing.localhost");
        await VerifyTenantContextMismatchAsync(
            missingTenantClient,
            matchingTenantToken,
            "/api/v1/tenancy/current",
            "tenant-scoped token on unknown tenant host",
            cancellationToken);
        await VerifyTenantContextMismatchAsync(
            missingTenantClient,
            matchingTenantToken,
            "/api/v1/me",
            "tenant-scoped token profile on unknown tenant host",
            cancellationToken);
    }

    private static string BuildTenantEffectiveScope(Guid tenantId) => $"tenant:{tenantId:N}";

    private static async Task VerifyTenantContextMismatchAsync(
        HttpClient client,
        string accessToken,
        string path,
        string scenario,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Forbidden,
            response.StatusCode,
            $"Tenant-scoped OIDC token must not authorize {scenario}.");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var problem = JsonDocument.Parse(body);
        Assert.AreEqual(
            "tenancy.context_mismatch",
            problem.RootElement.GetProperty("code").GetString());
        IdentityOidcErrorResponseAssertions.AssertDoesNotLeakInternalDetails(
            body,
            $"Tenant context mismatch for {scenario}");
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

    private static async Task SetApplicationSessionActiveTenantAsync(
        FullNetApiFactory factory,
        Guid applicationSessionId,
        Guid activeTenantId,
        CancellationToken cancellationToken) =>
        await SetApplicationSessionTenantContextAsync(
            factory,
            applicationSessionId,
            activeTenantId,
            "host",
            cancellationToken);

    private static async Task SetApplicationSessionTenantContextAsync(
        FullNetApiFactory factory,
        Guid applicationSessionId,
        Guid activeTenantId,
        string effectiveScope,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var executor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var affectedRows = await executor.ExecuteAsync(
            new SqlStatement(
                "integration.identity.oidc.set_application_session_tenant_context",
                """
                UPDATE fn_identity_oidc_application_session
                SET ActiveTenantId = @ActiveTenantId,
                    EffectiveScope = @EffectiveScope,
                    UpdatedAtUtc = @UpdatedAtUtc
                WHERE Id = @ApplicationSessionId
                """,
                SqlDataScope.HostOnly),
            new
            {
                ApplicationSessionId = applicationSessionId,
                ActiveTenantId = activeTenantId,
                EffectiveScope = effectiveScope,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            },
            cancellationToken);
        Assert.AreEqual(1, affectedRows);
    }

    private static string ResignAccessToken(
        string accessToken,
        RSA privateKey,
        string keyId,
        string issuer,
        string audience,
        Guid? tenantId = null,
        string? effectiveScope = null)
    {
        var token = new JsonWebToken(accessToken);
        var claims = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var claim in token.Claims)
        {
            if (claim.Type is JwtRegisteredClaimNames.Iss or JwtRegisteredClaimNames.Aud)
            {
                continue;
            }

            if (claim.Type == FullNetIdentityClaimTypes.TenantId && tenantId.HasValue)
            {
                continue;
            }

            if (claim.Type == FullNetIdentityClaimTypes.Scope && effectiveScope is not null)
            {
                continue;
            }

            claims[claim.Type] = claim.Value;
        }

        if (tenantId.HasValue)
        {
            claims[FullNetIdentityClaimTypes.TenantId] = tenantId.Value.ToString("D");
        }

        if (effectiveScope is not null)
        {
            claims[FullNetIdentityClaimTypes.Scope] = effectiveScope;
        }

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