using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 使用 SQL Server sp_getapplock / MySQL GET_LOCK 的会话锁实现跨实例互斥。
/// </summary>
internal sealed class SessionAppLockWorkspaceLockBackend(
    IDatabaseSessionLock sessionLock) : ICodeGenerationWorkspaceLockBackend
{
    /// <summary>
    /// 委托数据库会话锁尝试获取；竞争失败时返回 null，由 ApplyGate 决定是否拒绝请求，不在此处自旋等待。
    /// </summary>
    public Task<IAsyncDisposable?> TryAcquireAsync(
        string lockResource,
        CancellationToken cancellationToken) =>
        sessionLock.TryAcquireAsync(lockResource, cancellationToken);
}
