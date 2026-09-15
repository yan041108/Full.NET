using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcSigningKeyManagementAssertions
{
    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        using var keyA = RSA.Create(3072);
        using var keyB = RSA.Create(3072);
        var settings = IdentityOidcSigningKeyRotationAssertions.BuildDualPrivateKeySettings(
            keyA,
            keyB,
            IdentityOidcSigningKeyRotationAssertions.KeyBId);
        using var factory = new FullNetApiFactory(provider, connectionString, settings);
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyListsConfiguredKeysWithoutPrivateMaterialAsync(client, cancellationToken);
        await VerifyActivateSwitchesActiveSigningKeyAsync(client, cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/oidc-signing-keys");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await factory.CreateHostAccessTokenAsync(
                ["platform.dashboard.read"],
                cancellationToken));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task VerifyListsConfiguredKeysWithoutPrivateMaterialAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/oidc-signing-keys");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
        StringAssert.Contains(rawBody, IdentityOidcSigningKeyRotationAssertions.KeyAId);
        StringAssert.Contains(rawBody, IdentityOidcSigningKeyRotationAssertions.KeyBId);
        Assert.IsFalse(rawBody.Contains("PrivateKeyPem", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(rawBody.Contains("privateKeyPem", StringComparison.OrdinalIgnoreCase));

        var payload = await response.Content.ReadFromJsonAsync<OidcSigningKeyListResponse>(cancellationToken);
        Assert.IsNotNull(payload);
        Assert.AreEqual(IdentityOidcSigningKeyRotationAssertions.KeyBId, payload.ActiveSigningKeyId);
        Assert.IsFalse(payload.UsesEphemeralDevelopmentKey);
        Assert.AreEqual(2, payload.Keys.Count);
        Assert.IsTrue(payload.Keys.Any(key => key.KeyId == IdentityOidcSigningKeyRotationAssertions.KeyBId && key.IsActive));
        Assert.IsTrue(payload.Keys.Any(key => key.KeyId == IdentityOidcSigningKeyRotationAssertions.KeyAId && !key.IsActive));
        Assert.IsTrue(payload.Keys.All(key => !string.IsNullOrWhiteSpace(key.PublicKeyPem)));
        Assert.IsTrue(payload.Keys.Single(key => key.IsActive).HasPrivateKey);
        Assert.IsTrue(payload.Keys.Single(key => key.KeyId == IdentityOidcSigningKeyRotationAssertions.KeyAId).HasPrivateKey);
    }

    private static async Task VerifyActivateSwitchesActiveSigningKeyAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var legacyFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.AreEqual(
            IdentityOidcSigningKeyRotationAssertions.KeyBId,
            IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(legacyFlow.AccessToken, "kid"));

        using var activateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/oidc-signing-keys/{IdentityOidcSigningKeyRotationAssertions.KeyAId}/activate");
        activateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var activateResponse = await client.SendAsync(activateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, activateResponse.StatusCode);
        var activated = await activateResponse.Content.ReadFromJsonAsync<OidcSigningKeyListResponse>(cancellationToken);
        Assert.IsNotNull(activated);
        Assert.AreEqual(IdentityOidcSigningKeyRotationAssertions.KeyAId, activated.ActiveSigningKeyId);
        Assert.IsTrue(activated.Keys.Single(key => key.KeyId == IdentityOidcSigningKeyRotationAssertions.KeyAId).IsActive);

        using var legacyMeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        legacyMeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", legacyFlow.AccessToken);
        using var legacyMeResponse = await client.SendAsync(legacyMeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, legacyMeResponse.StatusCode);

        var rotatedFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.AreEqual(
            IdentityOidcSigningKeyRotationAssertions.KeyAId,
            IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(rotatedFlow.AccessToken, "kid"));
    }
}