using System.Net;
using System.Net.Sockets;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>HTTP 任务出站 SSRF 防护；执行前拒绝私网、环回与元数据地址。</summary>
internal static class HttpSsrfGuard
{
    private static readonly string[] BlockedHostSuffixes =
    [
        ".internal",
        ".local",
    ];

    public static async Task<(bool Allowed, string? Reason)> ValidateAsync(
        Uri uri,
        bool allowPrivateNetwork,
        CancellationToken cancellationToken)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            return (false, "Only http and https URLs are allowed.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return (false, "URL user credentials are not allowed.");
        }

        var host = uri.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return (false, "URL host is required.");
        }

        foreach (var suffix in BlockedHostSuffixes)
        {
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "URL host suffix is blocked.");
            }
        }

        if (IPAddress.TryParse(host, out var literal))
        {
            return EvaluateAddress(literal, allowPrivateNetwork);
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return (false, "URL host could not be resolved.");
        }

        if (addresses.Length == 0)
        {
            return (false, "URL host could not be resolved.");
        }

        foreach (var address in addresses)
        {
            var (allowed, reason) = EvaluateAddress(address, allowPrivateNetwork);
            if (!allowed)
            {
                return (false, reason);
            }
        }

        return (true, null);
    }

    private static (bool Allowed, string? Reason) EvaluateAddress(
        IPAddress address,
        bool allowPrivateNetwork)
    {
        if (IPAddress.IsLoopback(address)
            || IsLinkLocal(address)
            || IsMetadataAddress(address))
        {
            return allowPrivateNetwork
                ? (true, null)
                : (false, "Loopback or link-local addresses are blocked.");
        }

        if (IsPrivate(address))
        {
            return allowPrivateNetwork
                ? (true, null)
                : (false, "Private network addresses are blocked.");
        }

        return (true, null);
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return bytes.Length >= 2 && bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80;
        }

        var ipv4 = address.MapToIPv4().GetAddressBytes();
        return ipv4[0] == 169 && ipv4[1] == 254;
    }

    private static bool IsMetadataAddress(IPAddress address)
    {
        var mapped = address.MapToIPv4();
        var bytes = mapped.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254 && bytes[2] == 169 && bytes[3] == 254;
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 0xFC || bytes[0] == 0xFD;
        }

        var ipv4 = address.MapToIPv4().GetAddressBytes();
        return ipv4[0] switch
        {
            10 => true,
            127 => true,
            172 => ipv4[1] >= 16 && ipv4[1] <= 31,
            192 => ipv4[1] == 168,
            _ => false,
        };
    }
}
