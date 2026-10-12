using System.Net;
using System.Net.Sockets;

namespace Full.NET.AgenticWeb.Mcp.Client;

/// <summary>每次新连接只解析一次；整组拒绝后不能尝试其中的其他地址或公网回退。</summary>
internal sealed class McpNetworkConnector(McpEndpointPolicy policy,
    Func<string, CancellationToken, Task<IPAddress[]>> resolve,
    Func<IPEndPoint, CancellationToken, ValueTask<Stream>> connect)
{
    internal async ValueTask<Stream> ConnectAsync(Uri uri, DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var approved = policy.ValidateEndpoint(uri);
        if (!string.Equals(uri.IdnHost.Trim('[', ']'), endpoint.Host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase)
            || uri.Port != endpoint.Port) throw McpEndpointPolicy.Blocked();
        var addresses = IPAddress.TryParse(endpoint.Host.Trim('[', ']'), out var literal)
            ? [literal] : await resolve(endpoint.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(address => !policy.IsAllowedDestination(address, approved, uri.Scheme)))
            throw McpEndpointPolicy.Blocked();
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 只能依次尝试本轮已经全部校验的 IP，不再将主机名交给底层二次解析。
            try { return await connect(new IPEndPoint(address, endpoint.Port), cancellationToken).ConfigureAwait(false); }
            catch (SocketException) { }
        }
        throw new HttpRequestException("MCP destination could not be connected.");
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
