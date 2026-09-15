using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

internal static class NativeApiOidcE2EAssertions
{
    public static async Task VerifyOidcProtocolFlowAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connectionString, cancellationToken).ConfigureAwait(false);
        await using var host = await NativeApiProcessHost.StartAsync(
            artifact, provider, connectionString, BuildOidcSettings(),
            NativeAotTestTimeouts.ProcessStartup, cancellationToken).ConfigureAwait(false);
        using var client = host.CreateClient();
        await IdentityOidcProtocolAssertions.VerifyAsync(client, cancellationToken).ConfigureAwait(false);
        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    public static async Task VerifyConsumerEntryMinimalPathsAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connectionString, cancellationToken).ConfigureAwait(false);
        await using var host = await NativeApiProcessHost.StartAsync(
            artifact, provider, connectionString, BuildOidcSettings(),
            NativeAotTestTimeouts.ProcessStartup, cancellationToken).ConfigureAwait(false);
        using var client = host.CreateClient();
        var adminToken = await NativeApiE2EAssertions.LoginAsync(client, host.LogFilePath, cancellationToken).ConfigureAwait(false);
        await VerifyOidcOnlineSessionRevokeAsync(client, adminToken, host.LogFilePath, cancellationToken).ConfigureAwait(false);
        await VerifyOidcContextSwitchRejectedAsync(client, host.LogFilePath, cancellationToken).ConfigureAwait(false);
        await VerifyLegacyContextSwitchStillWorksAsync(client, adminToken, host.LogFilePath, cancellationToken).ConfigureAwait(false);
        await VerifyOidcProtectedToolAccessAsync(client, host.LogFilePath, cancellationToken).ConfigureAwait(false);
        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    public static async Task VerifyDualInstanceAuthorizationCodeExchangeAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connectionString, cancellationToken).ConfigureAwait(false);
        using var sharedKey = RSA.Create(3072);
        var settings = BuildOidcSettings(sharedKey, "native-aot-shared");
        await using var authorizeHost = await NativeApiProcessHost.StartAsync(
            artifact, provider, connectionString, settings, NativeAotTestTimeouts.ProcessStartup, cancellationToken).ConfigureAwait(false);
        await using var tokenHost = await NativeApiProcessHost.StartAsync(
            artifact, provider, connectionString, settings, NativeAotTestTimeouts.ProcessStartup, cancellationToken).ConfigureAwait(false);
        using var authorizeClient = authorizeHost.CreateClient();
        using var tokenClient = tokenHost.CreateClient();
        var pending = await IdentityOidcRelyingPartyFixture.BeginAuthorizationCodeFlowAsync(
            authorizeClient, IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri, "admin",
            NativeApiE2EAssertions.AdminPassword, requestOfflineAccess: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        var exchanged = await IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(
            tokenClient, pending.Code, pending.Verifier, pending.ClientId, pending.RedirectUri,
            null, expectedNonce: pending.Nonce, cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.IsFalse(string.IsNullOrWhiteSpace(exchanged.AccessToken));
        using var jwksResponse = await tokenClient.GetAsync("/.well-known/jwks", cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, jwksResponse.StatusCode);
        using var jwksDocument = JsonDocument.Parse(await jwksResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        Assert.IsTrue(jwksDocument.RootElement.TryGetProperty("keys", out var keys));
        Assert.IsTrue(keys.GetArrayLength() >= 1);
        await authorizeHost.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        await tokenHost.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        authorizeHost.AssertNoFatalMarkersInLogs();
        tokenHost.AssertNoFatalMarkersInLogs();
    }

    private static async Task VerifyOidcOnlineSessionRevokeAsync(
        HttpClient client, string adminToken, string logFilePath, CancellationToken cancellationToken)
    {
        var oidcResult = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client, IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri, null, "admin",
            NativeApiE2EAssertions.AdminPassword, requestOfflineAccess: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.IsFalse(string.IsNullOrWhiteSpace(oidcResult.AccessToken));
        using var meBeforeResponse = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/me", oidcResult.AccessToken), cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(meBeforeResponse, HttpStatusCode.OK, "OIDC access token before revoke", logFilePath, cancellationToken).ConfigureAwait(false);
        using var listResponse = await client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/identity/online-sessions?page=1&pageSize=50&usernameContains=admin", adminToken), cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(listResponse, HttpStatusCode.OK, "List host online sessions", logFilePath, cancellationToken).ConfigureAwait(false);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<HostOnlineSessionResponse>>(cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(page);
        var oidcSession = page.Items.SingleOrDefault(item => string.Equals(item.ClientId, IdentityOidcRelyingPartyFixture.PublicClientId, StringComparison.Ordinal));
        Assert.IsNotNull(oidcSession, "Expected an OIDC application session in online-sessions list.");
        using var revokeResponse = await client.SendAsync(
            AuthorizedJson(HttpMethod.Post, "/api/v1/identity/online-sessions/" + oidcSession.Id.ToString("D") + "/revoke", adminToken, new { }), cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(revokeResponse, HttpStatusCode.OK, "Revoke OIDC online session", logFilePath, cancellationToken).ConfigureAwait(false);
        using var meAfterResponse = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/me", oidcResult.AccessToken), cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, meAfterResponse.StatusCode);
        using var problem = JsonDocument.Parse(await meAfterResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        Assert.AreEqual(IdentityErrorCodes.SessionNotActive, problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyOidcContextSwitchRejectedAsync(HttpClient client, string logFilePath, CancellationToken cancellationToken)
    {
        var oidcResult = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client, IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret, "admin",
            NativeApiE2EAssertions.AdminPassword, requestOfflineAccess: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.IsFalse(string.IsNullOrWhiteSpace(oidcResult.AccessToken));
        using var availableResponse = await client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/tenancy/available", oidcResult.AccessToken), cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(availableResponse, HttpStatusCode.OK, "List available tenants with OIDC token", logFilePath, cancellationToken).ConfigureAwait(false);
        var available = await availableResponse.Content.ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(available);
        var developmentTenant = available.SingleOrDefault(tenant => tenant.Identifier == "local");
        Assert.IsNotNull(developmentTenant);
        using var switchResponse = await client.SendAsync(
            AuthorizedJson(HttpMethod.Put, "/api/v1/tenancy/context", oidcResult.AccessToken, new ChangeTenantContextRequest(developmentTenant.Id)), cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Forbidden, switchResponse.StatusCode);
        using var problem = JsonDocument.Parse(await switchResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        Assert.AreEqual(IdentityErrorCodes.OidcContextSwitchNotSupported, problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyLegacyContextSwitchStillWorksAsync(
        HttpClient client, string hostToken, string logFilePath, CancellationToken cancellationToken)
    {
        var tenantToken = await NativeApiE2EAssertions.EnterLocalTenantAsync(client, hostToken, cancellationToken).ConfigureAwait(false);
        using var response = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/tenancy/available", tenantToken), cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK, "Legacy tenant token remains valid", logFilePath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyOidcProtectedToolAccessAsync(HttpClient client, string logFilePath, CancellationToken cancellationToken)
    {
        var oidcResult = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client, IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri, null, "admin",
            NativeApiE2EAssertions.AdminPassword, requestOfflineAccess: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.IsFalse(string.IsNullOrWhiteSpace(oidcResult.AccessToken));
        using var toolsResponse = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/ai/agent-tools", oidcResult.AccessToken), cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(toolsResponse, HttpStatusCode.OK, "OIDC access token reaches AI tool catalog", logFilePath, cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, string?> BuildOidcSettings(RSA? sharedSigningKey = null, string? sharedSigningKeyId = null)
    {
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings);
        if (sharedSigningKey is not null && !string.IsNullOrWhiteSpace(sharedSigningKeyId))
        {
            settings["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "false";
            settings["Identity:Oidc:ActiveSigningKeyId"] = sharedSigningKeyId;
            settings["Identity:Oidc:SigningKeys:" + sharedSigningKeyId + ":PrivateKeyPem"] = sharedSigningKey.ExportRSAPrivateKeyPem();
            settings["Identity:Oidc:SigningKeys:" + sharedSigningKeyId + ":PublicKeyPem"] = sharedSigningKey.ExportRSAPublicKeyPem();
        }
        return settings;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage AuthorizedJson<T>(HttpMethod method, string url, string token, T body)
    {
        var request = Authorized(method, url, token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task AssertStatusAsync(
        HttpResponseMessage response, HttpStatusCode expected, string operation, string logFilePath, CancellationToken cancellationToken)
    {
        if (response.StatusCode == expected) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var logTail = File.Exists(logFilePath) ? File.ReadAllText(logFilePath) : string.Empty;
        if (logTail.Length > 4000) logTail = logTail[^4000..];
        Assert.Fail(operation + " failed. Expected " + expected + ", actual " + response.StatusCode + ". Response body: " + body
            + (string.IsNullOrEmpty(logTail) ? string.Empty : "\nNative log tail:\n" + logTail));
    }
}