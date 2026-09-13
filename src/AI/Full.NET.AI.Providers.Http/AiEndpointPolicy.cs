using System.Net;
using System.Net.Sockets;

namespace Full.NET.AI.Providers.Http;

/// <summary>宿主批准的精确 Ollama 源只放行私网/环回；元数据与特殊用途地址永不豁免。</summary>
internal sealed class AiEndpointPolicy
{
    private readonly HashSet<string> allowedOrigins;
    private static readonly IPNetwork[] DeniedV4 = Networks(
        "0.0.0.0/8", "100.64.0.0/10", "169.254.0.0/16", "192.0.0.0/24", "192.0.2.0/24",
        "192.88.99.0/24", "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4",
        "168.63.129.16/32");
    private static readonly IPNetwork[] PrivateV4 = Networks("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8");
    private static readonly IPNetwork[] DeniedV6 = Networks("2001::/23", "2001:db8::/32", "2002::/16", "3fff::/20", "fd00:ec2::254/128");
    private static readonly IPNetwork GlobalV6 = IPNetwork.Parse("2000::/3");
    private static readonly IPNetwork PrivateV6 = IPNetwork.Parse("fc00::/7");

    internal AiEndpointPolicy(IEnumerable<string> ollamaOrigins)
    {
        allowedOrigins = new(StringComparer.Ordinal);
        foreach (var origin in ollamaOrigins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
                || uri.AbsolutePath != "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidOperationException("AI network policy requires exact HTTP origins.");
            if (IPAddress.TryParse(uri.DnsSafeHost, out var address) && !IsAllowedAddress(address, true))
                throw new InvalidOperationException("AI network policy cannot allow a reserved address.");
            allowedOrigins.Add(Origin(uri));
        }
    }

    internal bool ValidateUri(Uri uri, bool ollama)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw Blocked();
        var approved = ollama && allowedOrigins.Contains(Origin(uri));
        if (uri.Scheme != "https" && !approved) throw Blocked();
        if (IPAddress.TryParse(uri.DnsSafeHost, out var literal) && !IsAllowedAddress(literal, approved)) throw Blocked();
        return approved;
    }

    internal static bool IsAllowedAddress(IPAddress address, bool approved)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !DeniedV4.Any(range => range.Contains(address))
                && (approved || !PrivateV4.Any(range => range.Contains(address)));
        if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.ScopeId != 0
            || DeniedV6.Any(range => range.Contains(address))) return false;
        if (IPAddress.IsLoopback(address) || PrivateV6.Contains(address)) return approved;
        return GlobalV6.Contains(address);
    }

    internal static HttpRequestException Blocked() => new("AI destination is blocked by host network policy.");
    private static string Origin(Uri uri) => uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    private static IPNetwork[] Networks(params string[] values) => values.Select(IPNetwork.Parse).ToArray();
}
