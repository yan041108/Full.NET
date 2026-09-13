using System.Net;
using System.Net.Sockets;

namespace Full.NET.AgenticWeb.Mcp.Client;

/// <summary>MCP 出站端点策略；沿用 Provider 私网/环回约束，禁止模型指定任意 URL。</summary>
internal sealed class McpEndpointPolicy
{
    private readonly HashSet<string> allowedOrigins;
    private static readonly System.Net.IPNetwork[] DeniedV4 = Networks(
        "0.0.0.0/8", "100.64.0.0/10", "169.254.0.0/16", "192.0.0.0/24", "192.0.2.0/24",
        "192.88.99.0/24", "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4",
        "168.63.129.16/32");
    private static readonly System.Net.IPNetwork[] PrivateV4 = Networks("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8");
    private static readonly System.Net.IPNetwork[] DeniedV6 = Networks("2001::/23", "2001:db8::/32", "2002::/16", "3fff::/20", "fd00:ec2::254/128");
    private static readonly System.Net.IPNetwork GlobalV6 = System.Net.IPNetwork.Parse("2000::/3");
    private static readonly System.Net.IPNetwork PrivateV6 = System.Net.IPNetwork.Parse("fc00::/7");

    internal McpEndpointPolicy(IEnumerable<string> approvedOrigins)
    {
        allowedOrigins = new(StringComparer.Ordinal);
        foreach (var origin in approvedOrigins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
                || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            {
                throw new InvalidOperationException("MCP network policy requires exact HTTP origins.");
            }

            if (IPAddress.TryParse(uri.DnsSafeHost, out var address) && !IsAllowedAddress(address, true))
            {
                throw new InvalidOperationException("MCP network policy cannot allow a reserved address.");
            }

            allowedOrigins.Add(Origin(uri));
        }
    }

    internal void ValidateEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("https" or "http") || endpoint.UserInfo.Length != 0)
        {
            throw Blocked();
        }

        var approved = allowedOrigins.Contains(Origin(endpoint));
        if (endpoint.Scheme != "https" && !approved)
        {
            throw Blocked();
        }

        if (IPAddress.TryParse(endpoint.DnsSafeHost, out var literal) && !IsAllowedAddress(literal, approved))
        {
            throw Blocked();
        }
    }

    internal static bool IsAllowedAddress(IPAddress address, bool approved)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !DeniedV4.Any(range => range.Contains(address))
                && (approved || !PrivateV4.Any(range => range.Contains(address)));
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.ScopeId != 0
            || DeniedV6.Any(range => range.Contains(address)))
        {
            return false;
        }

        if (IPAddress.IsLoopback(address) || PrivateV6.Contains(address))
        {
            return approved;
        }

        return GlobalV6.Contains(address);
    }

    private static HttpRequestException Blocked() => new("MCP destination is blocked by host network policy.");
    private static string Origin(Uri uri) => uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    private static System.Net.IPNetwork[] Networks(params string[] values) =>
        values.Select(System.Net.IPNetwork.Parse).ToArray();
}
