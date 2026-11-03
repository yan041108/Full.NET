using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>双库验证标准退出的防伪、精确回调和浏览器会话隔离。</summary>
[TestClass]
public sealed class IdentityOidcEndSessionTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Confirmed_logout_revokes_current_browser_but_preserves_other_device(DatabaseProvider provider)
    {
        const string callback = "https://localhost:5001/signed-out";
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:Oidc:Clients:0:PostLogoutRedirectUris:0"] = callback
        };
        using var factory = new FullNetApiFactory(provider, connection, settings);
        await factory.InitializeAsync();
        using var browser = factory.CreateClientForHost("localhost");
        using var otherDevice = factory.CreateClientForHost("localhost");
        // 中心 Cookie 始终 Secure，浏览器夹具也必须通过 HTTPS 往返，不能绕过 Cookie 安全属性。
        browser.BaseAddress = new Uri("https://localhost");
        otherDevice.BaseAddress = new Uri("https://localhost");
        async Task<IdentityOidcAuthorizationResult> Login(HttpClient client) =>
            await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(client,
                IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri,
                null, "admin", FullNetApiFactory.TestPassword, requestOfflineAccess: true);
        var current = await Login(browser);
        var other = await Login(otherDevice);
        Assert.IsFalse(string.IsNullOrWhiteSpace(current.IdToken));
        var currentSid = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(current.IdToken!, "sid");
        Assert.IsTrue(Guid.TryParse(currentSid, out _));
        Assert.AreNotEqual(currentSid, IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(other.IdToken!, "sid"));
        using (var discovery = await browser.GetAsync("/.well-known/openid-configuration"))
        {
            using var json = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync());
            StringAssert.EndsWith(json.RootElement.GetProperty("end_session_endpoint").GetString()!, "/connect/logout");
        }

        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = IdentityOidcRelyingPartyFixture.PublicClientId,
            ["id_token_hint"] = current.IdToken!, ["post_logout_redirect_uri"] = callback,
            ["state"] = "logout-state"
        };
        string Query() => string.Join('&', parameters.Select(item => Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(item.Value)));
        using (var invalidHint = await browser.GetAsync("/connect/logout?id_token_hint=invalid"))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalidHint.StatusCode);
            Assert.IsNull(invalidHint.Headers.Location);
        }
        using (var mismatchedClient = await browser.GetAsync("/connect/logout?client_id="
            + IdentityOidcRelyingPartyFixture.ConfidentialClientId + "&id_token_hint="
            + Uri.EscapeDataString(current.IdToken!)))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, mismatchedClient.StatusCode);
            Assert.IsNull(mismatchedClient.Headers.Location);
        }
        using (var invalidCallback = await browser.GetAsync("/connect/logout?client_id="
            + IdentityOidcRelyingPartyFixture.PublicClientId + "&post_logout_redirect_uri="
            + Uri.EscapeDataString("https://untrusted.example/signed-out")))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalidCallback.StatusCode);
            Assert.IsNull(invalidCallback.Headers.Location);
        }
        using var confirmation = await browser.GetAsync("/connect/logout?" + Query());
        Assert.AreEqual(HttpStatusCode.OK, confirmation.StatusCode);
        Assert.IsTrue(confirmation.Headers.CacheControl!.NoStore);
        await AssertAccessAsync(browser, current.AccessToken, HttpStatusCode.OK);
        var html = await confirmation.Content.ReadAsStringAsync();
        const string prefix = "name=\"__RequestVerificationToken\" value=\"";
        var start = html.IndexOf(prefix, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0);
        start += prefix.Length;
        var antiforgery = WebUtility.HtmlDecode(html[start..html.IndexOf('"', start)]);
        using (var initialPost = await browser.PostAsync("/connect/logout", new FormUrlEncodedContent(parameters)))
            Assert.AreEqual(HttpStatusCode.OK, initialPost.StatusCode);
        parameters["confirm"] = "yes";
        using (var forged = await browser.PostAsync("/connect/logout", new FormUrlEncodedContent(parameters)))
            Assert.AreEqual(HttpStatusCode.BadRequest, forged.StatusCode);
        await AssertAccessAsync(browser, current.AccessToken, HttpStatusCode.OK);
        parameters["__RequestVerificationToken"] = antiforgery;
        using (var logout = await browser.PostAsync("/connect/logout", new FormUrlEncodedContent(parameters)))
        {
            Assert.AreEqual(HttpStatusCode.Redirect, logout.StatusCode);
            Assert.AreEqual(callback, logout.Headers.Location!.GetLeftPart(UriPartial.Path));
            StringAssert.Contains(logout.Headers.Location.Query, "state=logout-state");
        }
        await AssertAccessAsync(browser, current.AccessToken, HttpStatusCode.Unauthorized);
        using (var refresh = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["client_id"] = IdentityOidcRelyingPartyFixture.PublicClientId,
            ["refresh_token"] = current.RefreshToken!
        })))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, refresh.StatusCode);
            StringAssert.Contains(await refresh.Content.ReadAsStringAsync(), "invalid_grant");
        }
        await AssertAccessAsync(otherDevice, other.AccessToken, HttpStatusCode.OK);
        using var otherRefresh = await otherDevice.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["client_id"] = IdentityOidcRelyingPartyFixture.PublicClientId,
            ["refresh_token"] = other.RefreshToken!
        }));
        Assert.AreEqual(HttpStatusCode.OK, otherRefresh.StatusCode);
        await AssertMonotonicRevocationAsync(factory,
            Guid.Parse(IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(current.IdToken!, "sub")!));
    }

    private static async Task AssertMonotonicRevocationAsync(FullNetApiFactory factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var queries = services.GetRequiredService<IQueryExecutor>();
        var commands = services.GetRequiredService<ICommandExecutor>();
        var clock = services.GetRequiredService<IClock>();
        var session = await services.GetRequiredService<IdentityOidcSessionService>()
            .CreateCenterSessionAsync(userId, "test-stamp", clock.UtcNow.AddHours(1));
        var service = new IdentityOidcSessionService(new RenewAfterRead(queries, commands, clock), commands, clock);
        // 在读取版本之后插入真实数据库续期，重现旧 CAS 会丢弃撤销的窗口。
        var revoked = await services.GetRequiredService<ICommandTransaction>().ExecuteAsync(
            token => service.RevokeCenterSessionAsync(session.Id, token), default);
        Assert.IsTrue(revoked, "并发版本递增不得丢弃退出撤销。");
        Assert.IsNull(await services.GetRequiredService<IdentityOidcSessionService>().FindActiveCenterSessionAsync(session.Id));
    }

    private sealed class RenewAfterRead(IQueryExecutor inner, ICommandExecutor commands, IClock clock) : IQueryExecutor
    {
        public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var row = await inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
            if (row is IdentityOidcCenterSessionRow center && statement == IdentityOidcSessionSql.FindCenterSessionById)
                await commands.ExecuteAsync(IdentityOidcSessionSql.UpdateCenterSession,
                    IdentitySqlParameters.Create(("Id", center.Id), ("SecurityStamp", center.SecurityStamp),
                        ("ExpiresAtUtc", clock.UtcNow.AddHours(2)), ("RevokedAtUtc", null),
                        ("UpdatedAtUtc", clock.UtcNow), ("Version", center.Version)), cancellationToken);
            return row;
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default) => inner.QueryAsync<T>(statement, parameters, cancellationToken);
    }

    private static async Task AssertAccessAsync(HttpClient browser, string token, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await browser.SendAsync(request);
        Assert.AreEqual(expected, response.StatusCode);
    }
}
