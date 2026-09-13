using System.Net.Http.Headers;
using ModelContextProtocol.Client;
using Microsoft.Extensions.Options;

namespace Full.NET.AgenticWeb.Mcp.Client;

/// <summary>按连接复用 MCP 客户端；只使用服务凭据，不透传用户访问令牌。</summary>
public sealed class McpClientConnectionManager : IAsyncDisposable
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);
    private readonly object sync = new();
    private readonly Dictionary<Guid, Task<OwnedClient>> clients = new();
    private readonly CancellationTokenSource shutdown = new();
    private Task? disposeTask;
    private bool disposed;
    private readonly McpEndpointPolicy endpointPolicy;
    private readonly IHttpClientFactory httpClientFactory;

    /// <summary>创建作用域拥有的连接管理器。</summary>
    /// <param name="httpClientFactory">提供受控 HTTP 客户端。</param>
    /// <param name="options">出站端点策略。</param>
    public McpClientConnectionManager(IHttpClientFactory httpClientFactory, IOptions<McpClientOptions> options)
    {
        this.httpClientFactory = httpClientFactory;
        endpointPolicy = new McpEndpointPolicy(options.Value.ApprovedOrigins);
    }

    /// <summary>复用同一连接的初始化任务；返回的客户端由本管理器释放。</summary>
    /// <param name="connectionId">作用域内的连接标识。</param>
    /// <param name="endpoint">受策略约束的远端地址。</param>
    /// <param name="serviceAccessToken">服务凭据，不得传入用户令牌。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    public async ValueTask<McpClient> GetClientAsync(
        Guid connectionId,
        Uri endpoint,
        string serviceAccessToken,
        CancellationToken cancellationToken)
    {
        endpointPolicy.ValidateEndpoint(endpoint);
        cancellationToken.ThrowIfCancellationRequested();
        Task<OwnedClient> pending;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            // 创建也在锁内，避免 GetOrAdd 工厂重复启动后丢失未入选客户端的所有权。
            if (!clients.TryGetValue(connectionId, out pending!) || pending.IsFaulted || pending.IsCanceled)
            {
                pending = ConnectAsync(endpoint, serviceAccessToken);
                clients[connectionId] = pending;
            }
        }
        try
        {
            return (await pending.WaitAsync(cancellationToken).ConfigureAwait(false)).Client;
        }
        catch
        {
            lock (sync)
            {
                // 仅删除失败的这一代；单个等待方取消不能丢弃仍在初始化的连接。
                if ((pending.IsFaulted || pending.IsCanceled)
                    && clients.TryGetValue(connectionId, out var current) && ReferenceEquals(current, pending))
                    clients.Remove(connectionId);
            }
            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposeTask is not null) return new ValueTask(disposeTask);
            disposed = true;
            var owned = clients.Values.ToArray();
            clients.Clear();
            disposeTask = DisposeClientsAsync(owned);
            return new ValueTask(disposeTask);
        }
    }

    private async Task DisposeClientsAsync(Task<OwnedClient>[] owned)
    {
        await shutdown.CancelAsync().ConfigureAwait(false);
        foreach (var entry in owned)
        {
            try
            {
                var connection = await entry.ConfigureAwait(false);
                try
                {
                    await connection.Client.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    // McpClient 只拥有会话；创建它的传输仍由管理器持有并释放。
                    await connection.Transport.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // 连接清理失败不能影响作用域释放。
            }
        }

        shutdown.Dispose();
    }

    private async Task<OwnedClient> ConnectAsync(Uri endpoint, string serviceAccessToken)
    {
        var httpClient = httpClientFactory.CreateClient(nameof(McpClientConnectionManager));
        HttpClientTransport? transport = null;
        try
        {
            // 初始化由作用域和独立截止时间拥有，单个调用方只取消自己的等待。
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            lifetime.CancelAfter(ConnectionTimeout);
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceAccessToken);
            transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = endpoint }, httpClient, ownsHttpClient: true);
            var client = await McpClient.CreateAsync(transport, cancellationToken: lifetime.Token).ConfigureAwait(false);
            return new OwnedClient(client, transport);
        }
        catch
        {
            // 初始化失败时尚无 McpClient 接管资源；必须在这里释放传输和 HTTP 客户端。
            if (transport is not null) await transport.DisposeAsync().ConfigureAwait(false);
            else httpClient.Dispose();
            throw;
        }
    }

    private sealed record OwnedClient(McpClient Client, HttpClientTransport Transport);
}

/// <summary>MCP 客户端出站策略配置。</summary>
public sealed class McpClientOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:Ai:Mcp:Client";

    /// <summary>允许精确匹配的 MCP 端点源；测试与内网回环必须显式登记。</summary>
    public string[] ApprovedOrigins { get; set; } = [];
}
