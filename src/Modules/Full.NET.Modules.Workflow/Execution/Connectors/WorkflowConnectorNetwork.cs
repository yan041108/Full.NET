using System.Net;
using System.Net.Sockets;

namespace Full.NET.Modules.Workflow.Execution.Connectors;

/// <summary>实际连接只使用整组校验后的IP；不允许代理、二次DNS解析或任意重定向接管凭据。</summary>
internal sealed class WorkflowConnectorNetwork(Func<string, CancellationToken, Task<IPAddress[]>> resolve,
    Func<IPEndPoint, CancellationToken, ValueTask<Stream>> connect)
{
    private static readonly IPNetwork[] BlockedV4 = [
        IPNetwork.Parse("0.0.0.0/8"), IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"), IPNetwork.Parse("169.254.0.0/16"), IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"), IPNetwork.Parse("192.168.0.0/16"), IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("224.0.0.0/4"), IPNetwork.Parse("240.0.0.0/4"), IPNetwork.Parse("168.63.129.16/32")];

    public static bool AllowsTarget(Uri target)
    {
        if (!target.IsAbsoluteUri || target.Scheme != "https" || target.UserInfo.Length != 0 || target.Fragment.Length != 0) return false;
        var host = target.IdnHost.Trim('[', ']').TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)) return false;
        return !IPAddress.TryParse(host, out var address) || AllowsAddress(address);
    }

    public static bool AllowsAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return IPNetwork.Parse("2000::/3").Contains(address) && !IPNetwork.Parse("2002::/16").Contains(address);
        return address.AddressFamily == AddressFamily.InterNetwork && !BlockedV4.Any(range => range.Contains(address));
    }

    public async ValueTask<Stream> ConnectAsync(Uri target, DnsEndPoint endpoint, CancellationToken token)
    {
        if (!AllowsTarget(target) || target.Port != endpoint.Port ||
            !string.Equals(target.IdnHost.Trim('[', ']').TrimEnd('.'), endpoint.Host.Trim('[', ']').TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
            throw new WorkflowConnectorDestinationException();
        var host = endpoint.Host.Trim('[', ']');
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await resolve(host, token).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(address => !AllowsAddress(address))) throw new WorkflowConnectorDestinationException();
        foreach (var address in addresses)
        {
            token.ThrowIfCancellationRequested();
            try { return await connect(new IPEndPoint(address, endpoint.Port), token).ConfigureAwait(false); }
            catch (SocketException) { }
        }
        throw new HttpRequestException("Workflow connector destination is unavailable.");
    }

    public static async ValueTask<Stream> ConnectSocketAsync(IPEndPoint endpoint, CancellationToken token)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try { await socket.ConnectAsync(endpoint, token).ConfigureAwait(false); return new NetworkStream(socket, ownsSocket: true); }
        catch { socket.Dispose(); throw; }
    }
}

/// <summary>此错误发生在HTTP数据发送前，可确定目的地策略拒绝，不能误作远端业务已生效。</summary>
internal sealed class WorkflowConnectorDestinationException : IOException
{
    public WorkflowConnectorDestinationException() : base("Workflow connector destination is blocked.") { }
}
