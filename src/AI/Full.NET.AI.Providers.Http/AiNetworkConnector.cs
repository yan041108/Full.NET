using System.Net;
using System.Net.Sockets;

namespace Full.NET.AI.Providers.Http;

/// <summary>单次解析、整组校验、按 IP 连接；不能把已校验主机名再次交给套接字解析。</summary>
internal sealed class AiNetworkConnector(AiEndpointPolicy policy, bool ollama,
    Func<string, CancellationToken, Task<IPAddress[]>> resolve,
    Func<IPEndPoint, CancellationToken, ValueTask<Stream>> connect)
{
    internal async ValueTask<Stream> ConnectAsync(Uri uri, DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var approved = policy.ValidateUri(uri, ollama);
        if (!string.Equals(uri.IdnHost.Trim('[', ']'), endpoint.Host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase)
            || uri.Port != endpoint.Port) throw AiEndpointPolicy.Blocked();
        var addresses = IPAddress.TryParse(endpoint.Host.Trim('[', ']'), out var literal)
            ? [literal] : await resolve(endpoint.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(address => !AiEndpointPolicy.IsAllowedAddress(address, approved)))
            throw AiEndpointPolicy.Blocked();
        // 明文例外仅限内网：已批准的域名重绑定到公网也不能发送 HTTP 请求。
        if (uri.Scheme == "http" && addresses.Any(address => AiEndpointPolicy.IsAllowedAddress(address, false)))
            throw AiEndpointPolicy.Blocked();
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await connect(new IPEndPoint(address, endpoint.Port), cancellationToken).ConfigureAwait(false); }
            catch (SocketException) { }
        }
        throw new HttpRequestException("AI destination could not be connected.");
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
