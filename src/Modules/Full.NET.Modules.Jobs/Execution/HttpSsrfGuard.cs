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

        var (addresses, reason) = await ResolveAllowedAddressesAsync(
                uri.Host,
                allowPrivateNetwork,
                cancellationToken)
            .ConfigureAwait(false);
        return (addresses is not null, reason);
    }

    /// <summary>
    /// 在与实际连接相同的一次解析结果上完成地址校验并连接，避免校验后再次 DNS 解析产生重绑定竞态。
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endpoint,
        bool allowPrivateNetwork,
        CancellationToken cancellationToken)
    {
        var (addresses, reason) = await ResolveAllowedAddressesAsync(
                endpoint.Host,
                allowPrivateNetwork,
                cancellationToken)
            .ConfigureAwait(false);
        if (addresses is null)
        {
            throw new HttpRequestException(reason ?? "HTTP job URL is blocked.");
        }

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            try
            {
                await socket.ConnectAsync(
                        new IPEndPoint(address, endpoint.Port),
                        cancellationToken)
                    .ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                lastError = exception;
            }
        }

        throw new HttpRequestException("HTTP job host could not be connected.", lastError);
    }

    private static async Task<(IPAddress[]? Addresses, string? Reason)> ResolveAllowedAddressesAsync(
        string host,
        bool allowPrivateNetwork,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return (null, "URL host is required.");
        }

        foreach (var suffix in BlockedHostSuffixes)
        {
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return (null, "URL host suffix is blocked.");
            }
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SocketException)
            {
                return (null, "URL host could not be resolved.");
            }
        }

        if (addresses.Length == 0)
        {
            return (null, "URL host could not be resolved.");
        }

        foreach (var address in addresses)
        {
            var (allowed, reason) = EvaluateAddress(address, allowPrivateNetwork);
            if (!allowed)
            {
                return (null, reason);
            }
        }

        return (addresses, null);
    }

    private static (bool Allowed, string? Reason) EvaluateAddress(
        IPAddress address,
        bool allowPrivateNetwork)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
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
