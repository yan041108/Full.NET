using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageMfaRecoveryCodes;
using Full.NET.Modules.Identity.Features.ManageOidcAuthorizations;
using Full.NET.Modules.Identity.Features.ManageTotp;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>真实表单、防伪与双库权威状态验证 consent、MFA 和强制改密闭环。</summary>
[TestClass]
public sealed class IdentityOidcInteractiveIntegrationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Third_party_consent_requires_explicit_acceptance_and_revocation_blocks_reuse(DatabaseProvider provider)
    {
        using var factory = new FullNetApiFactory(provider, await IdentityBrowserTestSupport.DatabaseAsync(provider), IdentityOidcProtocolAssertions.Settings);
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        await applications.CreateAsync(IdentityOidcClientDescriptorFactory.Build(new("third-party", null, "Third Party",
            [IdentityOidcRelyingPartyFixture.PublicRedirectUri], [], ["openid", "profile"], false, null)));
        using var browser = IdentityBrowserTestSupport.Browser(factory);
        var (url, verifier) = AuthorizeUrl("third-party");
        using var first = await browser.GetAsync(url); Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        var fields = IdentityBrowserTestSupport.Hidden(await first.Content.ReadAsStringAsync());
        fields["username"] = "admin"; fields["password"] = FullNetApiFactory.TestPassword;
        using var withoutAcceptance = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.OK, withoutAcceptance.StatusCode); Assert.IsNull(withoutAcceptance.Headers.Location);
        var consentFields = IdentityBrowserTestSupport.Hidden(await withoutAcceptance.Content.ReadAsStringAsync());
        consentFields["consent"] = "deny";
        using (var denied = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(consentFields)))
            StringAssert.Contains(denied.Headers.Location!.Query, "error=access_denied");
        using (var silent = await browser.GetAsync(url + "&prompt=none")) StringAssert.Contains(silent.Headers.Location!.Query, "error=consent_required");
        consentFields["consent"] = "accept";
        var forged = new Dictionary<string, string>(consentFields); forged.Remove("__RequestVerificationToken");
        using (var invalid = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(forged))) Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var accepted = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(consentFields));
        var code = QueryHelpers.ParseQuery(accepted.Headers.Location!.Query)["code"].ToString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(code));
        var exchange = await IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(browser, code, verifier,
            "third-party", IdentityOidcRelyingPartyFixture.PublicRedirectUri, null, expectedNonce: "interactive-nonce");
        Assert.IsFalse(string.IsNullOrEmpty(exchange.AccessToken));
        using (var silent = await browser.GetAsync(url + "&prompt=none")) StringAssert.Contains(silent.Headers.Location!.Query, "code=");
        var subject = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(exchange.IdToken!, "sub")!;
        var application = await applications.FindByClientIdAsync("third-party");
        var grants = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        string? grantId = null;
        await foreach (var grant in grants.FindAsync(subject, await applications.GetIdAsync(application!),
            OpenIddictConstants.Statuses.Valid, OpenIddictConstants.AuthorizationTypes.Permanent, ["openid", "profile"]))
        { grantId = await grants.GetIdAsync(grant); break; }
        Assert.IsNotNull(grantId);
        var anotherConsent = new OpenIddictAuthorizationDescriptor
        { ApplicationId = await applications.GetIdAsync(application!), Subject = subject,
            Status = OpenIddictConstants.Statuses.Valid, Type = OpenIddictConstants.AuthorizationTypes.Permanent };
        anotherConsent.Scopes.Add("openid"); await grants.CreateAsync(anotherConsent);
        var management = scope.ServiceProvider.GetRequiredService<OidcAuthorizationManagementService>();
        Assert.IsFalse((await management.RevokeAsync(Guid.Parse(grantId), new(Guid.CreateVersion7(), null, null), ownOnly: true)).IsSuccess);
        var revoked = await scope.ServiceProvider.GetRequiredService<OidcAuthorizationManagementService>().RevokeAsync(Guid.Parse(grantId),
            new OidcManagementActorContext(Guid.Parse(subject), null, "consent-integration"), ownOnly: true);
        Assert.IsTrue(revoked.IsSuccess);
        await foreach (var unexpected in grants.FindAsync(subject, await applications.GetIdAsync(application!), OpenIddictConstants.Statuses.Valid,
            OpenIddictConstants.AuthorizationTypes.Permanent, default)) Assert.Fail("撤回后不得留下该应用其他 scope 的有效永久授权。");
        using (var silent = await browser.GetAsync(url + "&prompt=none")) StringAssert.Contains(silent.Headers.Location!.Query, "error=consent_required");
        using var resource = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        resource.Headers.Authorization = new AuthenticationHeaderValue("Bearer", exchange.AccessToken);
        using var invalidated = await browser.SendAsync(resource); Assert.AreEqual(HttpStatusCode.Unauthorized, invalidated.StatusCode);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Mfa_is_not_inferred_from_password_cookie_and_totp_cannot_be_replayed(DatabaseProvider provider)
    {
        using var factory = new FullNetApiFactory(provider, await IdentityBrowserTestSupport.DatabaseAsync(provider), IdentityOidcProtocolAssertions.Settings);
        await factory.InitializeAsync(); using var passwordBrowser = IdentityBrowserTestSupport.Browser(factory);
        var old = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(passwordBrowser,
            IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri, null,
            "admin", FullNetApiFactory.TestPassword, true);
        var userId = Guid.Parse(IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(old.IdToken!, "sub")!);
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString("D"))], "fixture"));
        var enrollment = scope.ServiceProvider.GetRequiredService<TotpEnrollmentService>();
        var begun = await enrollment.BeginAsync(principal); Assert.IsTrue(begun.IsSuccess);
        var secret = begun.Value!.SharedSecretBase32;
        var otp = TotpAlgorithm.ComputeCode(TotpAlgorithm.DecodeSharedSecret(secret), DateTimeOffset.UtcNow);
        Assert.IsTrue((await enrollment.ConfirmAsync(principal, otp)).IsSuccess);
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await manager.FindByClientIdAsync(IdentityOidcRelyingPartyFixture.PublicClientId);
        var descriptor = new OpenIddictApplicationDescriptor(); await manager.PopulateAsync(descriptor, application!);
        IdentityOidcClientMetadata.Write(descriptor, true, null, false, true); await manager.UpdateAsync(application!, descriptor);
        var (url, verifier) = AuthorizeUrl(IdentityOidcRelyingPartyFixture.PublicClientId);
        using (var silent = await passwordBrowser.GetAsync(url + "&prompt=none")) StringAssert.Contains(silent.Headers.Location!.Query, "error=login_required");
        using var browser = IdentityBrowserTestSupport.Browser(factory);
        using var page = await browser.GetAsync(url + "&ui_locales=fr%20en-GB");
        var html = await page.Content.ReadAsStringAsync(); StringAssert.Contains(html, "lang=\"en-US\"");
        var fields = IdentityBrowserTestSupport.Hidden(html); fields["username"] = "admin"; fields["password"] = FullNetApiFactory.TestPassword;
        using (var missing = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields))) Assert.AreEqual(HttpStatusCode.Unauthorized, missing.StatusCode);
        fields["second_factor"] = otp;
        using var success = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields)); Assert.AreEqual(HttpStatusCode.Redirect, success.StatusCode);
        var code = QueryHelpers.ParseQuery(success.Headers.Location!.Query)["code"].ToString();
        var exchange = await IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(browser, code, verifier,
            IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri, null);
        Assert.AreEqual("urn:fullnet:acr:mfa", IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(exchange.IdToken!, "acr"));
        Assert.IsTrue(new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(exchange.IdToken!).Claims.Any(claim => claim.Type == "amr" && claim.Value == "otp"));
        using var replayBrowser = IdentityBrowserTestSupport.Browser(factory);
        using var replayPage = await replayBrowser.GetAsync(url);
        var replay = IdentityBrowserTestSupport.Hidden(await replayPage.Content.ReadAsStringAsync());
        replay["username"] = "admin"; replay["password"] = FullNetApiFactory.TestPassword; replay["second_factor"] = otp;
        using var repeated = await replayBrowser.PostAsync("/connect/authorize", new FormUrlEncodedContent(replay));
        Assert.AreEqual(HttpStatusCode.Unauthorized, repeated.StatusCode);
        var recovery = await scope.ServiceProvider.GetRequiredService<MfaRecoveryCodeService>().RegenerateAsync(userId);
        Assert.IsTrue(recovery.IsSuccess);
        using var recoveryBrowser = IdentityBrowserTestSupport.Browser(factory);
        using var recoveryPage = await recoveryBrowser.GetAsync(url);
        var recoveryFields = IdentityBrowserTestSupport.Hidden(await recoveryPage.Content.ReadAsStringAsync());
        recoveryFields["username"] = "admin"; recoveryFields["password"] = FullNetApiFactory.TestPassword;
        recoveryFields["second_factor"] = recovery.Value!.RecoveryCodes[1];
        using var recoveryLogin = await recoveryBrowser.PostAsync("/connect/authorize", new FormUrlEncodedContent(recoveryFields));
        Assert.AreEqual(HttpStatusCode.Redirect, recoveryLogin.StatusCode);
        var recoveryExchange = await IdentityOidcRelyingPartyFixture.ExchangeAuthorizationCodeAsync(recoveryBrowser,
            QueryHelpers.ParseQuery(recoveryLogin.Headers.Location!.Query)["code"].ToString(), verifier,
            IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri, null);
        Assert.IsTrue(new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(recoveryExchange.IdToken!).Claims.Any(claim => claim.Type == "amr" && claim.Value == "urn:fullnet:amr:recovery-code"));
        Assert.AreEqual("urn:fullnet:acr:mfa", IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(recoveryExchange.IdToken!, "acr"));
        var mfaEvents = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<int>(new SqlStatement(
            "test.mfa_authentication_events", "SELECT COUNT(1) FROM fn_identity_auth_audit WHERE UserId = @UserId AND EventType = 'oidc.center_login' AND AuthenticationMethod = 'mfa' AND Succeeded = 1", SqlDataScope.HostOnly),
            IdentitySqlParameters.Create(("UserId", userId)));
        Assert.AreEqual(2, mfaEvents);
        var recovered = await scope.ServiceProvider.GetRequiredService<IdentityOidcAccountService>().RecoverAsync("admin",
            recovery.Value!.RecoveryCodes[0], "Recovered!2026Identity", default);
        Assert.IsTrue(recovered.IsSuccess);
        Assert.IsFalse((await scope.ServiceProvider.GetRequiredService<IdentityOidcAccountService>().RecoverAsync("admin",
            recovery.Value.RecoveryCodes[0], "Another!2026Password", default)).IsSuccess);
        using var stale = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        stale.Headers.Authorization = new AuthenticationHeaderValue("Bearer", exchange.AccessToken);
        using var rejected = await browser.SendAsync(stale); Assert.AreEqual(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Must_change_password_does_not_issue_a_center_cookie_or_authorization_code(DatabaseProvider provider)
    {
        using var factory = new FullNetApiFactory(provider, await IdentityBrowserTestSupport.DatabaseAsync(provider), IdentityOidcProtocolAssertions.Settings);
        await factory.InitializeAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("test.require_password_change",
                "UPDATE fn_identity_user SET MustChangePassword = 1 WHERE NormalizedUsername = 'ADMIN' AND ScopeKey = 'host'", SqlDataScope.HostOnly));
        }
        using var browser = IdentityBrowserTestSupport.Browser(factory); var (url, _) = AuthorizeUrl(IdentityOidcRelyingPartyFixture.PublicClientId);
        using var page = await browser.GetAsync(url); var fields = IdentityBrowserTestSupport.Hidden(await page.Content.ReadAsStringAsync());
        fields["username"] = "admin"; fields["password"] = FullNetApiFactory.TestPassword;
        using var required = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.Unauthorized, required.StatusCode); Assert.IsNull(required.Headers.Location);
        var html = await required.Content.ReadAsStringAsync(); StringAssert.Contains(html, "name=\"new_password\"");
        Assert.IsFalse(required.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(value => value.StartsWith(IdentityOidcCenterAuthenticationDefaults.CookieName + "=", StringComparison.Ordinal)));
        fields = IdentityBrowserTestSupport.Hidden(html); fields["username"] = "admin";
        fields["password"] = FullNetApiFactory.TestPassword; fields["new_password"] = "Changed!2026Password";
        using var changed = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode); Assert.IsNull(changed.Headers.Location);
        fields = IdentityBrowserTestSupport.Hidden(await changed.Content.ReadAsStringAsync());
        fields["username"] = "admin"; fields["password"] = "Changed!2026Password";
        using var signedIn = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.Redirect, signedIn.StatusCode);
    }

    private static (string Url, string Verifier) AuthorizeUrl(string client)
    {
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        return ("/connect/authorize?client_id=" + client + "&redirect_uri=" + Uri.EscapeDataString(IdentityOidcRelyingPartyFixture.PublicRedirectUri)
            + "&response_type=code&scope=openid%20profile&state=interactive-state&nonce=interactive-nonce&code_challenge_method=S256&code_challenge=" + challenge, verifier);
    }
}
