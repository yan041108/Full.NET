namespace Full.NET.Modules.Ai.Streaming;

/// <summary>本地取消加速器；持久化生成租约才是跨实例执行所有权的权威源。</summary>
internal sealed class AiChatGenerationRegistry
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, Guid> activeSessions = [];
    private readonly Dictionary<Guid, Registration> registrations = [];

    /// <summary>登记一代请求；被接替任务只取消，令牌源仍由其自己的 finally 释放。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="generationId">已取得数据库租约的生成标识。</param>
    /// <param name="requestAborted">请求生命周期取消令牌。</param>
    public CancellationToken Register(Guid sessionId, Guid generationId, CancellationToken requestAborted)
    {
        lock (sync)
        {
            if (registrations.ContainsKey(generationId))
                throw new InvalidOperationException("AI generation is already registered.");
            if (activeSessions.TryGetValue(sessionId, out var previous)
                && registrations.TryGetValue(previous, out var old))
                old.Source.Cancel();
            var source = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
            registrations.Add(generationId, new Registration(sessionId, source));
            activeSessions[sessionId] = generationId;
            return source.Token;
        }
    }

    /// <summary>取消当前一代任务，不移除仍在执行中的注册项。</summary>
    /// <param name="sessionId">已授权的会话标识。</param>
    /// <param name="expectedGenerationId">取消请求确认的生成标识，不能作用于后继任务。</param>
    public bool TryCancel(Guid sessionId, Guid expectedGenerationId)
    {
        lock (sync)
        {
            if (!activeSessions.TryGetValue(sessionId, out var generation) || generation != expectedGenerationId
                || !registrations.TryGetValue(generation, out var registration)) return false;
            registration.Source.Cancel();
            return true;
        }
    }

    /// <summary>只清理调用者持有的生成标识；旧任务不能删掉新一代映射。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="generationId">当前执行任务自己的生成标识。</param>
    public void Unregister(Guid sessionId, Guid generationId)
    {
        lock (sync)
        {
            if (!registrations.TryGetValue(generationId, out var registration) || registration.SessionId != sessionId) return;
            registrations.Remove(generationId);
            if (activeSessions.GetValueOrDefault(sessionId) == generationId) activeSessions.Remove(sessionId);
            registration.Source.Dispose();
        }
    }

    /// <summary>由单代执行任务持有的取消资源。</summary>
    /// <param name="SessionId">资源所属会话。</param>
    /// <param name="Source">由该代请求最终释放的令牌源。</param>
    private sealed record Registration(Guid SessionId, CancellationTokenSource Source);
}
