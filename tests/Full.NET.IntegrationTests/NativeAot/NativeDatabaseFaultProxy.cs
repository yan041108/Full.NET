using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>只切断本测试 Worker 的 TCP 连接；不停止或更改共享数据库。</summary>
internal sealed class NativeDatabaseFaultProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, (TcpClient Client, TcpClient Server)> _connections = new();
    private readonly ConcurrentDictionary<int, Task> _pumps = new();
    private readonly object _gate = new();
    private readonly string _host;
    private readonly int _port;
    private Task? _accept;
    private bool _paused;
    private int _sequence;

    private NativeDatabaseFaultProxy(string host, int port) { _host = host; _port = port; }
    internal int CutConnections { get; private set; }
    internal int AcceptedConnections { get; private set; }

    internal static NativeDatabaseFaultProxy Start(DatabaseProvider provider, string original, out string proxied)
    {
        string host; int port;
        if (provider == DatabaseProvider.SqlServer)
        {
            var b = new SqlConnectionStringBuilder(original);
            var source = b.DataSource;
            if (source.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) source = source[4..];
            var parts = source.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[1], out port) || string.IsNullOrWhiteSpace(parts[0]))
                throw new InvalidOperationException("测试 SQL Server 地址必须带明确端口。");
            host = parts[0];
            // 保留原 TLS 和证书设置；若原连接要求主机名校验，禁止代理改变其可信目的地。
            if (!b.TrustServerCertificate)
                throw new InvalidOperationException("测试代理无法保持 SQL Server 的证书主机名绑定。");
            var result = new NativeDatabaseFaultProxy(host, port); result._listener.Start();
            b.DataSource = "127.0.0.1," + ((IPEndPoint)result._listener.LocalEndpoint).Port;
            proxied = b.ConnectionString; result._accept = result.AcceptAsync(); return result;
        }
        if (provider != DatabaseProvider.MySql) throw new InvalidOperationException("不支持的数据库提供程序。");
        var mysql = new MySqlConnectionStringBuilder(original);
        if (mysql.SslMode is MySqlSslMode.VerifyCA or MySqlSslMode.VerifyFull)
            throw new InvalidOperationException("测试代理不能改变 MySQL 证书主机名绑定。");
        host = mysql.Server; port = checked((int)mysql.Port);
        var proxy = new NativeDatabaseFaultProxy(host, port); proxy._listener.Start();
        mysql.Server = "127.0.0.1"; mysql.Port = checked((uint)((IPEndPoint)proxy._listener.LocalEndpoint).Port);
        proxied = mysql.ConnectionString; proxy._accept = proxy.AcceptAsync(); return proxy;
    }

    internal int PauseAndCut()
    {
        lock (_gate)
        {
            _paused = true;
            foreach (var pair in _connections.Values)
            {
                CutConnections++;
                pair.Client.Dispose(); pair.Server.Dispose();
            }
            return AcceptedConnections;
        }
    }

    internal void Resume() { lock (_gate) _paused = false; }

    internal async Task WaitForDrainAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while ((!_connections.IsEmpty || !_pumps.IsEmpty) && DateTime.UtcNow < deadline) await Task.Delay(25);
        if (!_connections.IsEmpty || !_pumps.IsEmpty) throw new TimeoutException("本测试的数据库代理连接未排空。");
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                lock (_gate) { if (_paused) { client.Dispose(); continue; } }
                var id = Interlocked.Increment(ref _sequence);
                var task = PumpAsync(id, client);
                _pumps[id] = task;
                _ = task.ContinueWith(_ => _pumps.TryRemove(id, out var ignored), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
    }

    private async Task PumpAsync(int id, TcpClient client)
    {
        using (client)
        using (var server = new TcpClient())
        {
            try
            {
                await server.ConnectAsync(_host, _port, _stop.Token);
                lock (_gate)
                {
                    if (_paused) return;
                    _connections[id] = (client, server); AcceptedConnections++;
                }
                var a = client.GetStream().CopyToAsync(server.GetStream(), _stop.Token);
                var b = server.GetStream().CopyToAsync(client.GetStream(), _stop.Token);
                await Task.WhenAny(a, b);
                client.Dispose(); server.Dispose();
                try { await Task.WhenAll(a, b); }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException) { }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException) { }
            finally { _connections.TryRemove(id, out _); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        PauseAndCut();
        if (_accept is not null) await _accept;
        await Task.WhenAll(_pumps.Values);
        _stop.Dispose();
    }
}
