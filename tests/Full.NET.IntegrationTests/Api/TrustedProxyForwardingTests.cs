using System.Net;
using Full.NET.Data.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class TrustedProxyForwardingTests
{
    [TestMethod]
    public async Task Disabled_forwarding_ignores_forged_client_addresses()
    {
        using var factory = CreateFactory();

        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(factory, IPAddress.Loopback, "198.51.100.1"));
        Assert.AreEqual(
            StatusCodes.Status429TooManyRequests,
            await SendHealthAsync(factory, IPAddress.Loopback, "203.0.113.2"));
    }

    [TestMethod]
    public async Task Unknown_proxy_cannot_change_client_partition()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:KnownProxies:0"] = "10.0.0.10",
        });

        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(factory, IPAddress.Loopback, "198.51.100.1"));
        Assert.AreEqual(
            StatusCodes.Status429TooManyRequests,
            await SendHealthAsync(factory, IPAddress.Loopback, "203.0.113.2"));
    }

    [TestMethod]
    public async Task Trusted_single_hop_clients_have_independent_partitions()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:KnownProxies:0"] = "127.0.0.1",
        });

        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(factory, IPAddress.Loopback, "198.51.100.1"));
        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(factory, IPAddress.Loopback, "203.0.113.2"));
        Assert.AreEqual(
            StatusCodes.Status429TooManyRequests,
            await SendHealthAsync(factory, IPAddress.Loopback, "198.51.100.1"));
    }

    [TestMethod]
    public async Task Trusted_network_clients_have_independent_partitions()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:KnownNetworks:0"] = "127.0.0.0/8",
        });

        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(factory, IPAddress.Loopback, "198.51.100.1"));
        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(factory, IPAddress.Loopback, "203.0.113.2"));
    }

    [TestMethod]
    public async Task IPv4_proxy_configuration_matches_ipv4_mapped_connection()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:KnownProxies:0"] = "127.0.0.1",
        });
        var mappedLoopback = IPAddress.Loopback.MapToIPv6();

        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(factory, mappedLoopback, "198.51.100.1"));
        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(factory, mappedLoopback, "203.0.113.2"));
    }

    [TestMethod]
    public async Task IPv4_network_configuration_matches_ipv4_mapped_connection()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:KnownNetworks:0"] = "127.0.0.0/8",
        });
        var mappedLoopback = IPAddress.Loopback.MapToIPv6();

        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(factory, mappedLoopback, "198.51.100.1"));
        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(factory, mappedLoopback, "203.0.113.2"));
    }

    [TestMethod]
    public async Task Forward_limit_ignores_left_side_of_overlong_chain()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:ForwardLimit"] = "1",
            ["TrustedProxy:KnownProxies:0"] = "127.0.0.1",
            ["TrustedProxy:KnownProxies:1"] = "192.0.2.10",
        });

        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(
                factory,
                IPAddress.Loopback,
                "198.51.100.1, 192.0.2.10"));
        Assert.AreEqual(
            StatusCodes.Status429TooManyRequests,
            await SendHealthAsync(
                factory,
                IPAddress.Loopback,
                "203.0.113.2, 192.0.2.10"));
    }

    [TestMethod]
    public async Task Trusted_two_hop_chain_resolves_real_clients()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:ForwardLimit"] = "2",
            ["TrustedProxy:KnownProxies:0"] = "127.0.0.1",
            ["TrustedProxy:KnownProxies:1"] = "192.0.2.10",
        });

        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(
                factory,
                IPAddress.Loopback,
                "198.51.100.1, 192.0.2.10"));
        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(
                factory,
                IPAddress.Loopback,
                "203.0.113.2, 192.0.2.10"));
    }

    [TestMethod]
    public async Task Trusted_ipv6_proxy_resolves_ipv6_clients()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:KnownProxies:0"] = "::1",
        });

        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(factory, IPAddress.IPv6Loopback, "2001:db8::1"));
        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(factory, IPAddress.IPv6Loopback, "2001:db8::2"));
    }

    [TestMethod]
    public async Task Invalid_forwarded_address_falls_back_to_connection_partition()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["TrustedProxy:Enabled"] = "true",
            ["TrustedProxy:KnownProxies:0"] = "127.0.0.1",
        });

        Assert.AreEqual(
            StatusCodes.Status200OK,
            await SendHealthAsync(factory, IPAddress.Loopback, "not-an-ip"));
        Assert.AreEqual(
            StatusCodes.Status429TooManyRequests,
            await SendHealthAsync(factory, IPAddress.Loopback, "still-not-an-ip"));
    }

    private static FullNetApiFactory CreateFactory(
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["RateLimiting:EnableGlobalApiLimit"] = "true",
            ["RateLimiting:GlobalApiPermitLimitPerMinute"] = "1",
        };
        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                settings[pair.Key] = pair.Value;
            }
        }

        return new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            "Server=127.0.0.1,1;Database=unused;User Id=sa;"
            + "Password=FullNet!2026Unused;TrustServerCertificate=true;Connect Timeout=1",
            settings);
    }

    private static async Task<int> SendHealthAsync(
        FullNetApiFactory factory,
        IPAddress proxyAddress,
        string forwardedFor,
        CancellationToken cancellationToken = default)
    {
        var context = await factory.Server.SendAsync(httpContext =>
        {
            httpContext.Connection.RemoteIpAddress = proxyAddress;
            httpContext.Request.Method = HttpMethods.Get;
            httpContext.Request.Scheme = "http";
            httpContext.Request.Host = new HostString("localhost");
            httpContext.Request.Path = "/health/live";
            httpContext.Request.Headers["X-Forwarded-For"] = forwardedFor;
        }, cancellationToken);
        return context.Response.StatusCode;
    }
}
