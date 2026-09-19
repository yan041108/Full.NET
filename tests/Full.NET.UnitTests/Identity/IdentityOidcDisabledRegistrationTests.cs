using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.DependencyInjection;
using Full.NET.Modules.Identity.Features.ChangeSessionContext;
using Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Security;
using Full.NET.Realtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityOidcDisabledRegistrationTests
{
    [TestMethod]
    public async Task Disabled_oidc_keeps_legacy_context_service_resolvable_and_rejects_invalid_identity()
    {
        using var provider = CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IIdentitySessionContextService>();

        var result = await service.ChangeAsync(new ClaimsPrincipal(), null);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(scope.ServiceProvider.GetService<IdentityOidcSigningKeyRing>());
        Assert.IsNull(scope.ServiceProvider.GetService<IdentityOidcContextAccessTokenIssuer>());
    }

    [TestMethod]
    public void Disabled_oidc_keeps_host_session_revocation_resolvable()
    {
        using var provider = CreateServices().BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<HostOnlineSessionManagementService>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IdentityOidcGrantRevocationService>());
    }

    [TestMethod]
    public async Task Enabled_oidc_without_issuers_rejects_before_database_access()
    {
        var services = CreateServices();
        services.Configure<IdentityOidcOptions>(options =>
        {
            options.Enable = true;
            options.Issuer = "https://identity.example.test/";
            options.AllowDevelopmentEphemeralSigningKey = true;
            options.Clients = [new IdentityOidcClientOptions
            {
                ClientId = "registration-test",
                RedirectUris = ["https://app.example.test/callback"],
            }];
        });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IIdentitySessionContextService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("iss", "https://identity.example.test/")], "oidc"));

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => service.ChangeAsync(principal, null));

        Assert.AreEqual(0, provider.GetRequiredService<ICommandExecutor>().ReceivedCalls().Count());
        Assert.AreEqual(0, provider.GetRequiredService<IQueryExecutor>().ReceivedCalls().Count());
    }

    private static ServiceCollection CreateServices()
    {
        // 使用真实 OIDC 装配，禁止测试夹具预注册本次缺失的协议依赖掩盖关闭场景。
        var services = new ServiceCollection();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        services.AddSingleton(environment);
        services.AddLogging();
        services.AddSingleton(Substitute.For<IQueryExecutor>());
        services.AddSingleton(Substitute.For<ICommandExecutor>());
        services.AddSingleton(Substitute.For<ICommandTransaction>());
        services.AddSingleton(Substitute.For<IClock>());
        services.AddSingleton(Substitute.For<IIdGenerator>());
        services.AddSingleton(Substitute.For<IPermissionSnapshotReader>());
        services.AddSingleton(Substitute.For<IAccessTokenIssuer>());
        services.AddSingleton(Substitute.For<ICurrentTenantContextWriter>());
        services.AddSingleton(Substitute.For<IRealtimePublisher>());
        services.AddSingleton(AuthorizationCatalog.Create([new IdentityAuthorizationContributor()]));
        services.AddScoped<PermissionClaimEvaluator>();
        services.AddScoped<IdentitySessionRealtimeDelivery>();
        services.AddScoped<IIdentitySessionContextService, IdentitySessionContextService>();
        services.AddScoped<HostOnlineSessionManagementService>();
        services.AddIdentityOidc(new ConfigurationBuilder().Build());
        return services;
    }
}
