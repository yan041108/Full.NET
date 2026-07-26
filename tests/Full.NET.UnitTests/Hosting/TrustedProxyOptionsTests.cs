using System.Net;
using Full.NET.Hosting.Forwarding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class TrustedProxyOptionsTests
{
    [TestMethod]
    public void Empty_configuration_disables_forwarding_and_removes_framework_trust_defaults()
    {
        using var services = BuildServiceProvider(new Dictionary<string, string?>());

        var options = services.GetRequiredService<IOptions<TrustedProxyOptions>>().Value;
        var forwarded = services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.IsFalse(options.Enabled);
        Assert.AreEqual(ForwardedHeaders.None, forwarded.ForwardedHeaders);
        Assert.AreEqual(0, forwarded.KnownProxies.Count);
        Assert.AreEqual(0, forwarded.KnownIPNetworks.Count);
    }

    [TestMethod]
    public void Enabled_configuration_maps_only_explicit_client_address_and_protocol_headers()
    {
        using var services = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:ForwardLimit"] = "2",
            ["TrustedProxy:KnownProxies:0"] = "127.0.0.1",
            ["TrustedProxy:KnownProxies:1"] = "::1",
            ["TrustedProxy:KnownNetworks:0"] = "10.0.0.0/8",
            ["TrustedProxy:KnownNetworks:1"] = "fd00::/8",
        });

        var forwarded = services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.AreEqual(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            forwarded.ForwardedHeaders);
        Assert.AreEqual(2, forwarded.ForwardLimit);
        Assert.IsTrue(forwarded.KnownProxies.Contains(IPAddress.Parse("127.0.0.1")));
        Assert.IsTrue(forwarded.KnownProxies.Contains(IPAddress.Parse("::1")));
        Assert.IsTrue(
            forwarded.KnownIPNetworks.Contains(System.Net.IPNetwork.Parse("10.0.0.0/8")));
        Assert.IsTrue(
            forwarded.KnownIPNetworks.Contains(System.Net.IPNetwork.Parse("fd00::/8")));
    }

    [TestMethod]
    public void Enabled_configuration_requires_at_least_one_trusted_source()
    {
        using var services = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
        });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<TrustedProxyOptions>>().Value);
    }

    [TestMethod]
    public void Disabled_configuration_rejects_silently_ignored_trusted_sources()
    {
        using var services = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["TrustedProxy:KnownProxies:0"] = "127.0.0.1",
        });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<TrustedProxyOptions>>().Value);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("11")]
    public void Forward_limit_outside_supported_range_is_rejected(string forwardLimit)
    {
        using var services = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:ForwardLimit"] = forwardLimit,
            ["TrustedProxy:KnownProxies:0"] = "127.0.0.1",
        });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<TrustedProxyOptions>>().Value);
    }

    [TestMethod]
    public void Invalid_proxy_address_is_rejected()
    {
        using var services = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:KnownProxies:0"] = "not-an-ip",
        });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<TrustedProxyOptions>>().Value);
    }

    [TestMethod]
    public void Invalid_network_is_rejected()
    {
        using var services = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:KnownNetworks:0"] = "10.0.0.0/not-a-prefix",
        });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<TrustedProxyOptions>>().Value);
    }

    [TestMethod]
    [DataRow("0.0.0.0/0")]
    [DataRow("::/0")]
    [DataRow("::/80")]
    [DataRow("::ffff:0:0/96")]
    public void Universal_network_is_rejected(string network)
    {
        using var services = BuildServiceProvider(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:KnownNetworks:0"] = network,
        });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => services.GetRequiredService<IOptions<TrustedProxyOptions>>().Value);
    }

    private static ServiceProvider BuildServiceProvider(
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddFullNetTrustedProxyForwarding(configuration);
        return services.BuildServiceProvider();
    }
}
