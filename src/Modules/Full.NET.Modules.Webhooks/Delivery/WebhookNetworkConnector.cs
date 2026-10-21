using System.Net;
using System.Net.Sockets;

namespace Full.NET.Modules.Webhooks.Delivery;

/// <summary>连接前整组校验解析结果，并直接连接被校验的 IP，避免二次解析与代理绕过。</summary>
internal sealed class WebhookNetworkConnector(
    Func<string, CancellationToken, Task<IPAddress[]>> resolve,
    Func<IPEndPoint, CancellationToken, ValueTask<Stream>> connect)
{
    internal async ValueTask<Stream> ConnectAsync(Uri uri, DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        if (uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)
            || WebhookTargetUrlPolicy.IsBlocked(uri)
            || !string.Equals(uri.IdnHost.Trim('[', ']'), endpoint.Host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase)
            || uri.Port != endpoint.Port)
            throw new HttpRequestException("Webhook destination is blocked.");

        var addresses = IPAddress.TryParse(endpoint.Host.Trim('[', ']'), out var literal)
            ? [literal] : await resolve(endpoint.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(WebhookTargetUrlPolicy.IsBlockedAddress))
            throw new HttpRequestException("Webhook destination is blocked.");
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await connect(new IPEndPoint(address, endpoint.Port), cancellationToken).ConfigureAwait(false); }
            catch (SocketException) { }
        }
        throw new HttpRequestException("Webhook destination could not be connected.");
    }

    internal static async ValueTask<Stream> ConnectSocketAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch { socket.Dispose(); throw; }
    }
}
