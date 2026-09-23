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
    private const string ContextSwitchGovernanceRedirectUri =
        "https://localhost:5013/signin-oidc-native-context-switch-governance";

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
        await VerifyOidcContextSwitchIssuesNewTokenAsync(client, host.LogFilePath, cancellationToken).ConfigureAwait(false);
        await VerifyLegacyContextSwitchStillWorksAsync(client, adminToken, host.LogFilePath, cancellationToken).ConfigureAwait(false);
        await VerifyOidcProtectedToolAccessAsync(client, host.LogFilePath, cancellationToken).ConfigureAwait(false);
        await VerifyOidcContextSwitchRefreshRotationAsync(client, host.LogFilePath, cancellationToken).ConfigureAwait(false);
        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    public static async Task VerifyRefreshReuseRejectedAsync(
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
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            NativeApiE2EAssertions.AdminPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));

        var firstTask = IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client, flow.RefreshToken!, IdentityOidcRelyingPartyFixture.PublicClientId, null, cancellationToken);
        var secondTask = IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client, flow.RefreshToken!, IdentityOidcRelyingPartyFixture.PublicClientId, null, cancellationToken);
        await Task.WhenAll(firstTask, secondTask);
        var firstResult = await firstTask;
        var secondResult = await secondTask;
        Assert.AreEqual(1, new[] { firstResult, secondResult }.Count(r => r.IsSuccessStatusCode));
        var thirdResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client, flow.RefreshToken!, IdentityOidcRelyingPartyFixture.PublicClientId, null, cancellationToken).ConfigureAwait(false);
        Assert.IsFalse(thirdResult.IsSuccessStatusCode);
        StringAssert.Contains(thirdResult.RawBody, "invalid_grant");
        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    public static async Task VerifyDualInstanceSigningKeyRotationOverlapAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connectionString, cancellationToken).ConfigureAwait(false);
        var dataProtectionAssets = IdentityOidcMultiInstanceTestSupport.CreateDataProtectionAssets();
        using var keyA = RSA.Create(3072);
        using var keyB = RSA.Create(3072);
        var settingsA = ToNativeSettings(IdentityOidcMultiInstanceTestSupport.BuildDualKeyFactorySettings(
            keyA,
            keyB,
            IdentityOidcSigningKeyRotationAssertions.KeyAId,
            dataProtectionAssets.KeyRingPath,
            dataProtectionAssets.CertificatePath));
        var settingsB = ToNativeSettings(IdentityOidcMultiInstanceTestSupport.BuildDualKeyFactorySettings(
            keyA,
            keyB,
            IdentityOidcSigningKeyRotationAssertions.KeyBId,
            dataProtectionAssets.KeyRingPath,
            dataProtectionAssets.CertificatePath));
        try
        {
        await using var primaryHost = await NativeApiProcessHost.StartAsync(
            artifact, provider, connectionString, settingsA, NativeAotTestTimeouts.ProcessStartup, cancellationToken).ConfigureAwait(false);
        await using var secondaryHost = await NativeApiProcessHost.StartAsync(
            artifact, provider, connectionString, settingsB, NativeAotTestTimeouts.ProcessStartup, cancellationToken).ConfigureAwait(false);
        using var primaryClient = primaryHost.CreateClient();
        using var secondaryClient = secondaryHost.CreateClient();
        var legacyFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            primaryClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            NativeApiE2EAssertions.AdminPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(
            IdentityOidcSigningKeyRotationAssertions.KeyAId,
            IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(legacyFlow.AccessToken, "kid"));
        using var legacyMe = await secondaryClient.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/me", legacyFlow.AccessToken), cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(legacyMe, HttpStatusCode.OK, "Legacy token on rotated native peer", secondaryHost.LogFilePath, cancellationToken).ConfigureAwait(false);
        var rotatedFlow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            secondaryClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            NativeApiE2EAssertions.AdminPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(
            IdentityOidcSigningKeyRotationAssertions.KeyBId,
            IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(rotatedFlow.AccessToken, "kid"));
        await primaryHost.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        await secondaryHost.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        primaryHost.AssertNoFatalMarkersInLogs();
        secondaryHost.AssertNoFatalMarkersInLogs();
        }
        finally
        {
            IdentityOidcMultiInstanceTestSupport.TryDeleteDirectory(dataProtectionAssets.RootPath);
        }
    }

    public static async Task VerifyCenterRestartPreservesAuthorizationExchangeAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connectionString, cancellationToken).ConfigureAwait(false);
        var dataProtectionAssets = IdentityOidcMultiInstanceTestSupport.CreateDataProtectionAssets();
        using var signingKey = RSA.Create(3072);
        var settings = BuildOidcSettings(
            signingKey,
            "native-aot-center-restart",
            dataProtectionAssets.KeyRingPath,
            dataProtectionAssets.CertificatePath);
        try
        {
        await using var host = await NativeApiProcessHost.StartAsync(
            artifact, provider, connectionString, settings, NativeAotTestTimeouts.ProcessStartup, cancellationToken).ConfigureAwait(false);
        using var client = host.CreateClient();
        var pending = await IdentityOidcRelyingPartyFixture.BeginAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            "admin",
            NativeApiE2EAssertions.AdminPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();

        await using var restartedHost = await NativeApiProcessHost.StartAsync(
            artifact, provider, connectionString, settings, NativeAotTestTimeouts.ProcessStartup, cancellationToken).ConfigureAwait(false);
        using var restartClient = restartedHost.CreateClient();
        var exchanged = await IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(
            restartClient,
            pending.Code,
            pending.Verifier,
            pending.ClientId,
            pending.RedirectUri,
            null,
            expectedNonce: pending.Nonce,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.IsFalse(string.IsNullOrWhiteSpace(exchanged.AccessToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(exchanged.RefreshToken));
        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            restartClient,
            exchanged.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken).ConfigureAwait(false);
        Assert.IsTrue(refreshResult.IsSuccessStatusCode, refreshResult.RawBody);
        await restartedHost.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        restartedHost.AssertNoFatalMarkersInLogs();
        }
        finally
        {
            IdentityOidcMultiInstanceTestSupport.TryDeleteDirectory(dataProtectionAssets.RootPath);
        }
    }

    public static async Task VerifyDualInstanceAuthorizationCodeExchangeAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connectionString, cancellationToken).ConfigureAwait(false);
        var dataProtectionAssets = IdentityOidcMultiInstanceTestSupport.CreateDataProtectionAssets();
        using var sharedKey = RSA.Create(3072);
        var settings = BuildOidcSettings(
            sharedKey,
            "native-aot-shared",
            dataProtectionAssets.KeyRingPath,
            dataProtectionAssets.CertificatePath);
        try
        {
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
        finally
        {
            IdentityOidcMultiInstanceTestSupport.TryDeleteDirectory(dataProtectionAssets.RootPath);
        }
    }

    public static async Task VerifyDualInstanceContextSwitchAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connectionString, cancellationToken).ConfigureAwait(false);
        var dataProtectionAssets = IdentityOidcMultiInstanceTestSupport.CreateDataProtectionAssets();
        using var sharedKey = RSA.Create(3072);
        var settings = BuildOidcSettings(
            sharedKey,
            "native-aot-context-switch",
            dataProtectionAssets.KeyRingPath,
            dataProtectionAssets.CertificatePath);
        try
        {
        await using var primaryHost = await NativeApiProcessHost.StartAsync(
            artifact, provider, connectionString, settings, NativeAotTestTimeouts.ProcessStartup, cancellationToken).ConfigureAwait(false);
        await using var peerHost = await NativeApiProcessHost.StartAsync(
            artifact, provider, connectionString, settings, NativeAotTestTimeouts.ProcessStartup, cancellationToken).ConfigureAwait(false);
        using var primaryClient = primaryHost.CreateClient();
        using var peerClient = peerHost.CreateClient();
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            primaryClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            NativeApiE2EAssertions.AdminPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.AccessToken));
        var developmentTenant = await GetDevelopmentTenantForContextSwitchAsync(
            primaryClient,
            flow.AccessToken,
            primaryHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);
        using var switchToTenantResponse = await primaryClient.SendAsync(
            AuthorizedJson(HttpMethod.Put, "/api/v1/tenancy/context", flow.AccessToken, new ChangeTenantContextRequest(developmentTenant.Id)),
            cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(
            switchToTenantResponse,
            HttpStatusCode.OK,
            "OIDC tenant context switch on primary native instance",
            primaryHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);
        var tenantToken = await switchToTenantResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(tenantToken);
        await AssertMeAcceptsTokenAsync(
            peerClient,
            tenantToken.AccessToken,
            "peer native instance after OIDC tenant context switch",
            peerHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);
        await AssertMeRejectsTokenAsync(
            peerClient,
            flow.AccessToken,
            "peer native instance with stale OIDC host token after context switch",
            peerHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);
        using var switchToHostResponse = await primaryClient.SendAsync(
            AuthorizedJson(HttpMethod.Put, "/api/v1/tenancy/context", tenantToken.AccessToken, new ChangeTenantContextRequest(null)),
            cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(
            switchToHostResponse,
            HttpStatusCode.OK,
            "OIDC host context switch round-trip on primary native instance",
            primaryHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);
        var hostToken = await switchToHostResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(hostToken);
        await AssertMeAcceptsTokenAsync(
            peerClient,
            hostToken.AccessToken,
            "peer native instance after OIDC host round-trip",
            peerHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);
        await AssertMeRejectsTokenAsync(
            peerClient,
            tenantToken.AccessToken,
            "peer native instance with stale OIDC tenant token after host round-trip",
            peerHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);
        await primaryHost.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        await peerHost.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        primaryHost.AssertNoFatalMarkersInLogs();
        peerHost.AssertNoFatalMarkersInLogs();
        }
        finally
        {
            IdentityOidcMultiInstanceTestSupport.TryDeleteDirectory(dataProtectionAssets.RootPath);
        }
    }

    public static async Task VerifyDualInstanceContextSwitchGovernanceAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connectionString, cancellationToken).ConfigureAwait(false);
        var dataProtectionAssets = IdentityOidcMultiInstanceTestSupport.CreateDataProtectionAssets();
        using var sharedKey = RSA.Create(3072);
        var settings = BuildOidcSettings(
            sharedKey,
            "native-aot-context-gov",
            dataProtectionAssets.KeyRingPath,
            dataProtectionAssets.CertificatePath);
        try
        {
        await using var primaryHost = await NativeApiProcessHost.StartAsync(
            artifact, provider, connectionString, settings, NativeAotTestTimeouts.ProcessStartup, cancellationToken).ConfigureAwait(false);
        await using var peerHost = await NativeApiProcessHost.StartAsync(
            artifact, provider, connectionString, settings, NativeAotTestTimeouts.ProcessStartup, cancellationToken).ConfigureAwait(false);
        using var primaryClient = primaryHost.CreateClient();
        using var peerClient = peerHost.CreateClient();
        var adminToken = await NativeApiE2EAssertions.LoginAsync(
            primaryClient,
            primaryHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);
        var clientId = $"na-gov-{Guid.NewGuid():N}"[..24];
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/oidc-clients")
        {
            Content = JsonContent.Create(new CreateOidcClientRequest(
                clientId,
                "Native context switch governance client",
                [ContextSwitchGovernanceRedirectUri],
                [],
                ["openid", "profile", "offline_access"],
                false,
                true,
                null)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var createResponse = await primaryClient.SendAsync(createRequest, cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(
            createResponse,
            HttpStatusCode.Created,
            "Create OIDC client for native context switch governance",
            primaryHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateOidcClientResponse>(cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(created);

        var authorizeUrl = BuildGovernanceAuthorizeUrl(clientId);
        using var warmAuthorizeResponse = await peerClient.GetAsync(authorizeUrl, cancellationToken).ConfigureAwait(false);
        Assert.IsTrue(
            warmAuthorizeResponse.StatusCode is HttpStatusCode.OK
                or HttpStatusCode.Redirect
                or HttpStatusCode.Found
                or HttpStatusCode.SeeOther,
            $"Peer authorize warm-up returned {warmAuthorizeResponse.StatusCode}.");

        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            primaryClient,
            clientId,
            ContextSwitchGovernanceRedirectUri,
            null,
            "admin",
            NativeApiE2EAssertions.AdminPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));

        var developmentTenant = await GetDevelopmentTenantForContextSwitchAsync(
            primaryClient,
            flow.AccessToken,
            primaryHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);
        using var switchToTenantResponse = await primaryClient.SendAsync(
            AuthorizedJson(HttpMethod.Put, "/api/v1/tenancy/context", flow.AccessToken, new ChangeTenantContextRequest(developmentTenant.Id)),
            cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(
            switchToTenantResponse,
            HttpStatusCode.OK,
            "OIDC tenant context switch before native governance disable",
            primaryHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);
        var tenantToken = await switchToTenantResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(tenantToken);

        using var disableRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/oidc-clients/{created!.Client.Id:D}/disable");
        disableRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var disableResponse = await primaryClient.SendAsync(disableRequest, cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(
            disableResponse,
            HttpStatusCode.OK,
            "Disable OIDC client after native context switch",
            primaryHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);

        await AssertDisabledClientRejectsTokensOnNativeInstanceAsync(
            peerClient,
            authorizeUrl,
            flow,
            tenantToken.AccessToken,
            clientId,
            "peer",
            peerHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);
        await AssertDisabledClientRejectsTokensOnNativeInstanceAsync(
            primaryClient,
            authorizeUrl,
            flow,
            tenantToken.AccessToken,
            clientId,
            "primary",
            primaryHost.LogFilePath,
            cancellationToken).ConfigureAwait(false);

        await primaryHost.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        await peerHost.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        primaryHost.AssertNoFatalMarkersInLogs();
        peerHost.AssertNoFatalMarkersInLogs();
        }
        finally
        {
            IdentityOidcMultiInstanceTestSupport.TryDeleteDirectory(dataProtectionAssets.RootPath);
        }
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

    private static async Task VerifyOidcContextSwitchIssuesNewTokenAsync(HttpClient client, string logFilePath, CancellationToken cancellationToken)
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
        await AssertStatusAsync(switchResponse, HttpStatusCode.OK, "OIDC tenant context switch", logFilePath, cancellationToken).ConfigureAwait(false);
        var switched = await switchResponse.Content.ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(switched);
        Assert.AreEqual(developmentTenant.Id, switched.Context.TenantId);
        using var meAfterResponse = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/me", switched.AccessToken), cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(meAfterResponse, HttpStatusCode.OK, "OIDC tenant token after switch", logFilePath, cancellationToken).ConfigureAwait(false);
        using var meBeforeResponse = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/me", oidcResult.AccessToken), cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, meBeforeResponse.StatusCode);
        using var toolsBeforeSwitchResponse = await client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/ai/agent-tools", oidcResult.AccessToken), cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            toolsBeforeSwitchResponse.StatusCode,
            "Stale OIDC host token must not reach AI tool catalog after context switch.");
        using var approvalBeforeSwitchResponse = await client.SendAsync(
            Authorized(
                HttpMethod.Get,
                "/api/v1/ai/agent/approvals/01981f2a-1200-7000-8000-000000000099",
                oidcResult.AccessToken),
            cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            approvalBeforeSwitchResponse.StatusCode,
            "Stale OIDC host token must not reach approval consumer path after context switch.");
        using var switchToHostResponse = await client.SendAsync(
            AuthorizedJson(HttpMethod.Put, "/api/v1/tenancy/context", switched.AccessToken, new ChangeTenantContextRequest(null)), cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(
            switchToHostResponse,
            HttpStatusCode.OK,
            "OIDC host context switch round-trip",
            logFilePath,
            cancellationToken).ConfigureAwait(false);
        var restored = await switchToHostResponse.Content.ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(restored);
        using var toolsRestoredResponse = await client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/ai/agent-tools", restored.AccessToken), cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(
            toolsRestoredResponse,
            HttpStatusCode.OK,
            "OIDC host token reaches AI tool catalog after round-trip",
            logFilePath,
            cancellationToken).ConfigureAwait(false);
        using var approvalRestoredResponse = await client.SendAsync(
            Authorized(
                HttpMethod.Get,
                "/api/v1/ai/agent/approvals/01981f2a-1200-7000-8000-000000000099",
                restored.AccessToken),
            cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(
            approvalRestoredResponse,
            HttpStatusCode.NotFound,
            "OIDC host token reaches approval consumer path after round-trip",
            logFilePath,
            cancellationToken).ConfigureAwait(false);
        using var toolsStaleTenantResponse = await client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/ai/agent-tools", switched.AccessToken), cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            toolsStaleTenantResponse.StatusCode,
            "Stale OIDC tenant token must not reach AI tool catalog after host round-trip.");
        using var approvalStaleTenantResponse = await client.SendAsync(
            Authorized(
                HttpMethod.Get,
                "/api/v1/ai/agent/approvals/01981f2a-1200-7000-8000-000000000099",
                switched.AccessToken),
            cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            approvalStaleTenantResponse.StatusCode,
            "Stale OIDC tenant token must not reach approval consumer path after host round-trip.");
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

    private static async Task VerifyOidcContextSwitchRefreshRotationAsync(
        HttpClient client,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        var oidcResult = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialRedirectUri,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            "admin",
            NativeApiE2EAssertions.AdminPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var available = await (await client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/tenancy/available", oidcResult.AccessToken),
            cancellationToken).ConfigureAwait(false)).Content.ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken).ConfigureAwait(false);
        var tenant = available!.Single(t => t.Identifier == "local");
        using var switchResponse = await client.SendAsync(
            AuthorizedJson(HttpMethod.Put, "/api/v1/tenancy/context", oidcResult.AccessToken, new ChangeTenantContextRequest(tenant.Id)),
            cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(switchResponse, HttpStatusCode.OK, "OIDC context switch with refresh rotation", logFilePath, cancellationToken).ConfigureAwait(false);
        var switched = await switchResponse.Content.ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(switched);
        Assert.IsFalse(string.IsNullOrWhiteSpace(switched!.RefreshToken));
        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            oidcResult.RefreshToken!,
            IdentityOidcRelyingPartyFixture.ConfidentialClientId,
            IdentityOidcRelyingPartyFixture.ConfidentialClientSecret,
            cancellationToken).ConfigureAwait(false);
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
        StringAssert.Contains(refreshResult.RawBody, "invalid_grant");
    }

    private static Dictionary<string, string?> BuildOidcSettings(
        RSA? sharedSigningKey = null,
        string? sharedSigningKeyId = null,
        string? dataProtectionKeyRingPath = null,
        string? dataProtectionCertificatePath = null)
    {
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings);
        if (sharedSigningKey is not null && !string.IsNullOrWhiteSpace(sharedSigningKeyId))
        {
            // 仅关闭 OIDC 临时密钥；JWT 仍允许 Testing 临时密钥，避免 IdentityOptions 要求根级 SigningKeys。
            settings["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "false";
            settings["Identity:Oidc:EncryptionKeyBase64"] =
                IdentityOidcMultiInstanceTestSupport.SharedEncryptionKeyBase64;
            settings["Identity:Oidc:ActiveSigningKeyId"] = sharedSigningKeyId;
            settings["Identity:Oidc:SigningKeys:" + sharedSigningKeyId + ":PrivateKeyPem"] = sharedSigningKey.ExportRSAPrivateKeyPem();
            settings["Identity:Oidc:SigningKeys:" + sharedSigningKeyId + ":PublicKeyPem"] = sharedSigningKey.ExportRSAPublicKeyPem();
        }

        ApplyMultiInstanceDataProtection(settings, dataProtectionKeyRingPath, dataProtectionCertificatePath);
        return settings;
    }

    private static Dictionary<string, string?> ToNativeSettings(IReadOnlyDictionary<string, string?> settings) =>
        new Dictionary<string, string?>(settings, StringComparer.Ordinal);

    private static void ApplyMultiInstanceDataProtection(
        Dictionary<string, string?> settings,
        string? dataProtectionKeyRingPath,
        string? dataProtectionCertificatePath)
    {
        if (string.IsNullOrWhiteSpace(dataProtectionKeyRingPath)
            || string.IsNullOrWhiteSpace(dataProtectionCertificatePath))
        {
            return;
        }

        settings["DataProtection:ApplicationName"] = "Full.NET.MultiInstance";
        settings["DataProtection:KeyRingPath"] = dataProtectionKeyRingPath;
        settings["DataProtection:CertificatePath"] = dataProtectionCertificatePath;
        settings["DataProtection:CertificatePassword"] =
            IdentityOidcMultiInstanceTestSupport.DataProtectionPassword;
    }

    private static async Task<TenantContextSummary> GetDevelopmentTenantForContextSwitchAsync(
        HttpClient client,
        string accessToken,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/tenancy/available", accessToken),
            cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(response, HttpStatusCode.OK, "List available tenants with OIDC token", logFilePath, cancellationToken).ConfigureAwait(false);
        var available = await response.Content.ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(available);
        var developmentTenant = available.SingleOrDefault(tenant => tenant.Identifier == "local");
        Assert.IsNotNull(
            developmentTenant,
            "Available tenants did not contain Development seed 'local': "
                + string.Join(", ", available.Select(tenant => $"{tenant.Identifier} ({tenant.Id})")));
        return developmentTenant;
    }

    private static async Task AssertMeAcceptsTokenAsync(
        HttpClient client,
        string accessToken,
        string scenario,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/me", accessToken), cancellationToken).ConfigureAwait(false);
        await AssertStatusAsync(
            response,
            HttpStatusCode.OK,
            "Access token must remain valid on peer native instance for " + scenario,
            logFilePath,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task AssertMeRejectsTokenAsync(
        HttpClient client,
        string accessToken,
        string scenario,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/me", accessToken), cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            response.StatusCode,
            "Stale OIDC access token must be rejected on peer native instance for " + scenario + ".");
    }

    private static async Task AssertDisabledClientRejectsTokensOnNativeInstanceAsync(
        HttpClient client,
        string authorizeUrl,
        IdentityOidcAuthorizationResult flow,
        string tenantAccessToken,
        string clientId,
        string instanceLabel,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        using var authorizeResponse = await client.GetAsync(authorizeUrl, cancellationToken).ConfigureAwait(false);
        Assert.IsTrue(authorizeResponse.Headers.Location is not null);
        StringAssert.Contains(
            authorizeResponse.Headers.Location!.ToString(),
            "error=unauthorized_client");

        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            clientId,
            null,
            cancellationToken).ConfigureAwait(false);
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
        Assert.IsTrue(
            refreshResult.RawBody.Contains("unauthorized_client", StringComparison.OrdinalIgnoreCase)
                || refreshResult.RawBody.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase),
            $"Expected disabled client refresh to fail on {instanceLabel} native instance, got: {refreshResult.RawBody}");

        await AssertMeRejectsTokenAsync(
            client,
            flow.AccessToken,
            $"stale OIDC host token after disable on {instanceLabel} native instance",
            logFilePath,
            cancellationToken).ConfigureAwait(false);
        await AssertMeRejectsTokenAsync(
            client,
            tenantAccessToken,
            $"OIDC tenant token after disable on {instanceLabel} native instance",
            logFilePath,
            cancellationToken).ConfigureAwait(false);
    }

    private static string BuildGovernanceAuthorizeUrl(string clientId)
    {
        var (_, challenge) = IdentityOidcRelyingPartyFixture.CreatePkcePair();
        return "/connect/authorize"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(ContextSwitchGovernanceRedirectUri)}"
            + "&response_type=code&scope=openid%20profile%20offline_access"
            + "&state=state&nonce=nonce"
            + $"&code_challenge={Uri.EscapeDataString(challenge)}"
            + "&code_challenge_method=S256";
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