using Full.NET.Realtime.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Realtime;

[TestClass]
public sealed class RealtimeBackplaneRegistrationTests
{
    [TestMethod]
    public void Redis_configuration_keeps_reconnect_enabled_and_scopes_channels()
    {
        var configuration = RealtimeRedisConfiguration.Create(
            "127.0.0.1:6379,abortConnect=true",
            "Production");

        Assert.IsFalse(configuration.AbortOnConnectFail);
        Assert.AreEqual(
            "fullnet:production:signalr:",
            configuration.ChannelPrefix.ToString());
    }

    [TestMethod]
    public void Dedicated_backplane_registers_a_ready_health_check()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFullNetRealtimeSignalR(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{RealtimeOptions.SectionName}:HubPath"] =
                        "/custom/notifications",
                    [$"{RealtimeOptions.SectionName}:RedisBackplaneConnectionString"] =
                        "127.0.0.1:6379",
                })
                .Build(),
            "Testing");

        using var provider = services.BuildServiceProvider();
        Assert.AreEqual(
            "/custom/notifications",
            provider.GetRequiredService<IOptions<RealtimeOptions>>()
                .Value
                .HubPath);
        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;

        Assert.IsTrue(registrations.Any(registration =>
            registration.Name == "realtime-backplane"
            && registration.Tags.Contains("ready")));
    }

    [TestMethod]
    public void Invalid_hub_paths_fail_during_realtime_registration()
    {
        string[] invalidHubPaths =
        [
            "",
            "hubs/notifications",
            "/hubs/notifications?tenant=host",
            "/hubs/notifications#fragment",
            "/hubs/notifi cations",
        ];

        foreach (var hubPath in invalidHubPaths)
        {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{RealtimeOptions.SectionName}:HubPath"] = hubPath,
                })
                .Build();

            var exception = Assert.ThrowsExactly<OptionsValidationException>(
                () => services.AddFullNetRealtimeSignalR(
                    configuration,
                    "Testing"),
                $"HubPath '{hubPath}' 必须在服务注册期间被拒绝。");

            CollectionAssert.Contains(
                exception.Failures.ToArray(),
                "Realtime:HubPath must be an absolute path without whitespace, a query string, or a fragment.");
        }
    }
}
