using System.Net;
using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>本地事务 Outbox、幂等落地、签名通知和重试状态均使用真实双库。</summary>
[TestClass]
public sealed class IdentityOidcBackchannelIntegrationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Center_revocation_commits_one_reliable_delivery_and_worker_retries_with_stable_jti(DatabaseProvider provider)
    {
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        { ["Identity:Oidc:BackchannelAllowedOrigins:0"] = "https://rp.example" };
        using var factory = new FullNetApiFactory(provider, await IdentityBrowserTestSupport.DatabaseAsync(provider), settings);
        await factory.InitializeAsync();
        await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        services.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
        var application = (await applications.FindByClientIdAsync(IdentityOidcRelyingPartyFixture.PublicClientId))!;
        var descriptor = new OpenIddictApplicationDescriptor(); await applications.PopulateAsync(descriptor, application);
        IdentityOidcClientMetadata.Write(descriptor, true, null, false, false, "https://rp.example/backchannel");
        await applications.UpdateAsync(application, descriptor);
        using var browser = IdentityBrowserTestSupport.Browser(factory);
        var exchange = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(browser,
            IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri, null,
            "admin", FullNetApiFactory.TestPassword, true);
        var center = Guid.Parse(IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(exchange.IdToken!, "sid")!);
        var sessions = services.GetRequiredService<IdentityOidcSessionService>(); var transaction = services.GetRequiredService<ICommandTransaction>();
        Assert.IsTrue(await transaction.ExecuteAsync(ct => sessions.RevokeCenterSessionAsync(center, ct), default));
        Assert.IsFalse(await transaction.ExecuteAsync(ct => sessions.RevokeCenterSessionAsync(center, ct), default));
        var queries = services.GetRequiredService<IQueryExecutor>(); var commands = services.GetRequiredService<ICommandExecutor>();
        var payloads = await queries.QueryAsync<byte[]>(new SqlStatement("test.logout_outbox", "SELECT Payload FROM fn_outbox_message WHERE MessageType = @Type", SqlDataScope.HostOnly),
            IdentitySqlParameters.Create(("Type", IdentityOidcLogoutRequested.EventType)));
        Assert.AreEqual(1, payloads.Count);
        var clock = new DeliveryClock(DateTimeOffset.UtcNow);
        var handler = new IdentityOidcLogoutRequestedHandler(services.GetRequiredService<IIntegrationEventSerializer>(), commands, clock,
            Options.Create(new DatabaseOptions { Provider = provider }));
        await handler.HandleAsync(payloads.Single(), default); await handler.HandleAsync(payloads.Single(), default);
        var rows = await queries.QueryAsync<IdentityOidcLogoutDeliveryRow>(provider == DatabaseProvider.MySql ? IdentityOidcLogoutSql.ListMySql : IdentityOidcLogoutSql.ListSqlServer);
        Assert.AreEqual(1, rows.Count);
        var receiver = new LogoutReceiver();
        var keys = services.GetRequiredService<IdentityOidcSigningKeyRing>();
        var delivery = new IdentityOidcLogoutDelivery(queries, commands, clock, receiver, keys,
            services.GetRequiredService<IOptions<IdentityOidcOptions>>(), Options.Create(new DatabaseOptions { Provider = provider }));
        Assert.AreEqual(0, await delivery.RunOnceAsync(default));
        var failed = (await queries.QueryAsync<IdentityOidcLogoutDeliveryRow>(provider == DatabaseProvider.MySql ? IdentityOidcLogoutSql.ListMySql : IdentityOidcLogoutSql.ListSqlServer)).Single();
        Assert.AreEqual("pending", failed.StatusKey); Assert.AreEqual(1, failed.AttemptCount); Assert.IsNull(failed.LeaseId);
        clock.Now = clock.Now.AddSeconds(5); receiver.Succeed = true;
        // 重建投递器代表进程重启；队列中的同一意图继续处理。
        delivery = new IdentityOidcLogoutDelivery(queries, commands, clock, receiver, keys,
            services.GetRequiredService<IOptions<IdentityOidcOptions>>(), Options.Create(new DatabaseOptions { Provider = provider }));
        Assert.AreEqual(1, await delivery.RunOnceAsync(default)); Assert.AreEqual(0, await delivery.RunOnceAsync(default));
        Assert.AreEqual(2, receiver.Tokens.Count);
        foreach (var token in receiver.Tokens)
        {
            var valid = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
            { ValidIssuer = settings["Identity:Oidc:Issuer"], ValidAudience = IdentityOidcRelyingPartyFixture.PublicClientId,
                IssuerSigningKeys = keys.ValidationKeys, ValidateIssuerSigningKey = true, ValidateIssuer = true,
                ValidateAudience = true, ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1), ValidTypes = ["logout+jwt"] });
            Assert.IsTrue(valid.IsValid, valid.Exception?.Message);
            var jwt = new JsonWebToken(token); Assert.AreEqual(center.ToString("D"), jwt.GetClaim("sid").Value);
            Assert.AreEqual(rows.Single().Id.ToString("D"), jwt.GetClaim("jti").Value);
            Assert.IsFalse(jwt.TryGetClaim("nonce", out _));
            using var events = JsonDocument.Parse(jwt.GetClaim("events").Value);
            Assert.IsTrue(events.RootElement.TryGetProperty("http://schemas.openid.net/event/backchannel-logout", out _));
        }
        // 故障注入只改变调度输入，终态与人工重试仍由正式 CAS SQL 完成。
        await commands.ExecuteAsync(new SqlStatement("test.logout_last_attempt", """
            UPDATE fn_identity_oidc_logout_delivery SET StatusKey = 'pending', AttemptCount = 7,
                NextAttemptAtUtc = @Now, DeliveredAtUtc = NULL WHERE Id = @Id
            """, SqlDataScope.HostOnly), IdentitySqlParameters.Create(("Id", rows.Single().Id), ("Now", clock.UtcNow)));
        receiver.Succeed = false; Assert.AreEqual(0, await delivery.RunOnceAsync(default));
        var terminal = (await queries.QueryAsync<IdentityOidcLogoutDeliveryRow>(provider == DatabaseProvider.MySql ? IdentityOidcLogoutSql.ListMySql : IdentityOidcLogoutSql.ListSqlServer)).Single();
        Assert.AreEqual("failed", terminal.StatusKey); Assert.AreEqual(8, terminal.AttemptCount);
        Assert.AreEqual(1, await commands.ExecuteAsync(IdentityOidcLogoutSql.Retry,
            IdentitySqlParameters.Create(("Id", terminal.Id), ("Now", clock.UtcNow))));
        Assert.AreEqual(0, await commands.ExecuteAsync(IdentityOidcLogoutSql.Retry,
            IdentitySqlParameters.Create(("Id", terminal.Id), ("Now", clock.UtcNow))));
        receiver.Succeed = true; Assert.AreEqual(1, await delivery.RunOnceAsync(default));
        var sent = receiver.Tokens.Count;
        await commands.ExecuteAsync(new SqlStatement("test.logout_crashed_final_attempt", """
            UPDATE fn_identity_oidc_logout_delivery SET StatusKey = 'pending', AttemptCount = 8,
                LeaseId = @Lease, LeaseUntilUtc = @Before, NextAttemptAtUtc = @Before, DeliveredAtUtc = NULL WHERE Id = @Id
            """, SqlDataScope.HostOnly), IdentitySqlParameters.Create(("Id", terminal.Id),
                ("Lease", Guid.CreateVersion7()), ("Before", clock.UtcNow.AddMinutes(-1))));
        Assert.AreEqual(0, await delivery.RunOnceAsync(default)); Assert.AreEqual(sent, receiver.Tokens.Count);
        var exhausted = (await queries.QueryAsync<IdentityOidcLogoutDeliveryRow>(provider == DatabaseProvider.MySql ? IdentityOidcLogoutSql.ListMySql : IdentityOidcLogoutSql.ListSqlServer)).Single();
        Assert.AreEqual("failed", exhausted.StatusKey); Assert.AreEqual(8, exhausted.AttemptCount); Assert.IsNull(exhausted.LeaseId);
        // 全设备路径以同一时间参数冻结新撤销中心，双库精度不能让通知目标丢失。
        var next = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(browser,
            IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri, null,
            "admin", FullNetApiFactory.TestPassword, true);
        var userId = Guid.Parse(IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(next.IdToken!, "sub")!);
        Assert.IsTrue(await transaction.ExecuteAsync(ct => sessions.RevokeAllCenterSessionsByUserAsync(userId, ct), default) > 0);
        var allPayloads = await queries.QueryAsync<byte[]>(new SqlStatement("test.all_logout_outbox", "SELECT Payload FROM fn_outbox_message WHERE MessageType = @Type", SqlDataScope.HostOnly),
            IdentitySqlParameters.Create(("Type", IdentityOidcLogoutRequested.EventType)));
        Assert.AreEqual(2, allPayloads.Count);
    }

    private sealed class DeliveryClock(DateTimeOffset now) : IClock { public DateTimeOffset Now { get; set; } = now; public DateTimeOffset UtcNow => Now; }
    private sealed class LogoutReceiver : HttpMessageHandler, IHttpClientFactory
    {
        public bool Succeed;
        public List<string> Tokens { get; } = [];
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual("https://rp.example/backchannel", request.RequestUri!.AbsoluteUri);
            var form = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            Tokens.Add(form["logout_token"].ToString());
            return new HttpResponseMessage(Succeed ? HttpStatusCode.NoContent : HttpStatusCode.ServiceUnavailable);
        }
    }
}
