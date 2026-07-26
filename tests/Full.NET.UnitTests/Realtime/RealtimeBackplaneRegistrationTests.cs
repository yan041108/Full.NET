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
                    [$"{RealtimeOptions.SectionName}:RedisBackplaneConnectionString"] =
                        "127.0.0.1:6379",
                })
                .Build(),
            "Testing");

        using var provider = services.BuildServiceProvider();
        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;

        Assert.IsTrue(registrations.Any(registration =>
            registration.Name == "realtime-backplane"
            && registration.Tags.Contains("ready")));
    }
}
