using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Retention;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Full.NET.UnitTests.Identity;

/// <summary>认证事件清理循环仅属于 Worker，API 即使启用保留配置也不启动清理。</summary>
[TestClass]
public sealed class AuthenticationEventRetentionRegistrationTests
{
    [TestMethod]
    public void Api_does_not_register_cleanup_hosted_service()
    {
        var services = new ServiceCollection();
        new IdentityModule().AddServices(services, Configuration());
        Assert.IsFalse(HasCleanupHostedService(services));
    }

    [TestMethod]
    public void Worker_registers_cleanup_hosted_service()
    {
        var services = new ServiceCollection();
        new IdentityModule().AddBackgroundServices(services, Configuration());
        Assert.IsTrue(HasCleanupHostedService(services));
    }

    private static bool HasCleanupHostedService(IServiceCollection services) =>
        services.Any(item => item.ServiceType == typeof(IHostedService)
            && item.ImplementationType == typeof(AuthenticationEventRetentionHostedProcessor));

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:AuthenticationEvents:Retention:Enabled"] = "true"
        })
        .Build();
}
