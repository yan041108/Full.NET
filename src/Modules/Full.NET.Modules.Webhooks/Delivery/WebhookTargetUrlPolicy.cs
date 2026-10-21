using System.Net;
using System.Net.Sockets;

namespace Full.NET.Modules.Webhooks.Delivery;

/// <summary>Webhook 回调 URL 出站安全校验，阻断环回与私网字面量地址。</summary>
internal static class WebhookTargetUrlPolicy
{
    private static readonly IPNetwork[] ReservedV4 = [
        IPNetwork.Parse("0.0.0.0/8"), IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("192.0.0.0/24"), IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("224.0.0.0/4"), IPNetwork.Parse("240.0.0.0/4"),
        IPNetwork.Parse("168.63.129.16/32")];

    private static readonly string[] BlockedHostNames =
    [
        "localhost",
        "metadata.google.internal",
    ];

    internal static bool IsBlocked(Uri uri)
    {
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return true;
        }

        var host = uri.DnsSafeHost.Trim('[', ']');
        if (BlockedHostNames.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        return IsBlockedAddress(address);
    }

    internal static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (!IPNetwork.Parse("2000::/3").Contains(address) || IPNetwork.Parse("2002::/16").Contains(address)) return true;
            var bytes = address.GetAddressBytes();
            return bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80
                || bytes[0] == 0xFC
                || bytes[0] == 0xFD;
        }

        if (ReservedV4.Any(range => range.Contains(address))) return true;
        var ipv4 = address.GetAddressBytes();
        return ipv4[0] switch
        {
            10 => true,
            127 => true,
            172 => ipv4[1] >= 16 && ipv4[1] <= 31,
            192 => ipv4[1] == 168,
            169 => ipv4[1] == 254,
            _ => false,
        };
    }
}