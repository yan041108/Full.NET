using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>共享数据库、真实 OpenIddict RP 和 Cookie-only 业务访问的双库回归。</summary>
[TestClass]
public sealed class IdentityBrowserSessionIntegrationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Browser_flow_keeps_tokens_on_server_and_preserves_cross_node_authority(DatabaseProvider provider)
    {
        var settings = IdentityBrowserTestSupport.Settings();
        var connection = await IdentityBrowserTestSupport.DatabaseAsync(provider);
        var transport = new IdentityBrowserTestTransport();
        using var factory = new FullNetApiFactory(provider, connection, settings,
            configureTestServices: services => services.AddSingleton<IHttpMessageHandlerBuilderFilter>(transport));
        transport.Factory = factory; await factory.InitializeAsync();
        using var browser = IdentityBrowserTestSupport.Browser(factory);
        var callback = await IdentityBrowserTestSupport.BeginBrowserLoginAsync(browser);
        string cookie;
        using (var completed = await browser.GetAsync(callback))
        {
            Assert.AreEqual(HttpStatusCode.Redirect, completed.StatusCode);
            Assert.IsFalse((await completed.Content.ReadAsStringAsync()).Contains("access_token", StringComparison.Ordinal));
            cookie = completed.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(
                IdentityBrowserSession.CookieName + "=", StringComparison.Ordinal)).Split(';')[0];
        }
        using (var replay = await browser.GetAsync(callback)) Assert.AreEqual(HttpStatusCode.BadRequest, replay.StatusCode);
        using var stateResponse = await browser.GetAsync("/api/v1/identity/browser/session");
        Assert.AreEqual(HttpStatusCode.OK, stateResponse.StatusCode);
        using var json = JsonDocument.Parse(await stateResponse.Content.ReadAsStringAsync());
        Assert.AreEqual(2, json.RootElement.EnumerateObject().Count());
        var csrf = json.RootElement.GetProperty("csrfToken").GetString()!;
        using (var user = await browser.GetAsync("/api/v1/me")) Assert.AreEqual(HttpStatusCode.OK, user.StatusCode);
        using (var missingCsrf = await browser.PutAsJsonAsync("/api/v1/tenancy/context", new { tenantId = (Guid?)null }))
            Assert.AreEqual(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
        using (var wrongOrigin = new HttpRequestMessage(HttpMethod.Get, "/api/v1/identity/browser/session"))
        {
            wrongOrigin.Headers.Add("Origin", "https://sibling.localhost");
            using var denied = await browser.SendAsync(wrongOrigin); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using (var mutation = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context"))
        {
            mutation.Headers.Add("Origin", "https://localhost"); mutation.Headers.Add("X-CSRF-Token", csrf);
            mutation.Content = JsonContent.Create(new { tenantId = (Guid?)null });
            using var changed = await browser.SendAsync(mutation);
            Assert.AreEqual(HttpStatusCode.NoContent, changed.StatusCode);
            Assert.AreEqual(string.Empty, await changed.Content.ReadAsStringAsync());
        }
        // 切换后的新 refresh 必须真正可兑换；只检查 204/访问令牌会漏掉手动签发的 issuer 缺失。
        await ExpireAccessScheduleAsync(factory);
        using (var refreshedAfterContext = await browser.GetAsync("/api/v1/identity/browser/session"))
            Assert.AreEqual(HttpStatusCode.OK, refreshedAfterContext.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            // 服务器上也只保存不可逆句柄；此处从 TestServer 业务请求读取 Cookie，由 HTTP 层继续证明。
            var count = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<long>(
                new SqlStatement("test.browser_count", "SELECT COUNT(*) FROM fn_identity_browser_session", SqlDataScope.HostOnly));
            Assert.AreEqual(1L, count);
        }
        // Cookie 由真实响应提取用于另一节点；不解密、搬运 OAuth 凭据到浏览器。
        using var nodeB = new FullNetApiFactory(provider, connection, settings); await nodeB.InitializeAsync();
        using var otherNode = IdentityBrowserTestSupport.Browser(nodeB);
        otherNode.DefaultRequestHeaders.Add("Cookie", cookie);
        using var otherState = await otherNode.GetAsync("/api/v1/me"); Assert.AreEqual(HttpStatusCode.OK, otherState.StatusCode);
        using var anonymous = IdentityBrowserTestSupport.Browser(factory);
        using var legacy = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = FullNetApiFactory.TestPassword });
        Assert.AreEqual(HttpStatusCode.NotFound, legacy.StatusCode);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Refresh_is_serialized_and_an_expired_or_uncertain_lease_never_replays_credentials(DatabaseProvider provider)
    {
        var settings = IdentityBrowserTestSupport.Settings(); var transport = new IdentityBrowserTestTransport();
        using var factory = new FullNetApiFactory(provider, await IdentityBrowserTestSupport.DatabaseAsync(provider), settings,
            configureTestServices: services => services.AddSingleton<IHttpMessageHandlerBuilderFilter>(transport));
        transport.Factory = factory; await factory.InitializeAsync(); using var browser = IdentityBrowserTestSupport.Browser(factory);
        await IdentityBrowserTestSupport.LoginBrowserAsync(browser);
        await ExpireAccessScheduleAsync(factory);
        var before = transport.TokenRequests;
        var responses = await Task.WhenAll(browser.GetAsync("/api/v1/identity/browser/session"), browser.GetAsync("/api/v1/identity/browser/session"));
        foreach (var response in responses) { Assert.AreEqual(HttpStatusCode.OK, response.StatusCode); response.Dispose(); }
        Assert.AreEqual(before + 1, transport.TokenRequests, "并发恢复只能兑换一次刷新令牌。");
        await ExpireAccessScheduleAsync(factory);
        before = transport.TokenRequests;
        transport.Transform = (request, response) =>
        {
            if (request.RequestUri!.AbsolutePath == "/connect/token")
            { response.Dispose(); throw new HttpRequestException("Simulated response loss after real redemption."); }
            return Task.FromResult(response);
        };
        using (var unavailable = await browser.GetAsync("/api/v1/identity/browser/session")) Assert.IsFalse(unavailable.IsSuccessStatusCode);
        var afterLoss = transport.TokenRequests;
        Assert.AreEqual(before + 1, afterLoss, "全局 HTTP 弹性策略也不得重放已消费的一次性凭据。");
        using (var denied = await browser.GetAsync("/api/v1/identity/browser/session")) Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.AreEqual(afterLoss, transport.TokenRequests, "未知结果后不得自动复用旧 refresh。");
        transport.Transform = null;
        // 新登录后模拟 Worker/节点在刷新租约内中断；过期租约必须使票据失效。
        await IdentityBrowserTestSupport.LoginBrowserAsync(browser);
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("test.expire_browser_lease", """
            UPDATE fn_identity_browser_session SET RefreshLeaseId = @Lease, RefreshLeaseUntilUtc = @Before
            """, SqlDataScope.HostOnly), IdentitySqlParameters.Create(("Lease", Guid.CreateVersion7()), ("Before", DateTimeOffset.UtcNow.AddMinutes(-1))));
        before = transport.TokenRequests;
        using var expired = await browser.GetAsync("/api/v1/identity/browser/session"); Assert.AreEqual(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.AreEqual(before, transport.TokenRequests);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)]
    [DataRow(DatabaseProvider.MySql, false)]
    [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, true)]
    public async Task Context_ticket_save_failure_discards_old_refresh_instead_of_releasing_it_for_replay(DatabaseProvider provider, bool failDelete)
    {
        var transport = new IdentityBrowserTestTransport();
        var rejectSave = false;
        var rejectDelete = failDelete;
        using var factory = new FullNetApiFactory(provider, await IdentityBrowserTestSupport.DatabaseAsync(provider), IdentityBrowserTestSupport.Settings(),
            configureTestServices: services =>
            {
                services.AddSingleton<IHttpMessageHandlerBuilderFilter>(transport);
                var original = services.Last(item => item.ServiceType == typeof(ICommandExecutor));
                services.Replace(ServiceDescriptor.Scoped<ICommandExecutor>(sp =>
                    new RejectTicketSave((ICommandExecutor)original.ImplementationFactory!(sp), () => rejectSave, () => rejectDelete)));
            });
        transport.Factory = factory; await factory.InitializeAsync();
        using var browser = IdentityBrowserTestSupport.Browser(factory);
        await IdentityBrowserTestSupport.LoginBrowserAsync(browser);
        using var response = await browser.GetAsync("/api/v1/identity/browser/session");
        using var state = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        rejectSave = true;
        using var change = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context");
        change.Headers.Add("Origin", "https://localhost"); change.Headers.Add("X-CSRF-Token", state.RootElement.GetProperty("csrfToken").GetString());
        change.Content = JsonContent.Create(new { tenantId = (Guid?)null });
        using var failed = await browser.SendAsync(change);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        var before = transport.TokenRequests;
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        if (failDelete)
        {
            // 清理失败仍保留不可接管的租约；模拟进程已退出并让租约到期，再恢复清理数据库。
            Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<int>(
                new SqlStatement("test.failed_delete_preserves_lease", "SELECT COUNT(*) FROM fn_identity_browser_session WHERE RefreshLeaseId IS NOT NULL", SqlDataScope.HostOnly)));
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("test.expire_failed_context_lease",
                "UPDATE fn_identity_browser_session SET RefreshLeaseUntilUtc = @Before WHERE RefreshLeaseId IS NOT NULL", SqlDataScope.HostOnly),
                IdentitySqlParameters.Create(("Before", DateTimeOffset.UtcNow.AddMinutes(-1))));
            rejectDelete = false;
        }
        using var denied = await browser.GetAsync("/api/v1/identity/browser/session");
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.AreEqual(before, transport.TokenRequests, "票据保存失败后不能再次兑换旧 refresh。");
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<int>(
            new SqlStatement("test.no_browser_ticket_after_context_failure", "SELECT COUNT(*) FROM fn_identity_browser_session", SqlDataScope.HostOnly)));
    }

    private sealed class RejectTicketSave(ICommandExecutor inner, Func<bool> reject, Func<bool> rejectDelete) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            statement.Name == IdentityBrowserSessionSql.Replace.Name && reject()
                || statement.Name == IdentityBrowserSessionSql.Delete.Name && reject() && rejectDelete()
                ? throw new HttpRequestException("Simulated ticket storage failure after context rotation.")
                : inner.ExecuteAsync(statement, parameters, cancellationToken);
    }

    private static async Task ExpireAccessScheduleAsync(FullNetApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var row = (await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QueryAsync<IdentityBrowserSessionRow>(
            new SqlStatement("test.browser_ticket", "SELECT Id, HandleHash, Ciphertext, ExpiresAtUtc, Version, RefreshLeaseId, RefreshLeaseUntilUtc FROM fn_identity_browser_session", SqlDataScope.HostOnly))).Single();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Full.NET.Identity.BrowserTicket.v1");
        var ticket = JsonSerializer.Deserialize(protector.Unprotect(row.Ciphertext), IdentityJsonSerializerContext.Default.IdentityBrowserTicket)!;
        var ciphertext = protector.Protect(JsonSerializer.Serialize(ticket with { AccessExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1) }, IdentityJsonSerializerContext.Default.IdentityBrowserTicket));
        await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("test.browser_access_schedule",
            "UPDATE fn_identity_browser_session SET Ciphertext = @Ciphertext WHERE Id = @Id", SqlDataScope.HostOnly),
            IdentitySqlParameters.Create(("Id", row.Id), ("Ciphertext", ciphertext)));
    }
}
