using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 使用 SQL Server sp_getapplock / MySQL GET_LOCK 的会话锁实现跨实例互斥。
/// </summary>
internal sealed class SessionAppLockWorkspaceLockBackend(
    IDatabaseSessionLock sessionLock) : ICodeGenerationWorkspaceLockBackend
{
    public Task<IAsyncDisposable?> TryAcquireAsync(
        string lockResource,
        CancellationToken cancellationToken) =>
        sessionLock.TryAcquireAsync(lockResource, cancellationToken);
}
