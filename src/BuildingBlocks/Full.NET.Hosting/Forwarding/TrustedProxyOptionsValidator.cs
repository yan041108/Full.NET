using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Forwarding;

internal sealed class TrustedProxyOptionsValidator : IValidateOptions<TrustedProxyOptions>
{
    private const int MaximumForwardLimit = 10;
    private static readonly IPAddress Ipv4MappedNetworkAddress =
        IPAddress.Parse("::ffff:0.0.0.0");

    public ValidateOptionsResult Validate(string? name, TrustedProxyOptions options)
    {
        if (options.ForwardLimit is < 1 or > MaximumForwardLimit)
        {
            return ValidateOptionsResult.Fail(
                $"{TrustedProxyOptions.SectionName}:ForwardLimit must be between 1 and "
                + $"{MaximumForwardLimit}.");
        }

        var knownProxies = options.KnownProxies ?? [];
        var knownNetworks = options.KnownNetworks ?? [];
        if (!options.Enabled)
        {
            return knownProxies.Length == 0 && knownNetworks.Length == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    $"{TrustedProxyOptions.SectionName}:Enabled must be true when trusted "
                    + "proxies or networks are configured.");
        }

        if (knownProxies.Length == 0 && knownNetworks.Length == 0)
        {
            return ValidateOptionsResult.Fail(
                $"{TrustedProxyOptions.SectionName} requires at least one trusted proxy "
                + "or network when enabled.");
        }

        foreach (var proxy in knownProxies)
        {
            if (string.IsNullOrWhiteSpace(proxy) || !IPAddress.TryParse(proxy, out _))
            {
                return ValidateOptionsResult.Fail(
                    $"{TrustedProxyOptions.SectionName}:KnownProxies contains an invalid "
                    + $"IP address: '{proxy}'.");
            }
        }

        foreach (var network in knownNetworks)
        {
            if (string.IsNullOrWhiteSpace(network)
                || !IPNetwork.TryParse(network, out var parsedNetwork))
            {
                return ValidateOptionsResult.Fail(
                    $"{TrustedProxyOptions.SectionName}:KnownNetworks contains an invalid "
                    + $"CIDR network: '{network}'.");
            }

            if (parsedNetwork.PrefixLength == 0
                || CoversEntireIpv4MappedAddressSpace(parsedNetwork))
            {
                return ValidateOptionsResult.Fail(
                    $"{TrustedProxyOptions.SectionName}:KnownNetworks must not trust an "
                    + "entire address family or the entire IPv4-mapped address space.");
            }
        }

        return ValidateOptionsResult.Success;
    }

    // IPNetwork.Contains 会对 mapped 地址执行地址族归一化，这里按原始 IPv6 前缀判断，
    // 避免 ::ffff:0:0/96 及其超网退化为“信任全部 IPv4 来源”。
    private static bool CoversEntireIpv4MappedAddressSpace(IPNetwork network) =>
        network.BaseAddress.AddressFamily == AddressFamily.InterNetworkV6
        && network.PrefixLength <= 96
        && PrefixMatches(
            network.BaseAddress.GetAddressBytes(),
            Ipv4MappedNetworkAddress.GetAddressBytes(),
            network.PrefixLength);

    private static bool PrefixMatches(
        ReadOnlySpan<byte> left,
        ReadOnlySpan<byte> right,
        int prefixLength)
    {
        var wholeBytes = prefixLength / 8;
        if (!left[..wholeBytes].SequenceEqual(right[..wholeBytes]))
        {
            return false;
        }

        var remainingBits = prefixLength % 8;
        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(byte.MaxValue << (8 - remainingBits));
        return (left[wholeBytes] & mask) == (right[wholeBytes] & mask);
    }
}
