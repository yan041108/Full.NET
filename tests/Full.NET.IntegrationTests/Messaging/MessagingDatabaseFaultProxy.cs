using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>仅转发独享测试数据库的原始双向字节；断连时关闭已建立连接与新连接，不改写数据库协议。</summary>
internal sealed class MessagingDatabaseFaultProxy : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly Task _accept;
    private readonly int _upstreamPort;
    private volatile bool _disconnected;
    internal int Port { get; }

    internal MessagingDatabaseFaultProxy(int upstreamPort, bool dockerReachable = false)
    {
        _upstreamPort = upstreamPort;
        _listener = new(dockerReachable ? IPAddress.Any : IPAddress.Loopback, 0);
        _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accept = AcceptAsync();
    }

    internal void SetDisconnected(bool disconnected)
    {
        _disconnected = disconnected;
        if (disconnected) foreach (var client in _clients.Keys) client.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                if (_disconnected) { client.Dispose(); continue; }
                _connections.Add(ForwardAsync(client));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
        }
    }

    private async Task ForwardAsync(TcpClient downstream)
    {
        using (downstream)
        using (var upstream = new TcpClient())
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            _clients[downstream] = 0;
            try
            {
                await upstream.ConnectAsync(IPAddress.Loopback, _upstreamPort, lifetime.Token);
                var request = downstream.GetStream().CopyToAsync(upstream.GetStream(), lifetime.Token);
                var response = upstream.GetStream().CopyToAsync(downstream.GetStream(), lifetime.Token);
                await Task.WhenAny(request, response);
                lifetime.Cancel(); upstream.Dispose(); downstream.Dispose();
                try { await Task.WhenAll(request, response); }
                catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
            finally { _clients.TryRemove(downstream, out _); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop();
        foreach (var client in _clients.Keys) client.Dispose();
        await _accept; await Task.WhenAll(_connections); _stop.Dispose();
    }
}
