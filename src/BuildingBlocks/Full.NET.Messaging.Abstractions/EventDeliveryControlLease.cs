namespace Full.NET.Messaging.Abstractions;

/// <summary>控制面执行能力；每次外部变更与终态提交前重新确认原会话仍持锁。</summary>
/// <param name="verify">只能校验原锁，不能通过重新获取锁延续旧执行者。</param>
public sealed class EventDeliveryControlLease(Func<CancellationToken, Task> verify)
{
    /// <summary>失锁、取消或无法确认时抛出异常；不保证撤回已经发送的 REST。</summary>
    public Task EnsureHeldAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return verify(cancellationToken);
    }
}
