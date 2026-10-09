using Microsoft.Extensions.AI;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>单次文本运行在模型调用边界降低请求上限；底层客户端由协调器独占并释放。</summary>
internal sealed class AiRunOutputLimitClient : IChatClient
{
    private readonly IChatClient _innerClient;
    private readonly int _maxOutputTokens;

    internal AiRunOutputLimitClient(IChatClient innerClient, long outputTokenLimit)
    {
        if (outputTokenLimit <= 0) throw new ArgumentOutOfRangeException(nameof(outputTokenLimit));
        ArgumentNullException.ThrowIfNull(innerClient);
        _innerClient = innerClient;
        _maxOutputTokens = (int)Math.Min(outputTokenLimit, 4096);
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) => _innerClient.GetResponseAsync(messages, Limit(options), cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) => _innerClient.GetStreamingResponseAsync(messages, Limit(options), cancellationToken);

    // 框架使用传入的客户端；不泄漏底层服务，防止绕过预算外观调用真实客户端。
    object? IChatClient.GetService(Type serviceType, object? serviceKey) =>
        serviceKey is null && serviceType == typeof(IChatClient) ? this : null;

    // 外观不拥有底层资源；即使执行器释放外观，协调器仍能按原有边界释放真实客户端。
    public void Dispose() { }

    private ChatOptions Limit(ChatOptions? options)
    {
        if (options?.MaxOutputTokens is <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        var limited = options?.Clone() ?? new ChatOptions();
        limited.MaxOutputTokens = Math.Min(options?.MaxOutputTokens ?? _maxOutputTokens, _maxOutputTokens);
        return limited;
    }
}
