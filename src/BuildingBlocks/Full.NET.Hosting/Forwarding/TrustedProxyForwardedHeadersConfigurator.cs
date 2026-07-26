using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Forwarding;

internal sealed class TrustedProxyForwardedHeadersConfigurator(
    IOptions<TrustedProxyOptions> options) :
    IConfigureOptions<ForwardedHeadersOptions>
{
    public void Configure(ForwardedHeadersOptions forwardedHeaders)
    {
        var settings = options.Value;

        // 框架默认信任 loopback；必须先清空，确保信任来源只来自 Full.NET 显式配置。
        forwardedHeaders.KnownProxies.Clear();
        forwardedHeaders.KnownIPNetworks.Clear();
        forwardedHeaders.ForwardedHeaders = ForwardedHeaders.None;
        forwardedHeaders.ForwardLimit = settings.ForwardLimit;
        if (!settings.Enabled)
        {
            return;
        }

        forwardedHeaders.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        foreach (var proxy in settings.KnownProxies)
        {
            forwardedHeaders.KnownProxies.Add(IPAddress.Parse(proxy));
        }

        foreach (var network in settings.KnownNetworks)
        {
            forwardedHeaders.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    }
}
