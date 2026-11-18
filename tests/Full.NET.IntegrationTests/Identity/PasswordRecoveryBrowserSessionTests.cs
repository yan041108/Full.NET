using System.Net;
using System.Net.Http.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>邮箱找回密码后，原 OIDC 中心和 BFF 会话均不能继续认证。</summary>
[TestClass]
public sealed class PasswordRecoveryBrowserSessionTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Password_recovery_revokes_original_browser_and_center_sessions(DatabaseProvider provider)
    {
        var root = Directory.CreateTempSubdirectory("fullnet-recovery-session-").FullName;
        try
        {
            var settings = IdentityBrowserTestSupport.Settings();
            settings["DataProtection:KeyRingPath"] = Path.Combine(root, "keys");
            var transport = new IdentityBrowserTestTransport();
            var delivery = new CapturingIdentityChallengeDeliveryPort();
            using var factory = new FullNetApiFactory(provider, await IdentityBrowserTestSupport.DatabaseAsync(provider), settings,
                configureTestServices: services =>
                {
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter>(transport);
                    services.AddSingleton<IIdentityChallengeDeliveryPort>(delivery);
                });
            transport.Factory = factory;
            await factory.InitializeAsync();
            var username = "recover-browser-" + Guid.NewGuid().ToString("N");
            var user = await factory.CreateHostIdentityAsync(username, [], password: FullNetApiFactory.TestPassword);
            var email = username + "@example.test";
            await factory.EnsureHostUserProfileEmailAsync(user.UserId, email, "Recovery browser account");
            var other = await factory.CreateHostIdentityAsync("recover-control-" + Guid.NewGuid().ToString("N"), [],
                password: FullNetApiFactory.TestPassword);
            using var browser = IdentityBrowserTestSupport.Browser(factory);
            using var control = IdentityBrowserTestSupport.Browser(factory);
            var authorize = await LoginAsync(browser, username, FullNetApiFactory.TestPassword);
            await LoginAsync(control, other.Username, FullNetApiFactory.TestPassword);
            using (var me = await browser.GetAsync("/api/v1/me")) Assert.AreEqual(HttpStatusCode.OK, me.StatusCode);
            using (var silent = await browser.GetAsync(QueryHelpers.AddQueryString(authorize, "prompt", "none")))
            {
                Assert.AreEqual(HttpStatusCode.Redirect, silent.StatusCode);
                Assert.IsTrue(QueryHelpers.ParseQuery(silent.Headers.Location!.Query).ContainsKey("code"));
            }

            using var recovery = factory.CreateClientForHost("localhost");
            using (var requested = await recovery.PostAsJsonAsync("/api/v1/auth/recover-password", new RequestPasswordRecoveryRequest(email)))
                Assert.AreEqual(HttpStatusCode.OK, requested.StatusCode);
            var intent = delivery.LastIntent;
            Assert.IsNotNull(intent);
            Assert.AreEqual(email, intent.NormalizedEmail);
            var confirmation = new ConfirmPasswordRecoveryRequest(intent.ChallengeId, intent.Credential, "FullNet!2026Recovered");
            using (var confirmed = await recovery.PostAsJsonAsync("/api/v1/auth/recover-password/confirm", confirmation))
                Assert.AreEqual(HttpStatusCode.NoContent, confirmed.StatusCode);
            using (var replay = await recovery.PostAsJsonAsync("/api/v1/auth/recover-password/confirm", confirmation))
                Assert.AreEqual(HttpStatusCode.BadRequest, replay.StatusCode);
            using (var stale = await browser.GetAsync("/api/v1/me")) Assert.AreEqual(HttpStatusCode.Unauthorized, stale.StatusCode);
            using (var unaffected = await control.GetAsync("/api/v1/me")) Assert.AreEqual(HttpStatusCode.OK, unaffected.StatusCode);
            using (var silent = await browser.GetAsync(QueryHelpers.AddQueryString(authorize, "prompt", "none")))
            {
                Assert.AreEqual(HttpStatusCode.Redirect, silent.StatusCode);
                Assert.AreEqual("login_required", QueryHelpers.ParseQuery(silent.Headers.Location!.Query)["error"].ToString());
            }
            using (var start = await browser.GetAsync("/api/v1/identity/browser/login"))
            using (var page = await browser.GetAsync(start.Headers.Location))
            {
                var fields = IdentityBrowserTestSupport.Hidden(await page.Content.ReadAsStringAsync());
                fields["username"] = username;
                fields["password"] = FullNetApiFactory.TestPassword;
                using var rejected = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields));
                Assert.AreEqual(HttpStatusCode.Unauthorized, rejected.StatusCode);
            }
            await LoginAsync(browser, username, "FullNet!2026Recovered");
            using var restored = await browser.GetAsync("/api/v1/me");
            Assert.AreEqual(HttpStatusCode.OK, restored.StatusCode);
            var current = await restored.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.AreEqual(user.UserId, current.GetProperty("id").GetGuid());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> LoginAsync(HttpClient browser, string username, string password)
    {
        using var start = await browser.GetAsync("/api/v1/identity/browser/login");
        Assert.AreEqual(HttpStatusCode.Redirect, start.StatusCode);
        var authorize = start.Headers.Location!.ToString();
        using var page = await browser.GetAsync(authorize);
        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
        var fields = IdentityBrowserTestSupport.Hidden(await page.Content.ReadAsStringAsync());
        fields["username"] = username;
        fields["password"] = password;
        using var authorized = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.Redirect, authorized.StatusCode);
        using var callback = await browser.GetAsync(authorized.Headers.Location);
        Assert.AreEqual(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.AreEqual("/#/identity/oidc/callback", callback.Headers.Location!.OriginalString);
        return authorize;
    }
}
