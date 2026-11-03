using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
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

/// <summary>旧钥必须先覆盖合法窗口并受控退役，不能通过删除节点配置伪装完成。</summary>
internal static class IdentityOidcSigningKeyRetirementAssertions
{
    public static async Task VerifyAsync(DatabaseProvider provider, string connectionString,
        CancellationToken cancellationToken = default, bool checkStalePeer = false)
    {
        using var keyA = RSA.Create(3072); using var keyB = RSA.Create(3072);
        var clock = new RetirementClock();
        var settings = IdentityOidcSigningKeyRotationAssertions.BuildDualPrivateKeySettings(keyA, keyB, IdentityOidcSigningKeyRotationAssertions.KeyAId);
        using var primary = new FullNetApiFactory(provider, connectionString, settings,
            configureTestServices: services => services.Replace(ServiceDescriptor.Singleton<IClock>(clock)));
        await primary.InitializeAsync(cancellationToken);
        using var browser = primary.CreateClientForHost("localhost");
        var legacy = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(browser,
            IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri, null,
            "admin", FullNetApiFactory.TestPassword, requestOfflineAccess: false, cancellationToken: cancellationToken);
        using var peer = checkStalePeer ? new FullNetApiFactory(provider, connectionString, settings,
            configureTestServices: services => services.Replace(ServiceDescriptor.Singleton<IClock>(clock))) : null;
        if (peer is not null) await peer.InitializeAsync(cancellationToken);
        await IdentityOidcSigningKeyRotationAssertions.ActivatePersistedKeyAsync(primary, IdentityOidcSigningKeyRotationAssertions.KeyBId, cancellationToken);
        using (var premature = new FullNetApiFactory(provider, connectionString, BuildSinglePrivateKeySettings(keyB, IdentityOidcSigningKeyRotationAssertions.KeyBId)))
            await Assert.ThrowsAsync<InvalidOperationException>(() => premature.InitializeAsync(cancellationToken));
        await using var scope = primary.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var user = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<IdentityUserRecord>(IdentitySql.FindUserByScopeAndUsername,
            IdentitySqlParameters.Create(("ScopeKey", "host"), ("NormalizedUsername", "ADMIN")), cancellationToken);
        Assert.IsNotNull(user);
        var actor = new OidcManagementActorContext(user.Id, null, "retirement-test");
        var manager = scope.ServiceProvider.GetRequiredService<OidcSigningKeyManagementService>();
        Assert.IsFalse((await manager.RetireAsync(IdentityOidcSigningKeyRotationAssertions.KeyAId, actor, cancellationToken)).IsSuccess);
        var old = (await scope.ServiceProvider.GetRequiredService<IdentityOidcKeyGovernance>().ReadKeysAsync(cancellationToken))
            .Single(row => row.KeyId == IdentityOidcSigningKeyRotationAssertions.KeyAId);
        Assert.IsNotNull(old.RetireAfterUtc);
        clock.Now = old.RetireAfterUtc.Value.AddMinutes(1);
        Assert.IsTrue((await manager.RetireAsync(old.KeyId, actor, cancellationToken)).IsSuccess);
        // 生命周期时钟推进只用于覆盖退役门禁；恢复业务时钟，避免以会话自然过期掩盖旧钥拒绝。
        clock.Now = DateTimeOffset.UtcNow;
        if (peer is not null)
        {
            Assert.IsTrue(peer.Services.GetRequiredService<IdentityOidcSigningKeyRing>().ValidationKeys.Any(key => key.KeyId == old.KeyId));
            await using var peerScope = peer.Services.CreateAsyncScope();
            Assert.IsFalse(await peerScope.ServiceProvider.GetRequiredService<IdentityOidcKeyGovernance>().IsValidationKeyAllowedAsync(old.KeyId, cancellationToken));
            using var peerClient = peer.CreateClientForHost("localhost");
            using var denied = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
            denied.Headers.Authorization = new AuthenticationHeaderValue("Bearer", legacy.AccessToken);
            using var response = await peerClient.SendAsync(denied, cancellationToken);
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var retired = new FullNetApiFactory(provider, connectionString, BuildSinglePrivateKeySettings(keyB, IdentityOidcSigningKeyRotationAssertions.KeyBId),
            configureTestServices: services => services.Replace(ServiceDescriptor.Singleton<IClock>(clock)));
        await retired.InitializeAsync(cancellationToken);
        using var client = retired.CreateClientForHost("localhost");
        using var jwks = await client.GetAsync("/.well-known/jwks", cancellationToken);
        using var json = JsonDocument.Parse(await jwks.Content.ReadAsStringAsync(cancellationToken));
        Assert.IsFalse(json.RootElement.GetProperty("keys").EnumerateArray().Any(key => key.GetProperty("kid").GetString() == old.KeyId));
        var rotated = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(client,
            IdentityOidcRelyingPartyFixture.PublicClientId, IdentityOidcRelyingPartyFixture.PublicRedirectUri, null,
            "admin", FullNetApiFactory.TestPassword, requestOfflineAccess: false, cancellationToken: cancellationToken);
        Assert.AreEqual(IdentityOidcSigningKeyRotationAssertions.KeyBId, IdentityOidcRelyingPartyFixture.ReadJwtHeaderValue(rotated.AccessToken, "kid"));
    }

    internal static IReadOnlyDictionary<string, string?> BuildSinglePrivateKeySettings(RSA key, string keyId) =>
        new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "false", ["Identity:Oidc:ActiveSigningKeyId"] = keyId,
            [$"Identity:Oidc:SigningKeys:{keyId}:PublicKeyPem"] = key.ExportRSAPublicKeyPem(),
            [$"Identity:Oidc:SigningKeys:{keyId}:PrivateKeyPem"] = key.ExportRSAPrivateKeyPem(),
            ["Identity:Oidc:EncryptionKeyBase64"] = IdentityOidcMultiInstanceTestSupport.SharedEncryptionKeyBase64
        };
    private sealed class RetirementClock : IClock { public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow; public DateTimeOffset UtcNow => Now; }
}
