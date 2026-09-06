using System.Collections.Concurrent;

namespace Full.NET.Modules.Ai.Streaming;

/// <summary>跟踪进行中的聊天生成，供取消端点触发 CancellationToken。</summary>
internal sealed class AiChatGenerationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _active = new();

    /// <summary>注册新的生成任务并返回合并后的取消令牌。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="requestAborted">HTTP 请求断开取消令牌。</param>
    /// <returns>可用于推理调用的取消令牌。</returns>
    public CancellationToken Register(Guid sessionId, CancellationToken requestAborted)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        _active.AddOrUpdate(
            sessionId,
            linked,
            (_, existing) =>
            {
                existing.Cancel();
                existing.Dispose();
                return linked;
            });
        return linked.Token;
    }

    /// <summary>尝试取消指定会话的生成。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>是否找到并触发取消。</returns>
    public bool TryCancel(Guid sessionId)
    {
        if (!_active.TryRemove(sessionId, out var source))
        {
            return false;
        }

        source.Cancel();
        source.Dispose();
        return true;
    }

    /// <summary>生成结束后移除注册项。</summary>
    /// <param name="sessionId">会话标识。</param>
    public void Unregister(Guid sessionId)
    {
        if (_active.TryRemove(sessionId, out var source))
        {
            source.Dispose();
        }
    }
}
