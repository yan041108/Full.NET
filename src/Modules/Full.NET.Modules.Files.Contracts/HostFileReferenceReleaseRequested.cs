using MemoryPack;

namespace Full.NET.Modules.Files.Contracts;

/// <summary>引用所有者与解除绑定在同一事务提交的释放意图；只接受内部可靠事件，不提供 HTTP 写入口。</summary>
/// <param name="IdempotencyKey">绑定时已建立的稳定 Claim 身份。</param>
[MemoryPackable]
public sealed partial record HostFileReferenceReleaseRequested(string IdempotencyKey)
{
    /// <summary>持久化事件类型，释放消费者可以安全重放。</summary>
    public const string EventType = "fullnet.files.reference.release_requested";
}
