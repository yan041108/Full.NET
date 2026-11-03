using Microsoft.Extensions.AI;

namespace Full.NET.Modules.Ai.Runtime;

/// <summary>静态完成图禁止模型派发；定义误加入模型节点时直接失败，不能回退真实供应商。</summary>
internal sealed class AiStaticToolOnlyClient : IChatClient
{
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("静态日程完成图不允许模型调用。");
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("静态日程完成图不允许流式模型调用。");
}
