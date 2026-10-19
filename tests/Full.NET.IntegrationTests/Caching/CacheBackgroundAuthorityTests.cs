using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Identity;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Caching;

/// <summary>真实会话与租户权威状态分别校验；跨实例停用及查询故障不能复用先前的后台准入结果。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CacheBackgroundAuthorityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task SqlServer_background_binding_rechecks_tenant_and_rejects_authority_failure(bool oidc) =>
        VerifyAsync(DatabaseProvider.SqlServer, oidc);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task MySql_background_binding_rechecks_tenant_and_rejects_authority_failure(bool oidc) =>
        VerifyAsync(DatabaseProvider.MySql, oidc);

    private static async Task VerifyAsync(DatabaseProvider provider, bool oidc)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var primary = new FullNetApiFactory(provider, connection,
            settingsOverrides: IdentityOidcProtocolAssertions.Settings);
        using var secondary = primary.CreateIsolatedFactory();
        await primary.InitializeAsync();
        await secondary.InitializeAsync();
        await CacheBackgroundAuthorityFixture.EnsureAnotherActiveTenantAsync(primary);
        using var client = primary.CreateClientForHost("localhost");
        var token = oidc
            ? (await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(client,
                IdentityOidcRelyingPartyFixture.PublicClientId,
                IdentityOidcRelyingPartyFixture.PublicRedirectUri, null, "admin",
                FullNetApiFactory.TestPassword, requestOfflineAccess: false)).AccessToken
            : await CacheBackgroundAuthorityFixture.LoginAsync(client, "admin");
        var tenant = await CacheBackgroundAuthorityFixture.GetAcmeTenantAsync(client, token);
        var tenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, token, tenant.Id);
        var binding = CacheBackgroundAuthorityFixture.Binding(tenantToken, oidc);
        var hostIdentity = await primary.CreateHostIdentityAsync($"background-control-{Guid.CreateVersion7():N}",
            [AiAgentRunPermissions.Read]);
        var hostBinding = CacheBackgroundAuthorityFixture.Binding(hostIdentity.AccessToken, false);

        // 长生命周期的同一验证器模拟后台领取后多次执行；另一个实例提交租户启停。
        await using var scope = secondary.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var validator = scope.ServiceProvider.GetRequiredService<IBackgroundSessionBindingValidator>();
        var authorization = scope.ServiceProvider.GetRequiredService<IBackgroundSessionAuthorization>();
        await AssertAllowedAsync(true);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await CacheBackgroundAuthorityFixture.SetActiveAsync(primary, tenant.Id, false);
            await AssertAllowedAsync(false);
            await CacheBackgroundAuthorityFixture.SetActiveAsync(primary, tenant.Id, true);
            await AssertAllowedAsync(true);
        }

        // 独立测试库只让租户权威表不可读，会话表保持正常；finally 恢复后必须重新准入。
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(
            provider == DatabaseProvider.SqlServer, connection);
        var hide = provider == DatabaseProvider.SqlServer
            ? "EXEC sp_rename 'dbo.fn_tenancy_tenant', 'fn_tenancy_tenant_authority_fault'"
            : "RENAME TABLE fn_tenancy_tenant TO fn_tenancy_tenant_authority_fault";
        var restore = provider == DatabaseProvider.SqlServer
            ? "EXEC sp_rename 'dbo.fn_tenancy_tenant_authority_fault', 'fn_tenancy_tenant'"
            : "RENAME TABLE fn_tenancy_tenant_authority_fault TO fn_tenancy_tenant";
        await db.ExecuteAsync(hide);
        try
        {
            await AssertAllowedAsync(false);
            Assert.IsTrue(await validator.IsValidAsync(hostBinding), "Host 会话不依赖租户目录，排除会话查询整体故障的假阳性。");
            Assert.IsNotNull(await authorization.AuthorizeAsync(hostBinding, AiAgentRunPermissions.Read));
        }
        finally { await db.ExecuteAsync(restore); }
        await AssertAllowedAsync(true);

        async Task AssertAllowedAsync(bool expected)
        {
            Assert.AreEqual(expected, await validator.IsValidAsync(binding), $"{provider}/{binding.SessionKind}");
            var actor = await authorization.AuthorizeAsync(binding, AiAgentRunPermissions.Read);
            Assert.AreEqual(expected, actor is not null, "超级管理员也必须先通过租户权威准入。");
        }
    }
}

/// <summary>缓存后台准入集成与 Native 场景共用真实登录、绑定解析及提交后启停。</summary>
internal static class CacheBackgroundAuthorityFixture
{
    internal static async Task EnsureAnotherActiveTenantAsync(FullNetApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        context.SetHost();
        try
        {
            var identifier = $"authority-control-{Guid.CreateVersion7():N}";
            var result = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>()
                .ProvisionAsync(new ProvisionTenantRequest(identifier, "Authority Control", $"{identifier}.localhost"));
            Assert.IsTrue(result.IsSuccess, "提供另一活动租户，保持最后活动租户保护有效。");
        }
        finally { context.Clear(); }
    }

    internal static async Task<string> LoginAsync(HttpClient client, string username)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, FullNetApiFactory.TestPassword)),
        };
        request.Headers.Add("Origin", "http://localhost");
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
    }

    internal static async Task<TenantContextSummary> GetAcmeTenantAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TenantContextSummary[]>())!.Single(tenant => tenant.Identifier == "acme");
    }

    internal static SessionBindingSnapshot Binding(string token, bool oidc)
    {
        string Claim(string name) => IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(token, name)
            ?? throw new AssertFailedException($"Missing binding claim: {name}");
        var tenantText = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(token, FullNetIdentityClaimTypes.TenantId);
        return new SessionBindingSnapshot(Guid.Parse(Claim("sub")),
            Guid.TryParse(tenantText, out var tenantId) ? tenantId : null,
            Guid.Parse(Claim(oidc ? FullNetIdentityClaimTypes.ApplicationSessionId : FullNetIdentityClaimTypes.SessionId)),
            Claim(FullNetIdentityClaimTypes.SecurityStamp), Claim(FullNetIdentityClaimTypes.ActorScope),
            Claim(FullNetIdentityClaimTypes.Scope), oidc ? SessionBindingKinds.OidcApplication : SessionBindingKinds.Refresh);
    }

    internal static async Task SetActiveAsync(FullNetApiFactory factory, Guid tenantId, bool active)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        context.SetHost();
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<HostTenantManagementService>();
            var result = active ? await service.EnableAsync(tenantId) : await service.DisableAsync(tenantId);
            Assert.IsTrue(result.IsSuccess, "启停操作必须实际提交，不能用会话撤销模拟租户变化。");
        }
        finally { context.Clear(); }
    }
}
