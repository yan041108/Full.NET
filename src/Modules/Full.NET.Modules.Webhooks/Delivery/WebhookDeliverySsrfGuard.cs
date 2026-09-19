using System.Net;
using System.Net.Sockets;

namespace Full.NET.Modules.Webhooks.Delivery;

/// <summary>Webhook 投递前 DNS 解析 SSRF 防护，阻断私网、环回与元数据地址。</summary>
internal static class WebhookDeliverySsrfGuard
{
    private static readonly string[] BlockedHostSuffixes =
    [
        ".internal",
        ".local",
    ];

    public static async Task<(bool Allowed, string? Reason)> ValidateAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        if (uri.Scheme is not "https")
        {
            return (false, "Only https URLs are allowed.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return (false, "URL user credentials are not allowed.");
        }

        if (WebhookTargetUrlPolicy.IsBlocked(uri))
        {
            return (false, "Webhook target URL is blocked.");
        }

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            return WebhookTargetUrlPolicy.IsBlocked(uri)
                ? (false, "Webhook target URL is blocked.")
                : (true, null);
        }

        foreach (var suffix in BlockedHostSuffixes)
        {
            if (uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return (false, "URL host suffix is blocked.");
            }
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken)
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
            if (WebhookTargetUrlPolicy.IsBlocked(new Uri($"https://{address}/")))
            {
                return (false, "Webhook target URL resolves to a blocked address.");
            }
        }

        return (true, null);
    }
}