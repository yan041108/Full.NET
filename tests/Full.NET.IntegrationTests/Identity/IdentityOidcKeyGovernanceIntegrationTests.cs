using System.Security.Cryptography;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Features.ManageOidcSigningKeys;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>双库、多节点与可推进时钟验证轮转不能绕过材料、就绪和合法令牌保留窗口。</summary>
[TestClass]
public sealed class IdentityOidcKeyGovernanceIntegrationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Activation_requires_live_private_material_on_every_signer_and_retirement_preserves_its_window(DatabaseProvider provider)
    {
        var connection = await IdentityBrowserTestSupport.DatabaseAsync(provider); using var first = RSA.Create(2048); using var second = RSA.Create(2048);
        var clock = new GovernanceClock(); var settings = Settings(first, second); settings["Identity:Oidc:MinimumReadyApiNodes"] = "2";
        using var a = Factory(provider, connection, settings, clock); await a.InitializeAsync();
        await using var scope = a.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var policy = scope.ServiceProvider.GetRequiredService<IdentityOidcSigningPolicy>();
        Assert.IsFalse(await policy.ActivateAsync("second", default), "一个 API 节点不能满足双节点门禁。");
        var publicOnly = new Dictionary<string, string?>(settings) { ["Identity:Oidc:SigningKeys:second:PrivateKeyPem"] = "" };
        using var b = Factory(provider, connection, publicOnly, clock); await b.InitializeAsync();
        var stoppedNode = b.Services.GetRequiredService<IdentityOidcSigningNodeIdentity>().Id;
        Assert.IsFalse(await policy.ActivateAsync("second", default), "仅公钥的候选节点不得被视作可签发。");
        b.Dispose();
        await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new SqlStatement("test.drain_signing_node",
            "UPDATE fn_identity_oidc_signing_node SET ExpiresAtUtc = @Before WHERE Id = @Id", SqlDataScope.HostOnly),
            IdentitySqlParameters.Create(("Before", clock.UtcNow.AddSeconds(-1)), ("Id", stoppedNode)));
        using var c = Factory(provider, connection, settings, clock); await c.InitializeAsync();
        var user = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<IdentityUserRecord>(
            IdentitySql.FindUserByScopeAndUsername, IdentitySqlParameters.Create(("ScopeKey", "host"), ("NormalizedUsername", "ADMIN")));
        Assert.IsNotNull(user);
        // 模拟配置收紧前仍合法的长会话，退休窗口不能仅按当前七天配置计算。
        await scope.ServiceProvider.GetRequiredService<Full.NET.Abstractions.Messaging.ICommandTransaction>().ExecuteAsync(
            ct => scope.ServiceProvider.GetRequiredService<IdentityOidcSessionService>().CreateCenterSessionAsync(user.Id, user.SecurityStamp, clock.UtcNow.AddDays(21), ct), default);
        Assert.IsTrue(await policy.ActivateAsync("second", default)); await policy.SynchronizeAsync(default);
        Assert.AreEqual("second", a.Services.GetRequiredService<IdentityOidcSigningKeyRing>().ActiveSigningKeyId);
        var manager = scope.ServiceProvider.GetRequiredService<OidcSigningKeyManagementService>();
        var actor = new OidcManagementActorContext(user.Id, null, "key-governance");
        Assert.IsFalse((await manager.RetireAsync("first", actor, default)).IsSuccess, "未覆盖旧 refresh 窗口不得退役。");
        Assert.IsFalse((await manager.RetireAsync("second", actor, default)).IsSuccess, "活动密钥不得退役。");
        clock.Now = clock.Now.AddDays(8);
        Assert.IsFalse((await manager.RetireAsync("first", actor, default)).IsSuccess, "历史长会话仍在合法窗口内。");
        clock.Now = clock.Now.AddDays(14);
        Assert.IsTrue((await manager.RetireAsync("first", actor, default)).IsSuccess);
        await using var peerScope = c.Services.CreateAsyncScope(); peerScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        Assert.IsTrue(c.Services.GetRequiredService<IdentityOidcSigningKeyRing>().ValidationKeys.Any(key => key.KeyId == "first"), "对端仍持有旧公开材料，模拟尚未执行下一次探针。");
        Assert.IsFalse(await peerScope.ServiceProvider.GetRequiredService<IdentityOidcKeyGovernance>().IsValidationKeyAllowedAsync("first", default));
        using (var metadata = a.CreateClientForHost("localhost"))
        using (var response = await metadata.GetAsync("/.well-known/jwks"))
        {
            using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.IsFalse(document.RootElement.GetProperty("keys").EnumerateArray().Any(key => key.GetProperty("kid").GetString() == "first"));
        }
        Assert.IsFalse(await policy.ActivateAsync("first", default));
        Assert.IsFalse((await manager.PublishAsync("first", actor, default)).IsSuccess, "已退役 kid 不得重新发布。");
        CollectionAssert.DoesNotContain(a.Services.GetRequiredService<IdentityOidcSigningKeyRing>().ValidationKeys.Select(key => key.KeyId).ToArray(), "first");
        var restarted = new Dictionary<string, string?>(settings) { ["Identity:Oidc:ActiveSigningKeyId"] = "second" };
        restarted.Remove("Identity:Oidc:SigningKeys:first:PublicKeyPem"); restarted.Remove("Identity:Oidc:SigningKeys:first:PrivateKeyPem");
        using var afterRetirement = Factory(provider, connection, restarted, clock); await afterRetirement.InitializeAsync();
        Assert.AreEqual("second", afterRetirement.Services.GetRequiredService<IdentityOidcSigningKeyRing>().ActiveSigningKeyId);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Prepublication_window_and_immutable_kid_material_are_enforced(DatabaseProvider provider)
    {
        var connection = await IdentityBrowserTestSupport.DatabaseAsync(provider); using var first = RSA.Create(2048); using var second = RSA.Create(2048);
        var clock = new GovernanceClock(); var settings = Settings(first, second); settings["Identity:Oidc:SigningPrepublicationSeconds"] = "60";
        using var a = Factory(provider, connection, settings, clock); await a.InitializeAsync();
        await using var scope = a.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var policy = scope.ServiceProvider.GetRequiredService<IdentityOidcSigningPolicy>();
        clock.Now = clock.Now.AddSeconds(59); await scope.ServiceProvider.GetRequiredService<IdentityOidcKeyGovernance>().ReportNodeAsync(default);
        Assert.IsFalse(await policy.ActivateAsync("second", default));
        clock.Now = clock.Now.AddSeconds(2); await scope.ServiceProvider.GetRequiredService<IdentityOidcKeyGovernance>().ReportNodeAsync(default);
        Assert.IsTrue(await policy.ActivateAsync("second", default));
        using var wrongKey = RSA.Create(2048);
        var rebound = new Dictionary<string, string?>(settings)
        {
            ["Identity:Oidc:SigningKeys:second:PublicKeyPem"] = wrongKey.ExportRSAPublicKeyPem(),
            ["Identity:Oidc:SigningKeys:second:PrivateKeyPem"] = wrongKey.ExportRSAPrivateKeyPem()
        };
        using var drifted = Factory(provider, connection, rebound, clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => drifted.InitializeAsync());
    }

    private static FullNetApiFactory Factory(DatabaseProvider provider, string connection, Dictionary<string, string?> settings, GovernanceClock clock) =>
        new(provider, connection, settings, configureTestServices: services => services.Replace(ServiceDescriptor.Singleton<IClock>(clock)));
    private static Dictionary<string, string?> Settings(RSA first, RSA second) => new(IdentityOidcProtocolAssertions.Settings)
    {
        ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "false", ["Identity:Oidc:ActiveSigningKeyId"] = "first",
        ["Identity:Oidc:EncryptionKeyBase64"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        ["Identity:Oidc:SigningKeys:first:PublicKeyPem"] = first.ExportRSAPublicKeyPem(), ["Identity:Oidc:SigningKeys:first:PrivateKeyPem"] = first.ExportRSAPrivateKeyPem(),
        ["Identity:Oidc:SigningKeys:second:PublicKeyPem"] = second.ExportRSAPublicKeyPem(), ["Identity:Oidc:SigningKeys:second:PrivateKeyPem"] = second.ExportRSAPrivateKeyPem()
    };
    private sealed class GovernanceClock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow; public DateTimeOffset UtcNow => Now; }
}
